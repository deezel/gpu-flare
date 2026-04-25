using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Threading;

namespace FLARE.Core;

public static class ElevatedDumpCopy
{
    public const string HelperArg = "--copy-dumps-to";

    internal const int ExitOk = 0;
    internal const int ExitGenericError = 1;
    internal const int ExitInvalidStagingPath = 2;
    internal const int ExitStagingOutsideRoot = 3;
    internal const int ExitStagingReparsePoint = 4;

    internal const string CopyFailureSentinelName = ".copy-failures";

    public static int RunHelperMode(string stagingDir, DateTime? cutoff = null)
    {
        if (string.IsNullOrWhiteSpace(stagingDir)) return ExitInvalidStagingPath;

        string fullStaging;
        try { fullStaging = Path.GetFullPath(stagingDir); }
        catch { return ExitInvalidStagingPath; }

        var rootFull = Path.GetFullPath(GetStagingRoot());
        if (!rootFull.EndsWith(Path.DirectorySeparatorChar))
            rootFull += Path.DirectorySeparatorChar;
        if (!fullStaging.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            return ExitStagingOutsideRoot;

        try
        {
            if (HasReparsePointInExistingPath(fullStaging, rootFull))
                return ExitStagingReparsePoint;

            var src = MinidumpLocator.GetSystemDumpDir();
            if (!Directory.Exists(src)) return ExitOk;

            Directory.CreateDirectory(fullStaging);
            if (HasReparsePointInExistingPath(fullStaging, rootFull))
                return ExitStagingReparsePoint;

            int failed = 0;
            foreach (var dmp in Directory.GetFiles(src, "*.dmp"))
            {
                if (HasReparsePointInExistingPath(fullStaging, rootFull))
                    return ExitStagingReparsePoint;
                if (cutoff.HasValue && File.GetLastWriteTime(dmp) < cutoff.Value)
                    continue;
                var dest = Path.Combine(fullStaging, Path.GetFileName(dmp));
                try { File.Copy(dmp, dest, overwrite: false); }
                catch { failed++; }
            }
            if (failed > 0)
                TryWriteCopyFailureSentinel(fullStaging, failed);
            return ExitOk;
        }
        catch
        {
            return ExitGenericError;
        }
    }

    public static List<string> CopyViaElevatedHelper(string destDir, DateTime? cutoff = null, Action<string>? log = null, CancellationToken ct = default, CollectorHealth? health = null)
    {
        var copied = new List<string>();
        var staging = Path.Combine(GetStagingRoot(), $"staging-{Guid.NewGuid():N}");

        try
        {
            ct.ThrowIfCancellationRequested();

            Directory.CreateDirectory(GetStagingRoot());
            SweepStaleStagings(GetStagingRoot());

            if (IsCurrentProcessElevated())
            {
                int rc = RunHelperMode(staging, cutoff);
                ct.ThrowIfCancellationRequested();
                if (rc != ExitOk)
                {
                    log?.Invoke($"  In-process minidump copy failed (code {rc}).");
                    health?.Failure("minidump copy", $"in-process helper returned exit code {rc}");
                    return copied;
                }
            }
            else if (!IsHostedByFlareExe())
            {
                log?.Invoke("  Minidump copy skipped (not running under FLARE.exe).");
                health?.Skipped("minidump copy", "not running under FLARE.exe; elevated helper was not launched");
                return copied;
            }
            else if (!SpawnElevatedHelper(staging, cutoff, log, ct, health))
            {
                return copied;
            }

            ct.ThrowIfCancellationRequested();

            if (HasReparsePointInExistingPath(staging, GetStagingRoot()))
            {
                log?.Invoke("  Minidump copy skipped (staging path contains a reparse point).");
                health?.Failure("minidump copy", "staging path contained a reparse point after helper returned");
                return copied;
            }

            Directory.CreateDirectory(destDir);
            if (!Directory.Exists(staging)) return copied;

            ReadCopyFailureSentinel(staging, log, health);

            foreach (var src in Directory.GetFiles(staging, "*.dmp"))
            {
                ct.ThrowIfCancellationRequested();
                var finalPath = ChooseDestination(destDir, src);
                if (finalPath == null) { File.Delete(src); continue; }
                File.Move(src, finalPath);
                copied.Add(finalPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Invoke($"  Minidump copy error: {ex.Message}");
            health?.Failure("minidump copy", ex.Message);
        }
        finally
        {
            TryDeleteStaging(staging);
        }

        return copied;
    }

    internal static bool TryWriteCopyFailureSentinel(string stagingDir, int failed)
    {
        try
        {
            var sentinelPath = Path.Combine(stagingDir, CopyFailureSentinelName);
            using var stream = new FileStream(
                sentinelPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(failed.ToString(CultureInfo.InvariantCulture));
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static void ReadCopyFailureSentinel(string staging, Action<string>? log, CollectorHealth? health)
    {
        var sentinel = Path.Combine(staging, CopyFailureSentinelName);
        if (!File.Exists(sentinel)) return;
        try
        {
            var raw = File.ReadAllText(sentinel).Trim();
            if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0)
            {
                log?.Invoke($"  {n} dump(s) in system minidump folder could not be copied.");
                health?.Failure("minidump copy", $"{n} dump(s) in system minidump folder could not be copied");
            }
        }
        catch { }
        try { File.Delete(sentinel); } catch { }
    }

    private static void TryDeleteStaging(string staging)
    {
        try
        {
            if (!Directory.Exists(staging)) return;
            if (HasReparsePointInExistingPath(staging, GetStagingRoot())) return;
            Directory.Delete(staging, recursive: true);
        }
        catch { }
    }

    internal static readonly TimeSpan StagingOrphanAge = TimeSpan.FromHours(1);

    internal static void SweepStaleStagings(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            var cutoff = DateTime.UtcNow - StagingOrphanAge;
            foreach (var dir in Directory.EnumerateDirectories(root, "staging-*"))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(dir) >= cutoff) continue;
                    if (HasReparsePointInExistingPath(dir, root)) continue;
                    Directory.Delete(dir, recursive: true);
                }
                catch { }
            }
        }
        catch { }
    }

    static string? ChooseDestination(string destDir, string srcPath)
    {
        var candidate = Path.Combine(destDir, Path.GetFileName(srcPath));
        if (!File.Exists(candidate)) return candidate;

        var si = new FileInfo(srcPath);
        var di = new FileInfo(candidate);
        if (si.Length == di.Length) return null;

        var stem = Path.GetFileNameWithoutExtension(srcPath);
        var ext = Path.GetExtension(srcPath);
        return Path.Combine(destDir, $"{stem}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
    }

    static bool SpawnElevatedHelper(string staging, DateTime? cutoff, Action<string>? log, CancellationToken ct, CollectorHealth? health)
    {
        var self = Environment.ProcessPath;
        if (string.IsNullOrEmpty(self))
        {
            log?.Invoke("  Could not resolve FLARE.exe path for elevated helper.");
            health?.Failure("minidump copy", "could not resolve FLARE.exe path for elevated helper");
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = self,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        psi.ArgumentList.Add(HelperArg);
        psi.ArgumentList.Add(staging);
        if (cutoff.HasValue)
            psi.ArgumentList.Add(cutoff.Value.Ticks.ToString(CultureInfo.InvariantCulture));

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            log?.Invoke("  Minidump copy skipped (UAC declined). Event log and GPU info still collected.");
            health?.Skipped("minidump copy", "UAC prompt declined; crash dump files were not copied");
            return false;
        }
        catch (Exception ex)
        {
            log?.Invoke($"  Could not request elevation for minidump copy: {ex.Message}");
            health?.Failure("minidump copy", $"could not request elevation: {ex.Message}");
            return false;
        }

        if (proc == null)
        {
            log?.Invoke("  Elevated helper did not start.");
            health?.Failure("minidump copy", "elevated helper did not start");
            return false;
        }

        using (proc)
        {
            while (!proc.WaitForExit(100))
            {
                if (!ct.IsCancellationRequested) continue;
                ct.ThrowIfCancellationRequested();
            }
            if (proc.ExitCode == ExitOk) return true;

            log?.Invoke(proc.ExitCode switch
            {
                ExitStagingOutsideRoot => "  Elevated helper rejected staging path (outside %LOCALAPPDATA%\\FLARE\\DO_NOT_SHARE\\Staging).",
                ExitInvalidStagingPath => "  Elevated helper rejected staging path (invalid).",
                ExitStagingReparsePoint => "  Elevated helper rejected staging path (contains a reparse point).",
                _ => $"  Elevated helper returned exit code {proc.ExitCode}.",
            });
            health?.Failure("minidump copy", proc.ExitCode switch
            {
                ExitStagingOutsideRoot => "elevated helper rejected staging path outside %LOCALAPPDATA%\\FLARE\\DO_NOT_SHARE\\Staging",
                ExitInvalidStagingPath => "elevated helper rejected invalid staging path",
                ExitStagingReparsePoint => "elevated helper rejected staging path containing a reparse point",
                _ => $"elevated helper returned exit code {proc.ExitCode}",
            });
            return false;
        }
    }

    internal static string GetStagingRoot() =>
        FlareStorage.StagingRoot();

    internal static bool HasReparsePointInExistingPath(string candidatePath, string rootPath) =>
        HasReparsePointInExistingPath(
            candidatePath,
            rootPath,
            Directory.Exists,
            p => new DirectoryInfo(p).Attributes);

    internal static bool HasReparsePointInExistingPath(
        string candidatePath,
        string rootPath,
        Func<string, bool> directoryExists,
        Func<string, FileAttributes> getAttributes)
    {
        string rootFull;
        string candidateFull;
        try
        {
            rootFull = TrimTrailingSeparators(Path.GetFullPath(rootPath));
            candidateFull = TrimTrailingSeparators(Path.GetFullPath(candidatePath));
        }
        catch
        {
            return true;
        }

        var rootWithSlash = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;
        if (!candidateFull.Equals(rootFull, StringComparison.OrdinalIgnoreCase) &&
            !candidateFull.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
            return true;

        if (PathHasReparsePoint(rootFull, directoryExists, getAttributes))
            return true;

        var relative = Path.GetRelativePath(rootFull, candidateFull);
        if (relative == "." || string.IsNullOrEmpty(relative)) return false;

        var current = rootFull;
        foreach (var segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (PathHasReparsePoint(current, directoryExists, getAttributes))
                return true;
        }

        return false;
    }

    private static bool PathHasReparsePoint(
        string path,
        Func<string, bool> directoryExists,
        Func<string, FileAttributes> getAttributes)
    {
        if (!directoryExists(path)) return false;
        try { return (getAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return true; }
    }

    private static string TrimTrailingSeparators(string path)
    {
        var root = Path.GetPathRoot(path) ?? "";
        while (path.Length > root.Length &&
               (path.EndsWith(Path.DirectorySeparatorChar) ||
                path.EndsWith(Path.AltDirectorySeparatorChar)))
        {
            path = path[..^1];
        }
        return path;
    }

    static bool IsCurrentProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    static bool IsHostedByFlareExe()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath)) return false;
        return string.Equals(
            Path.GetFileNameWithoutExtension(processPath),
            "FLARE",
            StringComparison.OrdinalIgnoreCase);
    }
}
