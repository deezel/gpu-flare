using FLARE.Core;

namespace FLARE.Tests;

public class LiveKernelDumpLocatorTests : IDisposable
{
    private readonly string _tempDir;

    public LiveKernelDumpLocatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flare_lk_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    private string WriteDump(string subdir, string name)
    {
        var dir = Path.Combine(_tempDir, subdir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[256]);
        return path;
    }

    private string WriteDumpAtTime(string subdir, string name, DateTime time)
    {
        var path = WriteDump(subdir, name);
        File.SetLastWriteTime(path, time);
        return path;
    }

    [Fact]
    public void Enumerate_NonexistentRoot_ReturnsEmptyAndNoNotice()
    {
        var health = new CollectorHealth();

        var result = LiveKernelDumpLocator.Enumerate(_tempDir, cutoff: null, maxDumps: 50, log: null, health: health);

        Assert.Empty(result);
        Assert.Empty(health.Notices);
    }

    [Fact]
    public void Enumerate_EmptyRoot_ReturnsEmptyAndNoNotice()
    {
        Directory.CreateDirectory(_tempDir);
        var health = new CollectorHealth();

        var result = LiveKernelDumpLocator.Enumerate(_tempDir, cutoff: null, maxDumps: 50, log: null, health: health);

        Assert.Empty(result);
        Assert.Empty(health.Notices);
    }

