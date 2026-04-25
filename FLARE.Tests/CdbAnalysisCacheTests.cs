using FLARE.Core;

namespace FLARE.Tests;

// The cdb cache is invisible in the happy path (round-trip) and load-bearing
// on the unhappy paths (invalidation). These tests pin both sides: a valid
// cache file returns its transcript verbatim, and every individual key field
// (version, size, mtime) can independently invalidate the hit.
public class CdbAnalysisCacheTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cacheDir;

    public CdbAnalysisCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flare_cdbcache_{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(_tempDir, "cache");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    private string CreateFakeDump(string name = "Mini0001.dmp", int sizeBytes = 64)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    [Fact]
    public void Store_ThenTryLoad_RoundTripsTranscript()
    {
        var dump = CreateFakeDump();
        var transcript = "BUGCHECK_STR:  0x116\nSTACK_TEXT:\n  nvlddmkm+0x1234\n";

        CdbAnalysisCache.Store(dump, transcript, cacheRoot: _cacheDir);
        var loaded = CdbAnalysisCache.TryLoad(dump, cacheRoot: _cacheDir);

        Assert.NotNull(loaded);
        Assert.Contains("BUGCHECK_STR:  0x116", loaded);
        Assert.Contains("nvlddmkm+0x1234", loaded);
    }

    [Fact]
    public void TryLoad_NoCacheFile_ReturnsNull()
    {
        var dump = CreateFakeDump();

        Assert.Null(CdbAnalysisCache.TryLoad(dump, cacheRoot: _cacheDir));
    }

    [Fact]
    public void TryLoad_SizeChangedAfterCache_ReturnsNull()
    {
        // Dump bytes changed between Store and TryLoad (extremely unusual for
        // a real minidump, which is immutable once written — the guard is
        // defensive for zip-round-trip and manual-edit cases).
        var dump = CreateFakeDump(sizeBytes: 64);
        CdbAnalysisCache.Store(dump, "original transcript", cacheRoot: _cacheDir);

        File.WriteAllBytes(dump, new byte[128]);

        Assert.Null(CdbAnalysisCache.TryLoad(dump, cacheRoot: _cacheDir));
    }

    [Fact]
    public void TryLoad_MtimeChangedAfterCache_ReturnsNull()
    {
        var dump = CreateFakeDump();
        CdbAnalysisCache.Store(dump, "original transcript", cacheRoot: _cacheDir);

        // Advance the dump's mtime without touching its bytes — zip extraction
        // or a filesystem copy would cause this.
        File.SetLastWriteTimeUtc(dump, DateTime.UtcNow.AddHours(1));

        Assert.Null(CdbAnalysisCache.TryLoad(dump, cacheRoot: _cacheDir));
    }

    [Fact]
    public void TryLoad_VersionMismatch_ReturnsNull()
    {
        // Simulates a cache written by a prior FLARE release whose
        // CacheVersion differs. The header-first check must reject it so a
        // format-level change doesn't silently read garbage.
        var dump = CreateFakeDump();
        var cacheFile = CdbAnalysisCache.CacheFilePath(dump, _cacheDir);
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        var dumpInfo = new FileInfo(dump);
        var content = string.Join('\n',
            "# FLARE cdb cache v0",
            $"# dump: {Path.GetFileName(dump)}",
            $"# size: {dumpInfo.Length}",
            $"# mtime: {dumpInfo.LastWriteTimeUtc:O}",
            "#",
            "some ancient transcript");
        File.WriteAllText(cacheFile, content);

        Assert.Null(CdbAnalysisCache.TryLoad(dump, cacheRoot: _cacheDir));
    }

    [Fact]
    public void TryLoad_MalformedHeader_ReturnsNull()
    {
        var dump = CreateFakeDump();
        var cacheFile = CdbAnalysisCache.CacheFilePath(dump, _cacheDir);
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        File.WriteAllText(cacheFile, "this is not a valid cache file");

        Assert.Null(CdbAnalysisCache.TryLoad(dump, cacheRoot: _cacheDir));
    }

    [Fact]
    public void TryLoad_ValidHeaderButMissingTrailer_ReturnsNull()
    {
        var dump = CreateFakeDump();
        var cacheFile = CdbAnalysisCache.CacheFilePath(dump, _cacheDir);
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        var dumpInfo = new FileInfo(dump);
        var content = string.Join('\n',
            $"# FLARE cdb cache v1",
            $"# dump: {Path.GetFileName(dump)}",
            $"# size: {dumpInfo.Length}",
            $"# mtime: {dumpInfo.LastWriteTimeUtc:O}",
            "#",
            "BUGCHECK_STR:  0x116",
            "STACK_TEXT:",
            "  nvlddmkm+0x12"); // truncated mid-frame, no trailer
        File.WriteAllText(cacheFile, content);

        Assert.Null(CdbAnalysisCache.TryLoad(dump, cacheRoot: _cacheDir));
    }

    [Fact]
    public void TryLoad_DumpFileMissing_ReturnsNull()
    {
        // Cache file orphaned by a deleted dump: not a cache hit. Returning the
        // body here would let a stale transcript for a dump that is no longer
        // on disk slip into a report.
        var dump = CreateFakeDump();
        CdbAnalysisCache.Store(dump, "transcript", cacheRoot: _cacheDir);
        File.Delete(dump);

        Assert.Null(CdbAnalysisCache.TryLoad(dump, cacheRoot: _cacheDir));
    }

    [Fact]
    public void Store_Overwrites_ExistingCacheFile()
    {
        var dump = CreateFakeDump();
        CdbAnalysisCache.Store(dump, "first transcript", cacheRoot: _cacheDir);
        CdbAnalysisCache.Store(dump, "second transcript", cacheRoot: _cacheDir);

        var loaded = CdbAnalysisCache.TryLoad(dump, cacheRoot: _cacheDir);

        Assert.NotNull(loaded);
        Assert.Contains("second transcript", loaded);
        Assert.DoesNotContain("first transcript", loaded);
    }

    [Fact]
    public void CacheFilePath_LivesUnderCacheRootAndKeepsDumpName()
    {
        // Pin the cache location contract: raw cdb transcripts (which include
        // unscrubbed user paths) live under the DO_NOT_SHARE umbrella, not
        // beside the report folder a user might zip and send. The dump name
        // remains visible in the cache filename for troubleshooting.
        var path = CdbAnalysisCache.CacheFilePath(@"C:\dumps\Mini042025-01.dmp");
        Assert.StartsWith(CdbAnalysisCache.DefaultCacheRoot(), path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DO_NOT_SHARE", CdbAnalysisCache.DefaultCacheRoot(), StringComparison.Ordinal);
        Assert.Contains("Mini042025-01.dmp.", Path.GetFileName(path));
        Assert.EndsWith(".cdb.txt", path);
        Assert.NotEqual(@"C:\dumps\Mini042025-01.dmp.cdb.txt", path);
    }
}
