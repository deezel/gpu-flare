using System;
using System.Collections.Generic;
using System.IO;

namespace FLARE.Core;

public static class MinidumpCollector
{
    /// <summary>
    /// Resolve the system minidump directory. Reads the configured path from
    /// HKLM\SYSTEM\CurrentControlSet\Control\CrashControl\MinidumpDir (supports
    /// environment variable expansion), falls back to %SystemRoot%\Minidump.
    /// </summary>
    public static string GetSystemDumpDir()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\CrashControl");
            if (key?.GetValue("MinidumpDir") is string val && !string.IsNullOrEmpty(val))
                return Environment.ExpandEnvironmentVariables(val);
        }
        catch { }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Minidump");
    }

    public static List<string> Copy(string destDir, Action<string>? log = null)
    {
        var copied = new List<string>();
        var sourceDir = GetSystemDumpDir();
        if (!Directory.Exists(sourceDir)) return copied;

        try
        {
            Directory.CreateDirectory(destDir);
            foreach (var dmp in Directory.GetFiles(sourceDir, "*.dmp"))
            {
                var destPath = Path.Combine(destDir, Path.GetFileName(dmp));
                if (File.Exists(destPath))
                {
                    var srcInfo = new FileInfo(dmp);
                    var destInfo = new FileInfo(destPath);
                    if (srcInfo.Length == destInfo.Length)
                        continue;
                    var stem = Path.GetFileNameWithoutExtension(dmp);
                    var ext = Path.GetExtension(dmp);
                    destPath = Path.Combine(destDir, $"{stem}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
                }
                File.Copy(dmp, destPath);
                copied.Add(destPath);
            }
        }
        catch (UnauthorizedAccessException)
        {
            log?.Invoke("  Could not access minidumps (not elevated).");
        }
        catch (Exception ex)
        {
            log?.Invoke($"  Error copying minidumps: {ex.Message}");
        }
        return copied;
    }
}
