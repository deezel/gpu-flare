using System;
using System.Collections.Generic;
using System.IO;

namespace FLARE.Core;

public static class CdbLocator
{
    // Store/winget WinDbg registers architecture-tagged aliases instead of plain cdb.exe.
    // Order = preference (x64 first on 64-bit hosts).
    internal static readonly string[] CdbFileNames = ["cdb.exe", "cdbx64.exe", "cdbarm64.exe", "cdbx86.exe"];

    // No PATH fallback: resolution is restricted to Microsoft-managed debugger roots so
    // a shadowed cdb.exe on PATH can't be preferred over a missing trusted install.
    public static string? FindCdb(Action<string>? log = null)
        => FindCdbInternal(log, File.Exists);

    internal static string? FindCdbInternal(
        Action<string>? log,
        Func<string, bool> fileExists)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var trustedPaths = new List<string>();
        if (!string.IsNullOrEmpty(programFilesX86))
            trustedPaths.Add(Path.Combine(programFilesX86, "Windows Kits", "10", "Debuggers", "x64", "cdb.exe"));
        if (!string.IsNullOrEmpty(programFiles))
            trustedPaths.Add(Path.Combine(programFiles, "Windows Kits", "10", "Debuggers", "x64", "cdb.exe"));
        if (!string.IsNullOrEmpty(localAppData))
        {
            trustedPaths.Add(Path.Combine(localAppData, "Microsoft", "WindowsApps", "cdbx64.exe"));
            trustedPaths.Add(Path.Combine(localAppData, "Microsoft", "WindowsApps", "cdbarm64.exe"));
            trustedPaths.Add(Path.Combine(localAppData, "Microsoft", "WindowsApps", "cdbx86.exe"));
            trustedPaths.Add(Path.Combine(localAppData, "Microsoft", "WindowsApps", "cdb.exe"));
            trustedPaths.Add(Path.Combine(localAppData, "Microsoft", "WinDbg", "cdb.exe"));
        }

        var resolved = ResolveAutoDetect(trustedPaths, programFiles, localAppData, fileExists);
        if (resolved != null)
            log?.Invoke($"Note: cdb resolved via auto-detect: {resolved}");
        return resolved;
    }

    static string? ResolveAutoDetect(
        List<string> trustedPaths,
        string programFiles,
        string localAppData,
        Func<string, bool> fileExists)
    {
        var trusted = FindCdbInTrustedPaths(trustedPaths.ToArray(), fileExists);
        if (trusted != null) return trusted;

        if (!string.IsNullOrEmpty(programFiles))
        {
            try
            {
                var appsDir = Path.Combine(programFiles, "WindowsApps");
                if (Directory.Exists(appsDir))
                {
                    foreach (var dir in Directory.GetDirectories(appsDir, "*WinDbg*"))
                    {
                        var cdb = Path.Combine(dir, "amd64", "cdb.exe");
                        if (fileExists(cdb)) return cdb;
                        cdb = Path.Combine(dir, "cdb.exe");
                        if (fileExists(cdb)) return cdb;
                    }
                }
            }
            catch { /* non-admin can't list WindowsApps; LocalAppData scan below covers user-install shapes */ }
        }

        return FindCdbInLocalAppData(localAppData, fileExists);
    }

    internal static string? FindCdbInLocalAppData(string localAppData, Func<string, bool> fileExists)
    {
        if (string.IsNullOrEmpty(localAppData)) return null;
        var microsoftRoot = Path.Combine(localAppData, "Microsoft");
        string[] roots = [
            Path.Combine(microsoftRoot, "WindowsApps"),
            Path.Combine(microsoftRoot, "WinDbg"),
        ];

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        try
        {
            // EnumerateFiles order is unspecified — rank by CdbFileNames preference.
            string? best = null;
            int bestRank = int.MaxValue;
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var candidate in Directory.EnumerateFiles(root, "cdb*.exe", options))
                {
                    if (!fileExists(candidate)) continue;
                    var fn = Path.GetFileName(candidate);
                    int rank = Array.FindIndex(CdbFileNames, n => string.Equals(n, fn, StringComparison.OrdinalIgnoreCase));
                    if (rank < 0) continue;
                    if (rank < bestRank)
                    {
                        bestRank = rank;
                        best = candidate;
                        if (rank == 0) return best;
                    }
                }
            }
            return best;
        }
        catch { return null; }
    }

    internal static string? FindCdbInTrustedPaths(string[] candidatePaths, Func<string, bool> exists)
    {
        foreach (var p in candidatePaths)
            if (exists(p)) return p;
        return null;
    }

}
