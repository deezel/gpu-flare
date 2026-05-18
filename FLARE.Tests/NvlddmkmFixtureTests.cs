using System.Collections.Generic;
using System.IO;
using FLARE.Core;

namespace FLARE.Tests;

public class NvlddmkmFixtureTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "EventLog", "Nvlddmkm");

    public static IEnumerable<object[]> Fixtures()
    {
        if (!Directory.Exists(FixturesDir)) yield break;
        foreach (var path in Directory.GetFiles(FixturesDir, "*.xml"))
            yield return new object[] { Path.GetFileName(path) };
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Fixtures))]
    public void ParseNvlddmkmEventXml_RealWorldEvent_MatchesExpected(string fixtureName)
    {
        var fixturePath = Path.Combine(FixturesDir, fixtureName);
        var expectedPath = fixturePath + ".expected.txt";
        var xml = File.ReadAllText(fixturePath);
        var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n");

        var actual = EventLogParser.ParseNvlddmkmEventXml(xml);

        Assert.Equal(expected, FormatNvlddmkmError(actual));
    }

    internal static string FormatNvlddmkmError(NvlddmkmError e) =>
        $"Timestamp: {e.Timestamp.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}\n" +
        $"EventId: {e.EventId}\n" +
        $"Message: {e.Message}\n" +
        $"ErrorType: {e.ErrorType ?? "<null>"}\n" +
        $"Gpc: {(e.Gpc?.ToString() ?? "<null>")}\n" +
        $"Tpc: {(e.Tpc?.ToString() ?? "<null>")}\n" +
        $"Sm: {(e.Sm?.ToString() ?? "<null>")}";
}
