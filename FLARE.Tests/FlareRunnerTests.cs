using FLARE.Core;

namespace FLARE.Tests;

public class FlareRunnerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempDumpDir;

    public FlareRunnerTests()
    {
        var id = $"flare_runner_test_{Guid.NewGuid():N}";
        _tempDir = Path.Combine(Path.GetTempPath(), id);
        _tempDumpDir = Path.Combine(Path.GetTempPath(), id + "_dumps");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        try { if (Directory.Exists(_tempDumpDir)) Directory.Delete(_tempDumpDir, true); } catch { }
    }

    private FlareOptions Options(Action<FlareOptions>? tweak = null)
    {
        var o = new FlareOptions { ReportDir = _tempDir, MinidumpsDir = _tempDumpDir };
        tweak?.Invoke(o);
        return o;
    }

    private static GpuInfo FakeGpu() => new(
        "FakeGPU", "99.0.0.1", "0.0", "0x0", "GPU-01234567-89ab-cdef-0123-456789abcdef",
        "0000:01:00.0", 10, "1024 MB", 0, 0, 0, 0, 0, 1);

    private static SystemInfo FakeSystem() => new(
        "FakeBios", "1.0", "2025-01-01",
        "FakeBoard", "Model-X", "rev1",
        "FakeSys", "Machine",
        "Fake CPU", 1024UL * 1024 * 1024);

    private FlareDependencies FakeDeps(List<string> callOrder) => new(
        CollectGpu: (_, _) => { callOrder.Add("gpu"); return FakeGpu(); },
        CollectSystem: (_, _) => { callOrder.Add("system"); return FakeSystem(); },
        PullGpuErrors: (_, _, _, _) => { callOrder.Add("errors"); return []; },
        PullCrashEvents: (_, _, _) => { callOrder.Add("crashes"); return []; },
        PullAppCrashEvents: (_, _, _) => { callOrder.Add("appcrashes"); return []; },
        PullDriverInstalls: (_, _, _) => { callOrder.Add("drivers"); return []; },
        CopyCrashDumps: (_, _, _, _) => { callOrder.Add("minidumps"); return []; },
        GenerateDumpReport: (_, _, _, _, _) => { callOrder.Add("dumpanalysis"); return "fake"; });

    [Fact]
    public void Run_WithFakeDeps_CallsCollectorsInDocumentedOrder()
    {
        var callOrder = new List<string>();
        var options = Options(o => o.DeepAnalyze = true);

        FlareRunner.Run(options, log: null, ct: default, deps: FakeDeps(callOrder));

        Assert.Equal(
            new[] { "gpu", "system", "errors", "crashes", "appcrashes", "drivers", "minidumps" },
            callOrder);
    }

    [Fact]
    public void Run_WithFakeDeps_PopulatesResultAndWritesReport()
    {
        var options = Options(o => o.DeepAnalyze = true);

        var result = FlareRunner.Run(options, log: null, ct: default, deps: FakeDeps(new List<string>()));

        Assert.NotNull(result.Gpu);
        Assert.Equal("FakeGPU", result.Gpu.Name);
        Assert.NotNull(result.System);
        Assert.Equal("FakeSys", result.System.SystemManufacturer);
        Assert.NotEmpty(result.Report);
        Assert.Contains("FakeGPU", result.Report);
        Assert.True(File.Exists(result.SavedPath), "report file should be written to disk");
    }

    [Fact]
    public void Run_WithDeepAnalyzeDisabled_SkipsMinidumpCopy()
    {
        var callOrder = new List<string>();
        var logs = new List<string>();
        var options = Options(o => o.DeepAnalyze = false);

        var result = FlareRunner.Run(options, log: logs.Add, ct: default, deps: FakeDeps(callOrder));

        Assert.DoesNotContain("minidumps", callOrder);
        Assert.DoesNotContain("dumpanalysis", callOrder);
        Assert.False(Directory.Exists(_tempDumpDir));
        Assert.Contains(logs, l => l.Contains("Crash dump analysis skipped (disabled)"));
        Assert.Contains("[skipped] minidump analysis: disabled by user", result.Report);
    }

    [Fact]
    public void Run_WithRedactionEnabled_RedactsUuidInProgressLog()
    {
        var logs = new List<string>();
        var options = Options(o => o.RedactIdentifiers = true);

        FlareRunner.Run(options, logs.Add, ct: default, deps: FakeDeps(new List<string>()));

        Assert.DoesNotContain(logs, l => l.Contains("GPU-01234567-89ab-cdef-0123-456789abcdef"));
        Assert.Contains(logs, l => l.Contains("UUID:   [redacted]"));
    }

    [Fact]
    public void Run_WithRedactionDisabled_KeepsUuidInProgressLog()
    {
        var logs = new List<string>();
        var options = Options(o => o.RedactIdentifiers = false);

        FlareRunner.Run(options, logs.Add, ct: default, deps: FakeDeps(new List<string>()));

        Assert.Contains(logs, l => l.Contains("UUID:   GPU-01234567-89ab-cdef-0123-456789abcdef"));
    }

    [Fact]
    public void Run_ProgressLog_NamesApplicationLogSource()
    {
        var logs = new List<string>();

        FlareRunner.Run(Options(), logs.Add, ct: default, deps: FakeDeps(new List<string>()));

        Assert.Contains(logs, l => l.Contains("Application log", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Run_CreatesReportAndMinidumpDirectories()
    {
        var options = Options(o => o.DeepAnalyze = true);

        FlareRunner.Run(options, log: null, ct: default, deps: FakeDeps(new List<string>()));

        Assert.True(Directory.Exists(_tempDir), "report dir should be created");
        Assert.True(Directory.Exists(_tempDumpDir), "minidumps dir should be created");
    }

    [Fact]
    public void Run_DoesNotCreateMinidumpSubfolderInsideReportDir()
    {
        var options = Options(o => o.DeepAnalyze = true);

        FlareRunner.Run(options, log: null, ct: default, deps: FakeDeps(new List<string>()));

        Assert.False(Directory.Exists(Path.Combine(_tempDir, "minidumps")),
            "report folder must not contain a minidumps subfolder");
    }

    [Fact]
    public void Run_PreCancelledToken_ThrowsBeforeAnyCollectorIsCalled()
    {
        var callOrder = new List<string>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var options = Options(o => o.DeepAnalyze = true);

        Assert.Throws<OperationCanceledException>(() =>
            FlareRunner.Run(options, log: null, ct: cts.Token, deps: FakeDeps(callOrder)));

        Assert.Empty(callOrder);
    }

    [Fact]
    public void Run_CancellationDuringPipeline_StopsBeforeNextCollector()
    {
        var callOrder = new List<string>();
        using var cts = new CancellationTokenSource();

        var deps = new FlareDependencies(
            CollectGpu: (_, _) => { callOrder.Add("gpu"); cts.Cancel(); return FakeGpu(); },
            CollectSystem: (_, _) => { callOrder.Add("system"); return FakeSystem(); },
            PullGpuErrors: (_, _, _, _) => { callOrder.Add("errors"); return []; },
            PullCrashEvents: (_, _, _) => { callOrder.Add("crashes"); return []; },
            PullAppCrashEvents: (_, _, _) => { callOrder.Add("appcrashes"); return []; },
            PullDriverInstalls: (_, _, _) => { callOrder.Add("drivers"); return []; },
            CopyCrashDumps: (_, _, _, _) => { callOrder.Add("minidumps"); return []; },
            GenerateDumpReport: (_, _, _, _, _) => { callOrder.Add("dumpanalysis"); return ""; });

        var options = Options(o => o.DeepAnalyze = true);

        Assert.Throws<OperationCanceledException>(() =>
            FlareRunner.Run(options, log: null, ct: cts.Token, deps: deps));

        Assert.Equal(new[] { "gpu" }, callOrder);
    }

    [Fact]
    public void Run_CancellationDuringMinidumpCopy_Propagates()
    {
        var callOrder = new List<string>();
        using var cts = new CancellationTokenSource();

        var deps = new FlareDependencies(
            CollectGpu: (_, _) => { callOrder.Add("gpu"); return FakeGpu(); },
            CollectSystem: (_, _) => { callOrder.Add("system"); return FakeSystem(); },
            PullGpuErrors: (_, _, _, _) => { callOrder.Add("errors"); return []; },
            PullCrashEvents: (_, _, _) => { callOrder.Add("crashes"); return []; },
            PullAppCrashEvents: (_, _, _) => { callOrder.Add("appcrashes"); return []; },
            PullDriverInstalls: (_, _, _) => { callOrder.Add("drivers"); return []; },
            CopyCrashDumps: (_, _, _, ct) =>
            {
                callOrder.Add("minidumps");
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return [];
            },
            GenerateDumpReport: (_, _, _, _, _) => { callOrder.Add("dumpanalysis"); return ""; });

        var options = Options(o => o.DeepAnalyze = true);

        Assert.Throws<OperationCanceledException>(() =>
            FlareRunner.Run(options, log: null, ct: cts.Token, deps: deps));

        Assert.Equal(new[] { "gpu", "system", "errors", "crashes", "appcrashes", "drivers", "minidumps" }, callOrder);
    }

    [Fact]
    public void Run_AppendsMinidumpRowsToCrashesWhenDumpsAreStaged()
    {
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_tempDumpDir);
        var staged = Path.Combine(_tempDumpDir, "mini-2025.dmp");
        File.WriteAllBytes(staged, new byte[2048]);

        var deps = new FlareDependencies(
            CollectGpu: (_, _) => FakeGpu(),
            CollectSystem: (_, _) => FakeSystem(),
            PullGpuErrors: (_, _, _, _) => [],
            PullCrashEvents: (_, _, _) => [],
            PullAppCrashEvents: (_, _, _) => [],
            PullDriverInstalls: (_, _, _) => [],
            CopyCrashDumps: (_, _, _, _) => [staged],
            GenerateDumpReport: (_, _, _, _, _) => "");

        var result = FlareRunner.Run(Options(o => o.DeepAnalyze = true), log: null, ct: default, deps: deps);

        Assert.Contains(result.Crashes, c => c.Source == "MINIDUMP" && c.Description.Contains("mini-2025.dmp"));
    }

    [Fact]
    public void Run_WithDefaultDependencies_DegradesWithoutThrowing()
    {
        var options = Options(o =>
        {
            o.MaxDays = 7;
            o.MaxEvents = 100;
            o.DeepAnalyze = false;
        });
        var logs = new List<string>();

        var result = FlareRunner.Run(options, log: logs.Add, ct: default);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Report);
        Assert.True(File.Exists(result.SavedPath), "report file should be written even with empty collectors");
        Assert.Contains(logs, l => l.Contains("Crash dump analysis skipped (disabled)"));
        Assert.Contains("[skipped] minidump analysis: disabled by user", result.Report);
    }

    [Fact]
    public void Run_WithDeepAnalyzeDisabled_SkipsCopyAndCreatesNoStagingDir()
    {
        var callOrder = new List<string>();
        var options = Options(o => o.DeepAnalyze = false);
        var logs = new List<string>();

        var result = FlareRunner.Run(options, log: logs.Add, ct: default, deps: FakeDeps(callOrder));

        Assert.DoesNotContain("minidumps", callOrder);
        Assert.DoesNotContain("dumpanalysis", callOrder);
        Assert.Null(result.DumpAnalysis);
        Assert.False(Directory.Exists(_tempDumpDir),
            "MinidumpsDir should not be created when crash dump analysis is off");
        Assert.Contains(logs, l => l.Contains("Crash dump analysis skipped (disabled)"));
        Assert.Contains("[skipped] minidump analysis: disabled by user", result.Report);
    }

    [Fact]
    public void Run_ForwardsMaxDaysCutoffToMinidumpCopy()
    {
        DateTime? seenCutoff = null;
        var deps = new FlareDependencies(
            CollectGpu:        (_, _) => FakeGpu(),
            CollectSystem:     (_, _) => FakeSystem(),
            PullGpuErrors:     (_, _, _, _) => [],
            PullCrashEvents:   (_, _, _) => [],
            PullAppCrashEvents:(_, _, _) => [],
            PullDriverInstalls:(_, _, _) => [],
            CopyCrashDumps:    (_, cutoff, _, _) => { seenCutoff = cutoff; return []; },
            GenerateDumpReport:(_, _, _, _, _) => "");
        var options = Options(o =>
        {
            o.MaxDays = 7;
            o.DeepAnalyze = true;
        });

        var earliest = DateTime.Now.AddDays(-7).AddSeconds(-2);
        FlareRunner.Run(options, log: null, ct: default, deps: deps);
        var latest = DateTime.Now.AddDays(-7).AddSeconds(2);

        Assert.NotNull(seenCutoff);
        Assert.InRange(seenCutoff.Value, earliest, latest);
    }

    [Fact]
    public void Run_WithDeepAnalyzeDisabled_IgnoresPreStagedDumps()
    {
        Directory.CreateDirectory(_tempDumpDir);
        File.WriteAllBytes(Path.Combine(_tempDumpDir, "stale.dmp"), new byte[2048]);

        var callOrder = new List<string>();
        var logs = new List<string>();
        var deps = new FlareDependencies(
            CollectGpu:        (_, _) => FakeGpu(),
            CollectSystem:     (_, _) => FakeSystem(),
            PullGpuErrors:     (_, _, _, _) => [],
            PullCrashEvents:   (_, _, _) => [],
            PullAppCrashEvents:(_, _, _) => [],
            PullDriverInstalls:(_, _, _) => [],
            CopyCrashDumps:    (_, _, _, _) => { callOrder.Add("minidumps"); return []; },
            GenerateDumpReport:(_, _, _, _, _) => { callOrder.Add("dumpanalysis"); return "  Analyzed 1 crash dump(s):\n  stale.dmp\n"; });
        var options = Options(o => o.DeepAnalyze = false);

        var result = FlareRunner.Run(options, logs.Add, ct: default, deps: deps);

        Assert.DoesNotContain("minidumps", callOrder);
        Assert.DoesNotContain("dumpanalysis", callOrder);
        Assert.Null(result.DumpAnalysis);
        Assert.DoesNotContain("stale.dmp", result.Report);
        Assert.DoesNotContain(result.Crashes, c => c.Source == "MINIDUMP");
        Assert.Contains(logs, l => l.Contains("Crash dump analysis skipped (disabled)"));
        Assert.Contains("[skipped] minidump analysis: disabled by user", result.Report);
    }

    [Fact]
    public void Run_WithAllCollectorsPopulated_RendersEveryOptionalSection()
    {
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        var errors = new List<NvlddmkmError>
        {
            new(baseTime,                13, "Graphics SM", 0, 0, 0, "Page Fault"),
            new(baseTime.AddHours(2),    13, "Graphics SM", 0, 0, 0, "Page Fault"),
        };
        var crashes = new List<SystemCrashEvent>
        {
            new(baseTime.AddMinutes(5),  "BSOD",   1001, "BSOD description"),
        };
        var appCrashes = new List<EventLogParser.AppCrashEvent>
        {
            new(baseTime.AddSeconds(10), "game.exe", "nvlddmkm.sys", "game.exe crashed"),
        };
        var driverInstalls = new List<EventLogParser.DriverInstallEvent>
        {
            new(baseTime.AddDays(-3),    "32.0.15.7216", "setupapi: 32.0.15.7216"),
        };

        var deps = new FlareDependencies(
            CollectGpu:        (_, _) => FakeGpu(),
            CollectSystem:     (_, _) => FakeSystem(),
            PullGpuErrors:     (_, _, _, _) => errors,
            PullCrashEvents:   (_, _, _) => crashes,
            PullAppCrashEvents:(_, _, _) => appCrashes,
            PullDriverInstalls:(_, _, _) => driverInstalls,
            CopyCrashDumps:    (_, _, _, _) => [],
            GenerateDumpReport:(_, _, _, _, _) => "  Fake dump analysis body");

        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_tempDumpDir);
        File.WriteAllBytes(Path.Combine(_tempDumpDir, "trigger.dmp"), new byte[64]);

        var options = Options(o => o.DeepAnalyze = true);
        var result = FlareRunner.Run(options, log: null, ct: default, deps: deps);

        string[] expectedHeaders =
        [
            "GPU IDENTIFICATION",
            "SYSTEM IDENTIFICATION",
            "NVLDDMKM ERROR SUMMARY",
            "ERROR FREQUENCY (per week)",
            "DRIVER INSTALL HISTORY",
            "ERROR TIMELINE",
            "SYSTEM CRASHES",
            "APPLICATION CRASH CORRELATION",
            "APPLICATION CRASHES",
            "CRASH DUMP ANALYSIS",
            "SUMMARY",
        ];
        foreach (var header in expectedHeaders)
            Assert.Contains(header, result.Report);
    }

    [Fact]
    public void Run_SavedReportFileContentsMatchResultReport()
    {
        var options = Options(o => o.DeepAnalyze = true);

        var result = FlareRunner.Run(options, log: null, ct: default, deps: FakeDeps(new List<string>()));
        var onDisk = File.ReadAllText(result.SavedPath);

        Assert.Equal(result.Report, onDisk);
    }

    [Fact]
    public void Run_CollectorHealthIsObservableThroughDependencies()
    {
        var health = new CollectorHealth();
        var deps = FlareDependencies.Default(health) with
        {
            CollectGpu = (_, _) => FakeGpu(),
            CollectSystem = (_, _) => FakeSystem(),
            PullGpuErrors = (_, _, _, _) => [],
            PullCrashEvents = (_, _, _) => [],
            PullAppCrashEvents = (_, _, _) => [],
            PullDriverInstalls = (_, _, _) => [],
            CopyCrashDumps = (_, _, _, _) => [],
            GenerateDumpReport = (_, _, _, _, _) => "",
        };
        var options = Options(o => o.MaxDays = 42);

        var result = FlareRunner.Run(options, log: null, ct: default, deps: deps);

        Assert.Equal(42, deps.Health.Truncation.RequestedMaxDays);
        Assert.Contains("Requested window: last 42 day(s).", result.Report);
    }

    [Fact]
    public void Run_ResultExposesCollectorHealthSoUiCanDistinguishFailureFromEmpty()
    {
        var health = new CollectorHealth();
        health.Failure("Event Log: nvlddmkm", "access denied");
        var deps = FlareDependencies.Default(health) with
        {
            CollectGpu = (_, _) => FakeGpu(),
            CollectSystem = (_, _) => FakeSystem(),
            PullGpuErrors = (_, _, _, _) => [],
            PullCrashEvents = (_, _, _) => [],
            PullAppCrashEvents = (_, _, _) => [],
            PullDriverInstalls = (_, _, _) => [],
            CopyCrashDumps = (_, _, _, _) => [],
            GenerateDumpReport = (_, _, _, _, _) => "",
        };

        var result = FlareRunner.Run(Options(), log: null, ct: default, deps: deps);

        Assert.NotNull(result.Health);
        Assert.Same(health, result.Health);
        Assert.Contains(result.Health.Notices, n =>
            n.Kind == CollectorNoticeKind.Failure && n.Source == "Event Log: nvlddmkm");
    }

    [Fact]
    public void Run_CrashDumpCopySkippedByUacDecline_PipelineStillCompletes()
    {
        var logs = new List<string>();
        var deps = new FlareDependencies(
            CollectGpu: (_, _) => FakeGpu(),
            CollectSystem: (_, _) => FakeSystem(),
            PullGpuErrors: (_, _, _, _) => [],
            PullCrashEvents: (_, _, _) => [],
            PullAppCrashEvents: (_, _, _) => [],
            PullDriverInstalls: (_, _, _) => [],
            CopyCrashDumps: (_, _, log, _) =>
            {
                log?.Invoke("  Minidump copy skipped (UAC declined). Event log and GPU info still collected.");
                return [];
            },
            GenerateDumpReport: (_, _, _, _, _) => "");
        var options = Options(o => o.DeepAnalyze = true);

        var result = FlareRunner.Run(options, log: logs.Add, ct: default, deps: deps);

        Assert.NotEmpty(result.Report);
        Assert.True(File.Exists(result.SavedPath));
        Assert.Contains(logs, l => l.Contains("UAC declined"));
    }
}
