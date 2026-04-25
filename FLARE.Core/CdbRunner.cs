using System;
using System.Text;
using System.Threading;

namespace FLARE.Core;

internal static class CdbRunner
{
    // Most stable anchor in !analyze -v output — present on every WinDbg version
    // we've seen. Used as the canary for "transcript was real, but our tag bank
    // matched nothing" so a silent WinDbg format shift doesn't leave the deep-
    // analysis section empty with no user-visible signal.
    private const string AnalyzeBanner = "Bugcheck Analysis";
    private const int MaxStackFrames = 10;

    public static string? RunCdbAnalysis(string cdbPath, string dumpPath, Action<string>? log = null, CancellationToken ct = default)
    {
        var output = ProcessRunner.RunWithLog(cdbPath, log, ct,
            "-z", dumpPath, "-c", "!analyze -v; q");
        return string.IsNullOrWhiteSpace(output) ? null : output;
    }

    public static string? ExtractCdbSummary(string cdbOutput, Action<string>? log = null, CollectorHealth? health = null)
    {
        var sb = new StringBuilder();
        var lines = cdbOutput.Split('\n');
        bool inStack = false;
        int stackLines = 0;
        bool matchedAnyTag = false;
        bool sawStackHeader = false;
        int stackLinesCaptured = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (line.Contains("BUGCHECK_STR:") || line.Contains("DEFAULT_BUCKET_ID:") ||
                line.Contains("PROCESS_NAME:") || line.Contains("IMAGE_NAME:") ||
                line.Contains("MODULE_NAME:") || line.Contains("FAULTING_MODULE:") ||
                line.Contains("FAILURE_BUCKET_ID:") || line.Contains("DRIVER_VERIFIER_IOMANAGER_VIOLATION"))
            {
                sb.AppendLine($"    {line.Trim()}");
                matchedAnyTag = true;
            }

            if (line.Contains("STACK_TEXT:"))
            {
                inStack = true;
                sb.AppendLine("    STACK_TEXT (top frames):");
                stackLines = 0;
                matchedAnyTag = true;
                sawStackHeader = true;
                continue;
            }

            if (inStack)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    inStack = false;
                    continue;
                }
                if (stackLines >= MaxStackFrames)
                {
                    sb.AppendLine($"      (top {MaxStackFrames} frames shown; cdb stack was longer)");
                    inStack = false;
                    continue;
                }
                sb.AppendLine($"      {line.Trim()}");
                stackLines++;
                stackLinesCaptured++;
            }
        }

        var result = sb.ToString();
        if (!matchedAnyTag && cdbOutput.Contains(AnalyzeBanner, StringComparison.Ordinal))
        {
            var msg =
                "cdb !analyze -v transcript contained the Bugcheck Analysis banner but " +
                "none of the expected tag lines (BUGCHECK_STR, PROCESS_NAME, MODULE_NAME, STACK_TEXT, ...). " +
                "Deep-analysis section will be empty; the cdb summary extractor may need updating.";
            log?.Invoke($"Warning: {msg}");
            health?.Canary("cdb summary extractor", msg);
        }
        else if (sawStackHeader && stackLinesCaptured == 0)
        {
            var msg =
                "cdb !analyze -v transcript contained the STACK_TEXT header but no stack frames " +
                "followed it before the next blank line. Stack frames are the bulk of the deep-analysis value; " +
                "the cdb summary extractor may need updating.";
            log?.Invoke($"Warning: {msg}");
            health?.Canary("cdb summary extractor", msg);
        }
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
