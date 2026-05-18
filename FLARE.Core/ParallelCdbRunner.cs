using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FLARE.Core;

internal static class ParallelCdbRunner
{
    private const long SmallDumpBytes = 100L * 1024 * 1024;
    private const long MediumDumpBytes = 500L * 1024 * 1024;
    private const int SmallWeight = 1;
    private const int MediumWeight = 2;
    private const int LargeWeight = 4;
    private const int MaxParallelismCap = 6;
    private const int ProgressLineMinTotal = 20;

    private enum RunOutcome { Cached, Fresh, Failed }

    private sealed class Counters
    {
        public int Completed;
        public int Cached;
        public int Fresh;
        public int Failed;
    }

    public static async Task<IReadOnlyDictionary<string, string?>> RunAllAsync(
        IReadOnlyList<string> dumpPaths,
        string cdbPath,
        string label,
        Action<string>? log,
        CancellationToken ct,
        CollectorHealth? health,
        string? cdbCacheRoot = null,
        Func<string, Action<string>?, CancellationToken, TimeSpan, string?>? runCdb = null)
    {
        var results = new ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (dumpPaths.Count == 0)
            return results;

        runCdb ??= (p, l, c, t) => ProcessRunner.RunWithLog(cdbPath, l, c, t, "-z", p, "-c", "!analyze -v; q");

        var ordered = dumpPaths
            .Select(p => new { Path = p, Size = SafeSize(p) })
            .OrderByDescending(x => x.Size)
            .ToList();

        var parallelism = Math.Min(Environment.ProcessorCount, MaxParallelismCap);
        var total = ordered.Count;
        var counters = new Counters();
        var progressStep = total >= ProgressLineMinTotal ? Math.Max(10, total / 20) : int.MaxValue;
        var sw = Stopwatch.StartNew();

        log?.Invoke($"  Analyzing {total} {label} with cdb (parallel, up to {parallelism})...");

        void OnComplete(RunOutcome outcome)
        {
            switch (outcome)
            {
                case RunOutcome.Cached: Interlocked.Increment(ref counters.Cached); break;
                case RunOutcome.Fresh:  Interlocked.Increment(ref counters.Fresh);  break;
                case RunOutcome.Failed: Interlocked.Increment(ref counters.Failed); break;
            }
            var done = Interlocked.Increment(ref counters.Completed);
            if (done < total && done % progressStep == 0)
                log?.Invoke($"  {done}/{total} ({done * 100 / total}%)");
        }

        await RunOneAsync(ordered[0].Path, runCdb, log, ct, cdbCacheRoot, results, OnComplete).ConfigureAwait(false);

        if (ordered.Count > 1)
        {
            using var capacity = new SemaphoreSlim(parallelism, parallelism);
            var remaining = ordered.Skip(1).ToList();

            await Parallel.ForEachAsync(
                remaining,
                new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct },
                async (entry, token) =>
                {
                    var weight = WeightFor(entry.Size);
                    for (int i = 0; i < weight; i++)
                        await capacity.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        await RunOneAsync(entry.Path, runCdb, log, token, cdbCacheRoot, results, OnComplete).ConfigureAwait(false);
                    }
                    finally
                    {
                        capacity.Release(weight);
                    }
                }).ConfigureAwait(false);
        }

        sw.Stop();
        log?.Invoke($"  {total}/{total} analyzed in {FormatElapsed(sw.Elapsed)} ({counters.Cached} cached, {counters.Fresh} fresh, {counters.Failed} failed).");

        return results;
    }

    private static async Task RunOneAsync(
        string dumpPath,
        Func<string, Action<string>?, CancellationToken, TimeSpan, string?> runCdb,
        Action<string>? log,
        CancellationToken ct,
        string? cdbCacheRoot,
        ConcurrentDictionary<string, string?> results,
        Action<RunOutcome> onComplete)
    {
        var cached = CdbAnalysisCache.TryLoad(dumpPath, log, cdbCacheRoot);
        if (cached != null)
        {
            results[dumpPath] = cached;
            onComplete(RunOutcome.Cached);
            return;
        }

        var transcript = await Task.Run(() => runCdb(dumpPath, log, ct, ProcessRunner.CdbSyncTimeout), ct)
            .ConfigureAwait(false);
        if (transcript != null)
        {
            CdbAnalysisCache.Store(dumpPath, transcript, log, cdbCacheRoot);
            results[dumpPath] = transcript;
            onComplete(RunOutcome.Fresh);
        }
        else
        {
            log?.Invoke($"  [{Path.GetFileName(dumpPath)}] cdb analysis failed");
            results[dumpPath] = null;
            onComplete(RunOutcome.Failed);
        }
    }

    private static string FormatElapsed(TimeSpan t)
        => t.TotalSeconds < 60
            ? $"{(int)t.TotalSeconds}s"
            : $"{(int)t.TotalMinutes}m {t.Seconds}s";

    private static int WeightFor(long size) =>
        size >= MediumDumpBytes ? LargeWeight :
        size >= SmallDumpBytes ? MediumWeight :
        SmallWeight;

    private static long SafeSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
