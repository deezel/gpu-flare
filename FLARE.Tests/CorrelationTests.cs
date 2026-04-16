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
}
