using System.Collections.Generic;
using System.IO;
using FLARE.Core;

namespace FLARE.Tests;

public class SetupApiFixtureTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "EventLog", "SetupApi");

    public static IEnumerable<object[]> Fixtures()
    {
        if (!Directory.Exists(FixturesDir)) yield break;
        foreach (var path in Directory.GetFiles(FixturesDir, "*.log"))
            yield return new object[] { Path.GetFileName(path) };
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Fixtures))]
    public void ParseSetupApiLog_RealWorldLog_MatchesExpected(string fixtureName)
    {
        var fixturePath = Path.Combine(FixturesDir, fixtureName);
        var expectedPath = fixturePath + ".expected.txt";
        var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n");

        var events = EventLogParser.ParseSetupApiLog(fixturePath, ct: TestContext.Current.CancellationToken);

        Assert.Equal(expected, FormatEvents(events));
    }

    internal static string FormatEvents(IReadOnlyList<EventLogParser.DriverInstallEvent> events)
    {
        if (events.Count == 0) return "";
        var lines = new List<string>();
        foreach (var e in events)
        {
            lines.Add($"Timestamp: {e.Timestamp:yyyy-MM-ddTHH:mm:ss}");
            lines.Add($"DriverVersion: {e.DriverVersion}");
            lines.Add($"Description: {e.Description}");
            lines.Add("---");
        }
        return string.Join("\n", lines);
    }

    [Fact]
    public void ExtractNvidiaInstallSlices_IncludesBootSessionAndNvidiaSection()
    {
        var lines = new[]
        {
            "Boot Session: 2026/01/15 10:00:00",
            ">>>  [Device Install (DiInstallDriver) - C:\\NVIDIA\\nv.inf - PCI\\VEN_10DE&DEV_2204]",
            ">>>  Section start 2026/01/15 10:05:00.123",
            "inf:   Driver Version = 6.14,32.0.15.8129",
            "<<<  Section end 2026/01/15 10:05:01.456",
        };

        var result = FixtureBuilder.ExtractNvidiaInstallSlices(lines, maxSections: 5);

        Assert.Single(result);
        Assert.Contains("Boot Session: 2026/01/15 10:00:00", result[0]);
        Assert.Contains("VEN_10DE", result[0]);
        Assert.Contains("32.0.15.8129", result[0]);
        Assert.Contains("<<<  Section end", result[0]);
    }

    [Fact]
    public void ExtractNvidiaInstallSlices_IgnoresNonNvidiaInstalls()
    {
        var lines = new[]
        {
            "Boot Session: 2026/01/15 10:00:00",
            ">>>  [Device Install (DiInstallDriver) - C:\\foo.inf - PCI\\VEN_8086&DEV_1234]",
            ">>>  Section start 2026/01/15 10:05:00.123",
            "<<<  Section end 2026/01/15 10:05:01.456",
        };

        var result = FixtureBuilder.ExtractNvidiaInstallSlices(lines, maxSections: 5);

        Assert.Empty(result);
    }

    [Fact]
    public void ExtractNvidiaInstallSlices_RespectsMaxSections()
    {
        var lines = new List<string> { "Boot Session: 2026/01/15 10:00:00" };
        for (int i = 0; i < 10; i++)
        {
            lines.Add($">>>  [Device Install (n{i}) - C:\\nv.inf - PCI\\VEN_10DE&DEV_2204]");
            lines.Add($"inf:   Driver Version = 6.14,32.0.15.{8000 + i}");
            lines.Add("<<<  Section end 2026/01/15 10:05:01.456");
        }

        var result = FixtureBuilder.ExtractNvidiaInstallSlices(lines, maxSections: 3);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ExtractNvidiaInstallSlices_BootSessionEmittedOncePerCluster()
    {
        var lines = new[]
        {
            "Boot Session: 2026/01/15 10:00:00",
            ">>>  [Device Install (a) - C:\\nv.inf - PCI\\VEN_10DE&DEV_2204]",
            "<<<  Section end 2026/01/15 10:05:01.456",
            ">>>  [Device Install (b) - C:\\nv.inf - PCI\\VEN_10DE&DEV_2204]",
            "<<<  Section end 2026/01/15 10:06:01.456",
        };

        var result = FixtureBuilder.ExtractNvidiaInstallSlices(lines, maxSections: 5);

        Assert.Equal(2, result.Count);
        Assert.Contains("Boot Session", result[0]);
        Assert.DoesNotContain("Boot Session", result[1]);
    }

    [Fact]
    public void ExtractNvidiaInstallSlices_NewBootSessionEmittedAgain()
    {
        var lines = new[]
        {
            "Boot Session: 2026/01/15 10:00:00",
            ">>>  [Device Install (a) - C:\\nv.inf - PCI\\VEN_10DE&DEV_2204]",
            "<<<  Section end 2026/01/15 10:05:01.456",
            "Boot Session: 2026/02/01 12:00:00",
            ">>>  [Device Install (b) - C:\\nv.inf - PCI\\VEN_10DE&DEV_2204]",
            "<<<  Section end 2026/02/01 12:05:01.456",
        };

        var result = FixtureBuilder.ExtractNvidiaInstallSlices(lines, maxSections: 5);

        Assert.Equal(2, result.Count);
        Assert.Contains("Boot Session: 2026/01/15", result[0]);
        Assert.Contains("Boot Session: 2026/02/01", result[1]);
    }
}
