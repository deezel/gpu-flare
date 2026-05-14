using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FLARE.Core;

public sealed record LiveKernelDump(
    string FullPath,
    string FileName,
    string Category,
    DateTime Timestamp,
    long FileSize);

public static class LiveKernelDumpLocator
{
    public const int MinDumpsCap = 1;
    public const int MaxDumpsCap = 1000;

    public static int ClampMaxDumps(int maxDumps) => Math.Clamp(maxDumps, MinDumpsCap, MaxDumpsCap);

    public static List<LiveKernelDump> Enumerate(
        string rootDir,
        DateTime? cutoff,
        int maxDumps,
        Action<string>? log,
        CollectorHealth? health)
    {
        var clampedMax = ClampMaxDumps(maxDumps);
        if (!Directory.Exists(rootDir)) return new List<LiveKernelDump>();

        var rootFull = Path.GetFullPath(rootDir);
        var dumps = new List<LiveKernelDump>();

        // Reparse-point skip is safe because rootDir is FLARE-owned staging
        // (%LOCALAPPDATA%\FLARE\DO_NOT_SHARE\LiveKernelDumps) where we constructed
        // every entry. If this locator is ever pointed at a directory FLARE didn't
        // build (e.g. scanning C:\Windows\LiveKernelReports directly without the
        // elevated copy), drop AttributesToSkip — silent skip becomes a Claim
        // Integrity gap when junctions could carry real dumps.
        var enumOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            RecurseSubdirectories = true,
        };
        try
        {
            foreach (var path in Directory.EnumerateFiles(rootFull, "*.dmp", enumOptions))
            {
                var info = new FileInfo(path);
                if (cutoff.HasValue && info.LastWriteTime < cutoff.Value) continue;
                var parent = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
                var category = ClassifyCategory(parent);
                dumps.Add(new LiveKernelDump(
                    FullPath: path,
                    FileName: info.Name,
                    Category: category,
                    Timestamp: info.LastWriteTime,
                    FileSize: info.Length));
            }
        }
        catch (Exception ex)
        {
            health?.Failure("livekernel scan", $"Enumeration failed: {ex.Message}");
        }

        var sorted = dumps.OrderByDescending(d => d.Timestamp).ToList();
        if (sorted.Count > clampedMax)
        {
            if (health != null)
            {
                health.Truncation.LiveKernelScanCap = true;
                health.Truncation.LiveKernelScanTotal = sorted.Count;
            }
            return sorted.Take(clampedMax).ToList();
        }
        return sorted;
    }

    private static readonly HashSet<string> KnownCategories =
        new(StringComparer.OrdinalIgnoreCase) { "WATCHDOG", "WATCHDOG4400", "WATCHDOG4401" };

    private static string ClassifyCategory(string parentDirName)
    {
        if (string.IsNullOrEmpty(parentDirName)) return "OTHER:";
        var upper = parentDirName.ToUpperInvariant();
        return KnownCategories.Contains(upper) ? upper : $"OTHER:{parentDirName}";
    }
}
