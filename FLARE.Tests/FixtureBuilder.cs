using System;
using System.IO;
using System.Reflection;
using FLARE.Core;

namespace FLARE.Tests;

public class FixtureBuilder
{
    private const string CdbCacheDir = @"c:\Users\danne\AppData\Local\FLARE\DO_NOT_SHARE\CdbCache";

    private static string FixturesDir =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(FixtureBuilder).Assembly.Location)!,
            "..", "..", "..", "Fixtures", "Cdb"));

    [Fact(Skip = "manual fixture rebuild only — unskip and run once to regenerate fixtures from local cdb cache")]
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

    [Fact(Skip = "manual fixture rebuild only — generates .expected.txt files from current parser output")]
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
}
