using FLARE.Core;

namespace FLARE.Tests;

// Legacy-layout migration is the only on-disk side effect that runs at app
// startup without user interaction. A bug here either silently loses prior
// cdb-analysis work (cache not moved -> 30s/dump re-analysis) or leaves
// kernel-memory dumps in a folder users zip and share (reason we did this
// in the first place). Each branch of MigrateLegacyLayout gets a pin.
public class FlareStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _flareRoot;
    private readonly string _doNotShareRoot;
    private readonly string _reportDir;

    public FlareStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flare_storage_{Guid.NewGuid():N}");
        _flareRoot = Path.Combine(_tempDir, "FLARE");
        _doNotShareRoot = Path.Combine(_flareRoot, "DO_NOT_SHARE");
        _reportDir = Path.Combine(_tempDir, "Reports");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    // Use the internal overload that takes explicit roots so no test touches
    // the real %LOCALAPPDATA%\FLARE.
    private void Migrate(string? reportDir, Action<string>? log = null) =>
        FlareStorage.MigrateLegacyLayout(reportDir, _flareRoot, _doNotShareRoot, log);

    [Fact]
    public void Migration_NothingToMigrate_IsNoOp()
    {
        Migrate(_reportDir);

        Assert.False(Directory.Exists(_flareRoot));
        Assert.False(Directory.Exists(_doNotShareRoot));
    }

    [Fact]
    public void Migration_LegacyCdbCache_MovedToDoNotShare()
    {
        var legacy = Path.Combine(_flareRoot, "CdbCache");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "mini.dmp.abc.cdb.txt"), "transcript");

        var logs = new List<string>();
        Migrate(_reportDir, logs.Add);

        var target = Path.Combine(_doNotShareRoot, "CdbCache");
        Assert.True(File.Exists(Path.Combine(target, "mini.dmp.abc.cdb.txt")));
        Assert.False(Directory.Exists(legacy), "legacy cache dir should be gone after move");
        Assert.Contains(logs, l => l.Contains("cdb cache"));
    }

    [Fact]
    public void Migration_LegacyCdbCacheAndNewAlreadyExists_LegacyPreservedForManualReview()
    {
        var legacy = Path.Combine(_flareRoot, "CdbCache");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "old.cdb.txt"), "old");
        var target = Path.Combine(_doNotShareRoot, "CdbCache");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "new.cdb.txt"), "new");

        Migrate(_reportDir);

        Assert.True(File.Exists(Path.Combine(legacy, "old.cdb.txt")),
            "legacy cache must remain when target also exists");
        Assert.True(File.Exists(Path.Combine(target, "new.cdb.txt")));
    }

    [Fact]
    public void Migration_LegacyMinidumpsInReportDir_MovedOutOfReportFolder()
    {
        var legacyDumpsDir = Path.Combine(_reportDir, "minidumps");
        Directory.CreateDirectory(legacyDumpsDir);
        File.WriteAllBytes(Path.Combine(legacyDumpsDir, "Mini01.dmp"), new byte[16]);
        File.WriteAllBytes(Path.Combine(legacyDumpsDir, "Mini02.dmp"), new byte[16]);

        var logs = new List<string>();
        Migrate(_reportDir, logs.Add);

        var target = Path.Combine(_doNotShareRoot, "Minidumps");
        Assert.True(File.Exists(Path.Combine(target, "Mini01.dmp")));
        Assert.True(File.Exists(Path.Combine(target, "Mini02.dmp")));
        Assert.False(Directory.Exists(legacyDumpsDir),
            "empty legacy minidumps/ folder should be removed after the move");
        Assert.Contains(logs, l => l.Contains("minidump"));
    }

    [Fact]
    public void Migration_LegacyMinidumpsWithCollidingName_SkipsWithoutOverwriting()
    {
        var legacyDumpsDir = Path.Combine(_reportDir, "minidumps");
        Directory.CreateDirectory(legacyDumpsDir);
        File.WriteAllBytes(Path.Combine(legacyDumpsDir, "Mini01.dmp"), new byte[16]);

        var target = Path.Combine(_doNotShareRoot, "Minidumps");
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "Mini01.dmp"), new byte[32]);

        Migrate(_reportDir);

        Assert.True(File.Exists(Path.Combine(legacyDumpsDir, "Mini01.dmp")),
            "legacy dump must remain when destination already holds a same-name file");
        Assert.Equal(32, new FileInfo(Path.Combine(target, "Mini01.dmp")).Length);
    }

    [Fact]
    public void Migration_EmptyLegacyMinidumpsDir_Removed()
    {
        var legacyDumpsDir = Path.Combine(_reportDir, "minidumps");
        Directory.CreateDirectory(legacyDumpsDir);

        Migrate(_reportDir);

        Assert.False(Directory.Exists(legacyDumpsDir));
    }

    [Fact]
    public void Migration_NullReportDir_StillMigratesCdbCache()
    {
        // On a brand-new install with no persisted OutputPath, App.xaml.cs
        // passes null. Cdb-cache migration must still run (legacy cache is
        // independent of report folder).
        var legacy = Path.Combine(_flareRoot, "CdbCache");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "a.txt"), "x");

        Migrate(null);

        Assert.True(File.Exists(Path.Combine(_doNotShareRoot, "CdbCache", "a.txt")));
    }

    [Fact]
    public void Migration_ReportDirDoesNotExist_IsNoOp()
    {
        Migrate(Path.Combine(_tempDir, "does", "not", "exist"));

        Assert.False(Directory.Exists(_doNotShareRoot));
    }

    [Fact]
    public void Migration_NonDmpFilesInLegacyMinidumps_PreservedWithLegacyDir()
    {
        var legacyDumpsDir = Path.Combine(_reportDir, "minidumps");
        Directory.CreateDirectory(legacyDumpsDir);
        File.WriteAllBytes(Path.Combine(legacyDumpsDir, "Mini01.dmp"), new byte[16]);
        File.WriteAllText(Path.Combine(legacyDumpsDir, "notes.txt"), "user notes");

        Migrate(_reportDir);

        Assert.True(File.Exists(Path.Combine(_doNotShareRoot, "Minidumps", "Mini01.dmp")));
        Assert.True(File.Exists(Path.Combine(legacyDumpsDir, "notes.txt")),
            "non-dmp files must be left in place");
        Assert.True(Directory.Exists(legacyDumpsDir),
            "legacy dir must remain because it still has unrelated content");
    }

    [Fact]
    public void Migration_RunTwice_IsIdempotent()
    {
        // App startup calls migration unconditionally every launch. Running
        // it again after a successful migration must not undo, move, or
        // corrupt anything.
        var legacy = Path.Combine(_flareRoot, "CdbCache");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "a.txt"), "x");
        var legacyDumps = Path.Combine(_reportDir, "minidumps");
        Directory.CreateDirectory(legacyDumps);
        File.WriteAllBytes(Path.Combine(legacyDumps, "Mini01.dmp"), new byte[8]);

        Migrate(_reportDir);
        Migrate(_reportDir);

        Assert.True(File.Exists(Path.Combine(_doNotShareRoot, "CdbCache", "a.txt")));
        Assert.True(File.Exists(Path.Combine(_doNotShareRoot, "Minidumps", "Mini01.dmp")));
    }
}
