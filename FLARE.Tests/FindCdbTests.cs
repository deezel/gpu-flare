using FLARE.Core;

namespace FLARE.Tests;

public class FindCdbTests
{
    // These tests pin the auto-detect algorithm without touching the live
    // filesystem. The outer FindCdb method still probes real paths.

    [Fact]
    public void FindCdbInTrustedPaths_FirstExistingWins()
    {
        string[] paths = ["A", "B", "C"];
        var result = CdbLocator.FindCdbInTrustedPaths(paths, p => p == "B" || p == "C");
        Assert.Equal("B", result);
    }

    [Fact]
    public void FindCdbInTrustedPaths_OrderRespected_EarlierWinsEvenWhenLaterExists()
    {
        string[] paths = ["first", "second"];
        var result = CdbLocator.FindCdbInTrustedPaths(paths, _ => true);
        Assert.Equal("first", result);
    }

    [Fact]
    public void FindCdbInTrustedPaths_NoneExist_ReturnsNull()
    {
        string[] paths = ["X", "Y", "Z"];
        var result = CdbLocator.FindCdbInTrustedPaths(paths, _ => false);
        Assert.Null(result);
    }

    [Fact]
    public void FindCdbInTrustedPaths_EmptyList_ReturnsNull()
    {
        var result = CdbLocator.FindCdbInTrustedPaths([], _ => true);
        Assert.Null(result);
    }

    [Fact]
    public void FindCdbInLocalAppData_IgnoresMicrosoftSubfoldersOutsideDebuggerRoots()
    {
        var localAppData = Path.Combine(Path.GetTempPath(), $"flare_cdb_local_{Guid.NewGuid():N}");
        try
        {
            var untrusted = Path.Combine(localAppData, "Microsoft", "NotWinDbg", "cdb.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(untrusted)!);
            File.WriteAllBytes(untrusted, [0]);

            var result = CdbLocator.FindCdbInLocalAppData(localAppData, File.Exists);

            Assert.Null(result);
        }
        finally
        {
            try { if (Directory.Exists(localAppData)) Directory.Delete(localAppData, true); } catch { }
        }
    }

    [Fact]
    public void FindCdbInLocalAppData_AcceptsDebuggerRoots()
    {
        var localAppData = Path.Combine(Path.GetTempPath(), $"flare_cdb_local_{Guid.NewGuid():N}");
        try
        {
            var cdb = Path.Combine(localAppData, "Microsoft", "WinDbg", "Nested", "cdb.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(cdb)!);
            File.WriteAllBytes(cdb, [0]);

            var result = CdbLocator.FindCdbInLocalAppData(localAppData, File.Exists);

            Assert.Equal(cdb, result);
        }
        finally
        {
            try { if (Directory.Exists(localAppData)) Directory.Delete(localAppData, true); } catch { }
        }
    }

    [Fact]
    public void FindCdbInLocalAppData_PrefersPlainCdbOverArchitectureAlias()
    {
        var localAppData = Path.Combine(Path.GetTempPath(), $"flare_cdb_local_{Guid.NewGuid():N}");
        try
        {
            var alias = Path.Combine(localAppData, "Microsoft", "WindowsApps", "cdbx64.exe");
            var plain = Path.Combine(localAppData, "Microsoft", "WinDbg", "cdb.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(alias)!);
            Directory.CreateDirectory(Path.GetDirectoryName(plain)!);
            File.WriteAllBytes(alias, [0]);
            File.WriteAllBytes(plain, [0]);

            var result = CdbLocator.FindCdbInLocalAppData(localAppData, File.Exists);

            Assert.Equal(plain, result);
        }
        finally
        {
            try { if (Directory.Exists(localAppData)) Directory.Delete(localAppData, true); } catch { }
        }
    }

    [Fact]
    public void FindCdbInternal_AutoDetectResolved_LogsWhichPath()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var expectedProbe = Path.Combine(programFilesX86, "Windows Kits", "10", "Debuggers", "x64", "cdb.exe");

        var log = new List<string>();
        var result = CdbLocator.FindCdbInternal(
            log.Add,
            fileExists: p => string.Equals(p, expectedProbe, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(expectedProbe, result);
        Assert.Contains(log, l => l.Contains("auto-detect") && l.Contains(expectedProbe));
    }
}
