using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FLARE.Core;

public static class MinidumpLocator
{
    // Don't auto-expand env vars: an unelevated parent could poison %SystemRoot%
    // before the UAC round-trip to redirect the elevated helper's source.
    public static string GetSystemDumpDir(Action<string>? log = null)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\CrashControl");
            var raw = key?.GetValue(
                "MinidumpDir", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (!string.IsNullOrEmpty(raw))
            {
                var expanded = ExpandSystemVariables(raw);
                if (expanded == null)
                {
                    log?.Invoke(
                        "Warning: CrashControl\\MinidumpDir contains an unsupported environment variable; " +
                        "using default location.");
                }
                else if (!IsUnderWindowsRoot(expanded))
                {
                    // HKLM is admin-only, but matching the staging-side defenses keeps the
                    // elevated helper's source path within the same trust envelope as its sink.
                    log?.Invoke(
                        $"Warning: CrashControl\\MinidumpDir ({expanded}) resolves outside %SystemRoot%; " +
                        "using default location.");
                }
                else
                {
                    return expanded;
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Could not read CrashControl\\MinidumpDir: {ex.Message}");
        }
        return DefaultDumpDir();
    }

    internal static bool IsUnderWindowsRoot(string candidate)
    {
        string full;
        try { full = Path.GetFullPath(candidate); }
        catch { return false; }

        var windows = ResolveWindowsDirectory();
        var windowsFull = Path.GetFullPath(windows);
        if (!windowsFull.EndsWith(Path.DirectorySeparatorChar))
            windowsFull += Path.DirectorySeparatorChar;

        return full.StartsWith(windowsFull, StringComparison.OrdinalIgnoreCase);
    }

    internal static string DefaultDumpDir() =>
        Path.Combine(ResolveWindowsDirectory(), "Minidump");

    // Environment.SystemDirectory wraps GetSystemDirectoryW (no env expansion).
    internal static string ResolveWindowsDirectory()
    {
        var sysDir = Environment.SystemDirectory;
        var parent = Path.GetDirectoryName(sysDir);
        return !string.IsNullOrEmpty(parent) ? parent : @"C:\Windows";
    }

    // Returns null on any non-%SystemRoot%/%windir% token so callers fall back to the default.
    internal static string? ExpandSystemVariables(string input)
    {
        var windows = ResolveWindowsDirectory();
        bool unsupported = false;
        var expanded = Regex.Replace(
            input,
            @"%([^%]+)%",
            m =>
            {
                var name = m.Groups[1].Value;
                if (string.Equals(name, "SystemRoot", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "windir", StringComparison.OrdinalIgnoreCase))
                    return windows;
                unsupported = true;
                return m.Value;
            },
            RegexOptions.None,
            TimeSpan.FromMilliseconds(500));
        return unsupported ? null : expanded;
    }
}
