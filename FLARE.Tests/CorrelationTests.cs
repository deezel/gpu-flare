using FLARE.Core;

namespace FLARE.Tests;

public class CorrelationTests
{
    private static NvlddmkmError MakeGpuError(DateTime ts) =>
        new(ts, 13, "test", 0, 0, 0, "Test Error");

    private static EventLogParser.AppCrashEvent MakeAppCrash(DateTime ts) =>
        new(ts, "game.exe", "nvlddmkm.sys", "game.exe crashed");

    [Fact]
    public void Correlate_CrashWithin30Seconds_ReturnsCorrelation()
    {
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        var gpuErrors = new List<NvlddmkmError> { MakeGpuError(baseTime) };
        var appCrashes = new List<EventLogParser.AppCrashEvent> { MakeAppCrash(baseTime.AddSeconds(15)) };
        var result = EventLogParser.CorrelateWithAppCrashes(gpuErrors, appCrashes);
        Assert.Single(result);
        Assert.Equal(15, result[0].secondsApart, precision: 1);
    }

    [Fact]
    public void Correlate_CrashOutsideWindow_ReturnsEmpty()
    {
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        var gpuErrors = new List<NvlddmkmError> { MakeGpuError(baseTime) };
        var appCrashes = new List<EventLogParser.AppCrashEvent> { MakeAppCrash(baseTime.AddSeconds(31)) };
        var result = EventLogParser.CorrelateWithAppCrashes(gpuErrors, appCrashes);
        Assert.Empty(result);
    }

    [Fact]
    public void Correlate_CrashExactlyAtBoundary_ReturnsCorrelation()
    {
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        var gpuErrors = new List<NvlddmkmError> { MakeGpuError(baseTime) };
        var appCrashes = new List<EventLogParser.AppCrashEvent> { MakeAppCrash(baseTime.AddSeconds(30)) };
        var result = EventLogParser.CorrelateWithAppCrashes(gpuErrors, appCrashes);
        Assert.Single(result);
    }

    [Fact]
    public void Correlate_CrashBeforeGpuError_StillCorrelates()
    {
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        var gpuErrors = new List<NvlddmkmError> { MakeGpuError(baseTime) };
        var appCrashes = new List<EventLogParser.AppCrashEvent> { MakeAppCrash(baseTime.AddSeconds(-10)) };
        var result = EventLogParser.CorrelateWithAppCrashes(gpuErrors, appCrashes);
        Assert.Single(result);
        Assert.Equal(10, result[0].secondsApart, precision: 1);
    }

    [Fact]
    public void Correlate_EmptyLists_ReturnsEmpty()
    {
        var result = EventLogParser.CorrelateWithAppCrashes([], []);
        Assert.Empty(result);
    }

    [Fact]
    public void Correlate_MultipleMatches_ReturnsAllPairs()
    {
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        var gpuErrors = new List<NvlddmkmError>
        {
            MakeGpuError(baseTime),
            MakeGpuError(baseTime.AddMinutes(5))
        };
        var appCrashes = new List<EventLogParser.AppCrashEvent>
        {
            MakeAppCrash(baseTime.AddSeconds(5)),
            MakeAppCrash(baseTime.AddMinutes(5).AddSeconds(10))
        };
        var result = EventLogParser.CorrelateWithAppCrashes(gpuErrors, appCrashes);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Correlate_CustomWindow_UsesProvidedValue()
    {
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        var gpuErrors = new List<NvlddmkmError> { MakeGpuError(baseTime) };
        var appCrashes = new List<EventLogParser.AppCrashEvent> { MakeAppCrash(baseTime.AddSeconds(15)) };
        var narrow = EventLogParser.CorrelateWithAppCrashes(gpuErrors, appCrashes, windowSeconds: 10);
        var wide = EventLogParser.CorrelateWithAppCrashes(gpuErrors, appCrashes, windowSeconds: 20);
        Assert.Empty(narrow);
        Assert.Single(wide);
    }

    [Fact]
    public void Correlate_UnsortedInput_StillProducesAllMatches()
    {
        // Two-pointer sweep relies on sorted inputs. Both Pull* call sites
        // finalize with OrderBy, but a test or future caller could hand in
        // an unsorted list — the correlator sorts defensively so the result
        // must not depend on caller-side ordering.
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        var gpuErrors = new List<NvlddmkmError>
        {
            MakeGpuError(baseTime.AddMinutes(10)),
            MakeGpuError(baseTime),
            MakeGpuError(baseTime.AddMinutes(5)),
        };
        var appCrashes = new List<EventLogParser.AppCrashEvent>
        {
            MakeAppCrash(baseTime.AddMinutes(5).AddSeconds(3)),
            MakeAppCrash(baseTime.AddSeconds(10)),
            MakeAppCrash(baseTime.AddMinutes(10).AddSeconds(-5)),
        };

        var result = EventLogParser.CorrelateWithAppCrashes(gpuErrors, appCrashes);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Correlate_LargeInput_BoundedWorkAndNoExplosion()
    {
        // O(n*m) would have produced 1M+ pairs from this shape. The two-pointer
        // sweep only emits pairs within the window, so the result stays linear
        // in the matches that actually fell inside ±windowSeconds.
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        var gpuErrors = new List<NvlddmkmError>();
        for (int i = 0; i < 2000; i++)
            gpuErrors.Add(MakeGpuError(baseTime.AddMinutes(i)));
        var appCrashes = new List<EventLogParser.AppCrashEvent>();
        for (int i = 0; i < 500; i++)
            appCrashes.Add(MakeAppCrash(baseTime.AddMinutes(i * 4).AddSeconds(5)));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = EventLogParser.CorrelateWithAppCrashes(gpuErrors, appCrashes);
        sw.Stop();

        // Only app crashes within 30s of a GPU error correlate. Given the above
        // spacing, every app crash lands exactly on a GPU-minute boundary +5s,
        // so each produces one pair — expect ~500, not the 1M of the old nested loop.
        Assert.Equal(500, result.Count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1),
            $"Correlate should stay well under 1s on this shape (elapsed {sw.Elapsed.TotalSeconds:F3}s)");
    }
}