    [Fact]
    public void Enumerate_KnownCategories_ClassifiedByParentDir()
    {
        WriteDump("WATCHDOG",     "WATCHDOG-20260101-1200.dmp");
        WriteDump("WATCHDOG4400", "WATCHDOG4400-20260101-1200.dmp");
        WriteDump("WATCHDOG4401", "WATCHDOG4401-20260101-1200.dmp");

        var result = LiveKernelDumpLocator.Enumerate(_tempDir, cutoff: null, maxDumps: 50, log: null, health: null);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, d => d.Category == "WATCHDOG");
        Assert.Contains(result, d => d.Category == "WATCHDOG4400");
        Assert.Contains(result, d => d.Category == "WATCHDOG4401");
    }

    [Fact]
    public void Enumerate_UnknownCategory_ClassifiedAsOtherWithSubdir()
    {
        WriteDump("MysteryDir", "x.dmp");

        var result = LiveKernelDumpLocator.Enumerate(_tempDir, cutoff: null, maxDumps: 50, log: null, health: null);

        var dump = Assert.Single(result);
        Assert.Equal("OTHER:MysteryDir", dump.Category);
    }

    [Fact]
    public void Enumerate_NonDumpFiles_Ignored()
    {
        WriteDump("WATCHDOG", "a.dmp");
        var noise = Path.Combine(_tempDir, "WATCHDOG", "notes.txt");
        File.WriteAllText(noise, "ignore me");

        var result = LiveKernelDumpLocator.Enumerate(_tempDir, cutoff: null, maxDumps: 50, log: null, health: null);

        var dump = Assert.Single(result);
        Assert.EndsWith("a.dmp", dump.FileName);
    }

    [Fact]
    public void Enumerate_Cutoff_DropsOlderDumps()
    {
        var now = DateTime.Now;
        WriteDumpAtTime("WATCHDOG", "recent.dmp",  now.AddDays(-2));
        WriteDumpAtTime("WATCHDOG", "ancient.dmp", now.AddDays(-400));

        var cutoff = now.AddDays(-30);
        var result = LiveKernelDumpLocator.Enumerate(_tempDir, cutoff, maxDumps: 50, log: null, health: null);

        var dump = Assert.Single(result);
        Assert.Equal("recent.dmp", dump.FileName);
    }

    [Fact]
    public void Enumerate_OverCap_ReturnsNewest50AndSetsTruncationFlag()
    {
        var now = DateTime.Now;
        for (int i = 0; i < 60; i++)
            WriteDumpAtTime("WATCHDOG", $"d{i:D2}.dmp", now.AddMinutes(-i));

        var health = new CollectorHealth();
        var result = LiveKernelDumpLocator.Enumerate(_tempDir, cutoff: null, maxDumps: 50, log: null, health: health);

        Assert.Equal(50, result.Count);
        Assert.Equal("d00.dmp", result.First().FileName);
        Assert.Equal("d49.dmp", result.Last().FileName);
        Assert.Empty(health.Notices);
        Assert.True(health.Truncation.LiveKernelScanCap);
        Assert.Equal(60, health.Truncation.LiveKernelScanTotal);
    }

    [Fact]
    public void Enumerate_UnderCap_NoNotice()
    {
        var now = DateTime.Now;
        for (int i = 0; i < 10; i++)
            WriteDumpAtTime("WATCHDOG", $"d{i:D2}.dmp", now.AddMinutes(-i));

        var health = new CollectorHealth();
        var result = LiveKernelDumpLocator.Enumerate(_tempDir, cutoff: null, maxDumps: 50, log: null, health: health);

        Assert.Equal(10, result.Count);
        Assert.Empty(health.Notices);
        Assert.False(health.Truncation.LiveKernelScanCap);
        Assert.Equal(0, health.Truncation.LiveKernelScanTotal);
    }

    [Fact]
    public void Enumerate_CapAppliedAcrossCategoriesCombined()
    {
        var now = DateTime.Now;
        for (int i = 0; i < 30; i++)
            WriteDumpAtTime("WATCHDOG",     $"w{i:D2}.dmp",  now.AddMinutes(-i));
        for (int i = 0; i < 30; i++)
            WriteDumpAtTime("WATCHDOG4401", $"w4{i:D2}.dmp", now.AddMinutes(-(i + 100)));

        var result = LiveKernelDumpLocator.Enumerate(_tempDir, cutoff: null, maxDumps: 50, log: null, health: null);

        Assert.Equal(50, result.Count);
        Assert.Equal(30, result.Count(d => d.Category == "WATCHDOG"));
        Assert.Equal(20, result.Count(d => d.Category == "WATCHDOG4401"));
    }

    [Fact]
    public void Enumerate_RespectsCustomMaxDumpsValue()
    {
        var now = DateTime.Now;
        for (int i = 0; i < 60; i++)
            WriteDumpAtTime("WATCHDOG", $"d{i:D2}.dmp", now.AddMinutes(-i));

        var health = new CollectorHealth();
        var result = LiveKernelDumpLocator.Enumerate(_tempDir, cutoff: null, maxDumps: 10, log: null, health: health);

        Assert.Equal(10, result.Count);
        Assert.True(health.Truncation.LiveKernelScanCap);
        Assert.Equal(60, health.Truncation.LiveKernelScanTotal);
    }

    [Fact]
    public void Enumerate_MaxDumpsZero_ClampsToOne()
    {
        var now = DateTime.Now;
        for (int i = 0; i < 5; i++)
            WriteDumpAtTime("WATCHDOG", $"d{i:D2}.dmp", now.AddMinutes(-i));

        var result = LiveKernelDumpLocator.Enumerate(_tempDir, cutoff: null, maxDumps: 0, log: null, health: null);

        Assert.Single(result);
    }

    [Fact]
    public void Enumerate_MaxDumpsAboveCeiling_ClampsTo1000()
    {
        var now = DateTime.Now;
        for (int i = 0; i < 5; i++)
            WriteDumpAtTime("WATCHDOG", $"d{i:D2}.dmp", now.AddMinutes(-i));

        var result = LiveKernelDumpLocator.Enumerate(_tempDir, cutoff: null, maxDumps: 5000, log: null, health: null);

        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void ClampMaxDumps_PinsBoundsDirectly()
    {
        Assert.Equal(1, LiveKernelDumpLocator.ClampMaxDumps(-100));
        Assert.Equal(1, LiveKernelDumpLocator.ClampMaxDumps(0));
        Assert.Equal(1, LiveKernelDumpLocator.ClampMaxDumps(1));
        Assert.Equal(500, LiveKernelDumpLocator.ClampMaxDumps(500));
        Assert.Equal(1000, LiveKernelDumpLocator.ClampMaxDumps(1000));
        Assert.Equal(1000, LiveKernelDumpLocator.ClampMaxDumps(1001));
        Assert.Equal(1000, LiveKernelDumpLocator.ClampMaxDumps(5000));
        Assert.Equal(1000, LiveKernelDumpLocator.ClampMaxDumps(int.MaxValue));
    }

    [Fact]
    public void MaxDumpsHardCap_ConstantRemoved()
    {
        var members = typeof(LiveKernelDumpLocator).GetMembers(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.DoesNotContain(members, m => m.Name == "MaxDumpsHardCap");
    }
}
