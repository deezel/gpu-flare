using System;
using System.IO;

namespace FLARE.Core;

// DoNotShareRoot holds raw Windows artifacts (minidumps, cdb stack traces with local paths);
// the report folder holds only the generated .txt. The folder name is the user-facing signal.
public static class FlareStorage
{
    public static string FlareRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FLARE");

    public static string DoNotShareRoot() =>
        Path.Combine(FlareRoot(), "DO_NOT_SHARE");

    public static string MinidumpsDir() =>
        Path.Combine(DoNotShareRoot(), "Minidumps");

    public static string CdbCacheDir() =>
        Path.Combine(DoNotShareRoot(), "CdbCache");

    // Transient raw-dump staging for the elevated copy helper. Keep it under
    // DO_NOT_SHARE so crash/cancel leftovers do not escape the user-facing
    // "raw artifacts live here" boundary described in the README/UI.
    public static string StagingRoot() =>
        Path.Combine(DoNotShareRoot(), "Staging");

    public static string ReportsDir() =>
        Path.Combine(FlareRoot(), "Reports");

    // Idempotent one-shot migration from pre-DO_NOT_SHARE layouts. Failures
    // log and swallow — migration must not break app startup.
    public static void MigrateLegacyLayout(string? reportDir, Action<string>? log = null) =>
        MigrateLegacyLayout(reportDir, FlareRoot(), DoNotShareRoot(), log);

    internal static void MigrateLegacyLayout(
        string? reportDir,
        string flareRoot,
        string doNotShareRoot,
        Action<string>? log)
    {
        TryMigrateLegacyCdbCache(flareRoot, doNotShareRoot, log);
        if (!string.IsNullOrWhiteSpace(reportDir))
            TryMigrateLegacyMinidumps(reportDir, Path.Combine(doNotShareRoot, "Minidumps"), log);
    }

    internal static void TryMigrateLegacyCdbCache(string flareRoot, string doNotShareRoot, Action<string>? log)
    {
        var legacy = Path.Combine(flareRoot, "CdbCache");
        var target = Path.Combine(doNotShareRoot, "CdbCache");

        if (!Directory.Exists(legacy)) return;
        // Both present: don't silently merge — leave the legacy folder for manual inspection.
        if (Directory.Exists(target)) return;

        try
        {
            Directory.CreateDirectory(doNotShareRoot);
            Directory.Move(legacy, target);
            log?.Invoke($"FLARE migration: moved legacy cdb cache {legacy} -> {target}");
        }
        catch (Exception ex)
        {
            log?.Invoke($"FLARE migration: cdb cache move failed ({legacy}): {ex.Message}");
        }
    }

    internal static void TryMigrateLegacyMinidumps(string reportDir, string newMinidumpsDir, Action<string>? log)
    {
        string legacyDir;
        try { legacyDir = Path.Combine(Path.GetFullPath(reportDir), "minidumps"); }
        catch { return; }

        if (!Directory.Exists(legacyDir)) return;

        try
        {
            var dmps = Directory.GetFiles(legacyDir, "*.dmp");
            if (dmps.Length == 0)
            {
                try { Directory.Delete(legacyDir); } catch { }
                return;
            }

            Directory.CreateDirectory(newMinidumpsDir);

            int moved = 0, skipped = 0;
            foreach (var src in dmps)
            {
                var dest = Path.Combine(newMinidumpsDir, Path.GetFileName(src));
                if (File.Exists(dest))
                {
                    // Don't overwrite; leave the legacy copy for manual diff/delete.
                    skipped++;
                    continue;
                }
                File.Move(src, dest);
                moved++;
            }

            if (moved > 0 || skipped > 0)
                log?.Invoke(
                    $"FLARE migration: moved {moved} minidump(s) from {legacyDir} to {newMinidumpsDir}" +
                    (skipped > 0 ? $" (skipped {skipped} already present at destination)" : ""));

            // Migration scope is strictly .dmp; preserve any other contents.
            if (Directory.GetFileSystemEntries(legacyDir).Length == 0)
                Directory.Delete(legacyDir);
        }
        catch (Exception ex)
        {
            log?.Invoke($"FLARE migration: minidump move failed ({legacyDir}): {ex.Message}");
        }
    }
}
