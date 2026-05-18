using System.Collections.Concurrent;
using FLARE.Core;

namespace FLARE.Tests;

public class ParallelCdbRunnerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cacheRoot;

    public ParallelCdbRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flare_pcr_{Guid.NewGuid():N}");
        _cacheRoot = Path.Combine(_tempDir, "cache");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_cacheRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string WriteDump(string name, long size)
    {
        var path = Path.Combine(_tempDir, name);
        using var fs = File.Create(path);
        fs.SetLength(size);
        return path;
    }

    [Fact]
    public async Task RunAllAsync_PreWarmsLargestDumpBeforeOthers()
    {
        var small = WriteDump("small.dmp", 10L * 1024 * 1024);
        var medium = WriteDump("medium.dmp", 50L * 1024 * 1024);
        var large = WriteDump("large.dmp", 200L * 1024 * 1024);

        var starts = new ConcurrentBag<(string path, long tick)>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        string? Fake(string path, Action<string>? log, CancellationToken ct, TimeSpan to)
        {
            starts.Add((path, sw.ElapsedTicks));
            Thread.Sleep(50);
            return "transcript-" + Path.GetFileName(path);
        }

        var result = await ParallelCdbRunner.RunAllAsync(
            new[] { small, medium, large },
            cdbPath: "synthetic",
            label: "test",
            log: null,
            ct: TestContext.Current.CancellationToken,
            health: null,
            cdbCacheRoot: _cacheRoot,
            runCdb: Fake);

        var startsList = starts.ToList();
        var largeStart = startsList.First(s => s.path == large).tick;
        var smallStart = startsList.First(s => s.path == small).tick;
        var mediumStart = startsList.First(s => s.path == medium).tick;
        Assert.True(largeStart < smallStart, "large should start before small");
        Assert.True(largeStart < mediumStart, "large should start before medium");
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task RunAllAsync_CacheHit_SkipsRunner()
    {
        var dump = WriteDump("cached.dmp", 1024);
        CdbAnalysisCache.Store(dump, "seeded-transcript", log: null, cacheRoot: _cacheRoot);

        var called = new ConcurrentBag<string>();
        string? Fake(string path, Action<string>? log, CancellationToken ct, TimeSpan to)
        {
            called.Add(path);
            return "should-not-be-used";
        }

        var result = await ParallelCdbRunner.RunAllAsync(
            new[] { dump },
            cdbPath: "synthetic",
            label: "test",
            log: null,
            ct: TestContext.Current.CancellationToken,
            health: null,
            cdbCacheRoot: _cacheRoot,
            runCdb: Fake);

        Assert.Empty(called);
        Assert.Equal("seeded-transcript", result[dump]);
    }

    [Fact]
    public async Task RunAllAsync_CacheMiss_WritesBackTranscript()
    {
        var a = WriteDump("a.dmp", 1024);
        var b = WriteDump("b.dmp", 2048);

        string? Fake(string path, Action<string>? log, CancellationToken ct, TimeSpan to) =>
            "transcript-for-" + Path.GetFileName(path);

        await ParallelCdbRunner.RunAllAsync(
            new[] { a, b },
            cdbPath: "synthetic",
            label: "test",
            log: null,
            ct: TestContext.Current.CancellationToken,
            health: null,
            cdbCacheRoot: _cacheRoot,
            runCdb: Fake);

        Assert.Equal("transcript-for-a.dmp", CdbAnalysisCache.TryLoad(a, log: null, cacheRoot: _cacheRoot));
        Assert.Equal("transcript-for-b.dmp", CdbAnalysisCache.TryLoad(b, log: null, cacheRoot: _cacheRoot));
    }

    [Fact]
    public async Task RunAllAsync_Cancellation_StopsFurtherWork()
    {
        var first = WriteDump("first.dmp", 200L * 1024 * 1024);
        var second = WriteDump("second.dmp", 1024);
        var third = WriteDump("third.dmp", 1024);

        using var cts = new CancellationTokenSource();
        var called = new ConcurrentBag<string>();

        string? Fake(string path, Action<string>? log, CancellationToken ct, TimeSpan to)
        {
            called.Add(path);
            if (Path.GetFileName(path) == "first.dmp")
                cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return "transcript-" + Path.GetFileName(path);
        }

        try
        {
            await ParallelCdbRunner.RunAllAsync(
                new[] { first, second, third },
                cdbPath: "synthetic",
                label: "test",
                log: null,
                ct: cts.Token,
                health: null,
                cdbCacheRoot: _cacheRoot,
                runCdb: Fake);
        }
        catch (OperationCanceledException) { }

        Assert.Contains(first, called);
        Assert.True(called.Count <= 1, $"only the pre-warm dump should have run before cancellation; saw {called.Count}");
    }

    [Fact]
    public async Task RunAllAsync_Empty_ReturnsEmptyDictionaryWithoutInvokingRunner()
    {
        var called = false;
        string? Fake(string path, Action<string>? log, CancellationToken ct, TimeSpan to) { called = true; return ""; }

        var result = await ParallelCdbRunner.RunAllAsync(
            Array.Empty<string>(),
            cdbPath: "synthetic",
            label: "test",
            log: null,
            ct: TestContext.Current.CancellationToken,
            health: null,
            cdbCacheRoot: _cacheRoot,
            runCdb: Fake);

        Assert.Empty(result);
        Assert.False(called);
    }

    [Fact]
    public async Task RunAllAsync_LogLines_EmitStartAndSummaryAndStayQuietPerDump()
    {
        var dump = WriteDump("WATCHDOG-x.dmp", 1024);
        var logs = new ConcurrentQueue<string>();

        string? Fake(string path, Action<string>? log, CancellationToken ct, TimeSpan to) => "out";

        await ParallelCdbRunner.RunAllAsync(
            new[] { dump },
            cdbPath: "synthetic",
            label: "minidumps",
            log: logs.Enqueue,
            ct: TestContext.Current.CancellationToken,
            health: null,
            cdbCacheRoot: _cacheRoot,
            runCdb: Fake);

        Assert.Contains(logs, l => l.Contains("Analyzing 1 minidumps with cdb"));
        Assert.Contains(logs, l => l.Contains("1/1 analyzed in") && l.Contains("0 cached, 1 fresh, 0 failed"));
        Assert.DoesNotContain(logs, l => l.Contains("WATCHDOG-x.dmp"));
    }

    [Fact]
    public async Task RunAllAsync_FailureLogsBracketedFilename()
    {
        var dump = WriteDump("bad.dmp", 1024);
        var logs = new ConcurrentQueue<string>();

        string? Fake(string path, Action<string>? log, CancellationToken ct, TimeSpan to) => null;

        await ParallelCdbRunner.RunAllAsync(
            new[] { dump },
            cdbPath: "synthetic",
            label: "minidumps",
            log: logs.Enqueue,
            ct: TestContext.Current.CancellationToken,
            health: null,
            cdbCacheRoot: _cacheRoot,
            runCdb: Fake);

        Assert.Contains(logs, l => l.Contains("[bad.dmp] cdb analysis failed"));
        Assert.Contains(logs, l => l.Contains("0 cached, 0 fresh, 1 failed"));
    }

    public static bool BenchmarksEnabled =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLARE_RUN_BENCHMARKS"));

    [Trait("Category", "Benchmark")]
    [Fact(Skip = "benchmark — set FLARE_RUN_BENCHMARKS=1 or filter Category=Benchmark",
        SkipUnless = nameof(BenchmarksEnabled), SkipType = typeof(ParallelCdbRunnerTests))]
    public async Task RunAllAsync_Parallel_AtLeastThreeTimesFasterThanSerial()
    {
        var dumps = new List<string>();
        for (int i = 0; i < 10; i++)
            dumps.Add(WriteDump($"bench_{i}.dmp", (i + 1) * 10L * 1024 * 1024));

        var delay = TimeSpan.FromMilliseconds(500);
        string? FakeCdb(string path, Action<string>? log, CancellationToken ct, TimeSpan to)
        {
            Thread.Sleep(delay);
            return "transcript";
        }

        var serialSw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var p in dumps)
            FakeCdb(p, null, TestContext.Current.CancellationToken, TimeSpan.FromSeconds(30));
        serialSw.Stop();

        var parallelRoot = Path.Combine(_tempDir, "parallel");
        Directory.CreateDirectory(parallelRoot);
        var parallelSw = System.Diagnostics.Stopwatch.StartNew();
        await ParallelCdbRunner.RunAllAsync(
            dumps,
            cdbPath: "synthetic",
            label: "test",
            log: null,
            ct: TestContext.Current.CancellationToken,
            health: null,
            cdbCacheRoot: parallelRoot,
            runCdb: FakeCdb);
        parallelSw.Stop();

        var speedup = (double)serialSw.ElapsedMilliseconds / parallelSw.ElapsedMilliseconds;
        var benchPath = Environment.GetEnvironmentVariable("FLARE_BENCH_OUT");
        if (!string.IsNullOrEmpty(benchPath))
            File.WriteAllText(benchPath, $"serial={serialSw.ElapsedMilliseconds}ms parallel={parallelSw.ElapsedMilliseconds}ms speedup={speedup:F2}x");
        Console.WriteLine($"FLARE benchmark: serial {serialSw.ElapsedMilliseconds}ms, parallel {parallelSw.ElapsedMilliseconds}ms, speedup {speedup:F2}x");
        Assert.True(speedup >= 3.0,
            $"expected >= 3x speedup, got {speedup:F2}x (serial {serialSw.ElapsedMilliseconds}ms, parallel {parallelSw.ElapsedMilliseconds}ms)");
    }
}
