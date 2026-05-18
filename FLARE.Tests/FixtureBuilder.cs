using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Reflection;
using FLARE.Core;

namespace FLARE.Tests;

public class FixtureBuilder
{
    private static string CdbCacheDir => FlareStorage.CdbCacheDir();

    public static bool RebuildFixturesEnabled =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLARE_REBUILD_FIXTURES"));

    private static string FixturesDir =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(FixtureBuilder).Assembly.Location)!,
            "..", "..", "..", "Fixtures", "Cdb"));

    private static string NvlddmkmFixturesDir =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(FixtureBuilder).Assembly.Location)!,
            "..", "..", "..", "Fixtures", "EventLog", "Nvlddmkm"));

    private static string SetupApiFixturesDir =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(FixtureBuilder).Assembly.Location)!,
            "..", "..", "..", "Fixtures", "EventLog", "SetupApi"));

    [Fact(Skip = "manual fixture rebuild only — set FLARE_REBUILD_FIXTURES=1",
        SkipUnless = nameof(RebuildFixturesEnabled), SkipType = typeof(FixtureBuilder))]
    public void RebuildCdbFixtures()
    {
        var pairs = new[]
        {
            ("040326-18109-01.dmp.018cfbd62f29a6d07568ffb5a9645b2a.cdb.txt", "minidump_0x113_dxgkrnl.cdb.txt"),
            ("040326-19109-01.dmp.bf2ea4a6546cb27af8d68fd06db6faef.cdb.txt", "minidump_0x116_nvlddmkm.cdb.txt"),
            ("010726-18531-01.dmp.36968ad8a0c11f34ed413d0ffc84d725.cdb.txt", "minidump_0x133_dpc_watchdog.cdb.txt"),
        };

        Directory.CreateDirectory(FixturesDir);

        foreach (var (src, dest) in pairs)
        {
            var srcPath = Path.Combine(CdbCacheDir, src);
            if (!File.Exists(srcPath)) continue;

            var raw = File.ReadAllText(srcPath);
            var stripped = StripFlareCacheHeader(raw);
            var redacted = ReportRedaction.RedactAll(stripped, Environment.MachineName);

            File.WriteAllText(Path.Combine(FixturesDir, dest), redacted);
        }

        PickAndCopyLiveKernel("WATCHDOG-", "0x141", "livekernel_0x141_watchdog_nvlddmkm.cdb.txt", "nvlddmkm");
        PickAndCopyLiveKernel("WATCHDOG4401-", "0x1b8", "livekernel_0x1B8_watchdog4401_dxgkrnl.cdb.txt", null);
    }

    [Fact(Skip = "manual fixture rebuild only — set FLARE_REBUILD_FIXTURES=1",
        SkipUnless = nameof(RebuildFixturesEnabled), SkipType = typeof(FixtureBuilder))]
    public void RegenerateExpectedFromFixtures()
    {
        var fixtures = Directory.GetFiles(FixturesDir, "*.cdb.txt");
        foreach (var fixture in fixtures)
        {
            if (fixture.EndsWith(".expected.txt")) continue;
            var content = File.ReadAllText(fixture);
            var extracted = CdbRunner.ExtractCdbSummary(content, log: null, health: null);
            var expectedPath = fixture + ".expected.txt";
            File.WriteAllText(expectedPath, extracted ?? "");
        }
    }

    private static void PickAndCopyLiveKernel(string filenamePrefix, string bugcheckMarker, string destName, string? extraMarker)
    {
        var candidates = Directory.GetFiles(CdbCacheDir, "*.cdb.txt", SearchOption.TopDirectoryOnly);
        Array.Sort(candidates, StringComparer.OrdinalIgnoreCase);
        foreach (var path in candidates)
        {
            var name = Path.GetFileName(path);
            if (!name.StartsWith(filenamePrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var raw = File.ReadAllText(path);
            if (raw.IndexOf(bugcheckMarker, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (extraMarker != null && raw.IndexOf(extraMarker, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var stripped = StripFlareCacheHeader(raw);
            var redacted = ReportRedaction.RedactAll(stripped, Environment.MachineName);
            File.WriteAllText(Path.Combine(FixturesDir, destName), redacted);
            return;
        }
    }

    private static string StripFlareCacheHeader(string raw)
    {
        var lines = raw.Split('\n');
        int start = 0;
        if (lines.Length >= 5 && lines[0].StartsWith("# FLARE cdb cache "))
        {
            start = 5;
        }
        int end = lines.Length;
        if (end > 0 && lines[end - 1].StartsWith("# end "))
        {
            end -= 1;
        }
        return string.Join('\n', lines, start, end - start);
    }

    [Fact(Skip = "manual fixture rebuild only — set FLARE_REBUILD_FIXTURES=1",
        SkipUnless = nameof(RebuildFixturesEnabled), SkipType = typeof(FixtureBuilder))]
    public void RebuildNvlddmkmFixtures()
    {
        Directory.CreateDirectory(NvlddmkmFixturesDir);
        var query = EventLogParser.CreateEventLogQuery(EventLogParser.SystemLogName, EventLogParser.NvlddmkmGpuEventXPath);
        using var reader = new EventLogReader(query);

        var counts = new Dictionary<string, int>();
        const int perBucket = 2;
        const int maxBuckets = 8;

        EventRecord? rec;
        while ((rec = reader.ReadEvent()) != null)
        {
            using (rec)
            {
                try
                {
                    var rawXml = rec.ToXml();
                    var ts = rec.TimeCreated ?? DateTime.MinValue;
                    var allData = EventLogParser.NormalizeEventProperties(rec.Properties.Select(p => p.Value));
                    var classified = EventLogParser.ClassifyGpuError(ts, rec.Id, allData);
                    var bucket = $"event{classified.EventId}_{(classified.Sm.HasValue ? "sm" : "nosm")}_{SafeBucketSuffix(classified.ErrorType)}";

                    var seen = counts.GetValueOrDefault(bucket);
                    if (seen >= perBucket) continue;
                    counts[bucket] = seen + 1;

                    var redacted = ReportRedaction.RedactAll(rawXml, Environment.MachineName);
                    var fileName = $"{bucket}_{seen + 1}.xml";
                    File.WriteAllText(Path.Combine(NvlddmkmFixturesDir, fileName), redacted);
                }
                catch { }
            }
            if (counts.Count >= maxBuckets && counts.Values.All(v => v >= perBucket)) break;
        }
    }

    [Fact(Skip = "manual fixture rebuild only — set FLARE_REBUILD_FIXTURES=1",
        SkipUnless = nameof(RebuildFixturesEnabled), SkipType = typeof(FixtureBuilder))]
    public void RegenerateNvlddmkmExpectedFromFixtures()
    {
        if (!Directory.Exists(NvlddmkmFixturesDir)) return;
        foreach (var fixture in Directory.GetFiles(NvlddmkmFixturesDir, "*.xml"))
        {
            var xml = File.ReadAllText(fixture);
            var parsed = EventLogParser.ParseNvlddmkmEventXml(xml);
            var expected = NvlddmkmFixtureTests.FormatNvlddmkmError(parsed);
            File.WriteAllText(fixture + ".expected.txt", expected);
        }
    }

    private static string SafeBucketSuffix(string? errorType)
    {
        if (string.IsNullOrEmpty(errorType)) return "untyped";
        var sb = new System.Text.StringBuilder(errorType.Length);
        foreach (var c in errorType)
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
        return sb.ToString().Trim('_');
    }

    [Fact(Skip = "manual fixture rebuild only — set FLARE_REBUILD_FIXTURES=1",
        SkipUnless = nameof(RebuildFixturesEnabled), SkipType = typeof(FixtureBuilder))]
    public void RebuildSetupApiFixtures()
    {
        Directory.CreateDirectory(SetupApiFixturesDir);
        var sourcePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "INF", "setupapi.dev.log");
        if (!File.Exists(sourcePath)) return;

        const int maxSections = 5;
        var sections = ExtractNvidiaInstallSlices(File.ReadLines(sourcePath), maxSections);
        if (sections.Count == 0) return;

        var raw = string.Join(Environment.NewLine, sections);
        var redacted = ReportRedaction.RedactAll(raw, Environment.MachineName);
        File.WriteAllText(Path.Combine(SetupApiFixturesDir, "setupapi_nvidia_installs.log"), redacted);
    }

    [Fact(Skip = "manual fixture rebuild only — set FLARE_REBUILD_FIXTURES=1",
        SkipUnless = nameof(RebuildFixturesEnabled), SkipType = typeof(FixtureBuilder))]
    public void RegenerateSetupApiExpectedFromFixtures()
    {
        if (!Directory.Exists(SetupApiFixturesDir)) return;
        foreach (var fixture in Directory.GetFiles(SetupApiFixturesDir, "*.log"))
        {
            var events = EventLogParser.ParseSetupApiLog(fixture, ct: TestContext.Current.CancellationToken);
            var expected = SetupApiFixtureTests.FormatEvents(events);
            File.WriteAllText(fixture + ".expected.txt", expected);
        }
    }

    internal static IReadOnlyList<string> ExtractNvidiaInstallSlices(IEnumerable<string> lines, int maxSections)
    {
        var sections = new List<string>();
        string? recentBootSession = null;
        string? bootSessionEmittedInCurrent = null;
        var current = new List<string>();
        bool inNvidiaSection = false;

        foreach (var line in lines)
        {
            if (!inNvidiaSection && line.Contains("Boot Session:"))
                recentBootSession = line;

            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^>>>\s+\[Device Install .* - PCI\\VEN_10DE"))
            {
                inNvidiaSection = true;
                current.Clear();
                if (recentBootSession != null && recentBootSession != bootSessionEmittedInCurrent)
                {
                    current.Add(recentBootSession);
                    current.Add("");
                    bootSessionEmittedInCurrent = recentBootSession;
                }
                current.Add(line);
                continue;
            }

            if (inNvidiaSection)
            {
                current.Add(line);
                if (line.StartsWith("<<<  Section end"))
                {
                    sections.Add(string.Join(Environment.NewLine, current));
                    current.Clear();
                    inNvidiaSection = false;
                    if (sections.Count >= maxSections) break;
                }
            }
        }

        return sections;
    }
}
