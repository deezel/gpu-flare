using FLARE.Core;
using static FLARE.Core.EventLogParser;

namespace FLARE.Tests;

public class LiveKernelDumpReportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cdbCacheRoot;

    public LiveKernelDumpReportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flare_lkr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _cdbCacheRoot = Path.Combine(_tempDir, "cdb-cache");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private Task<string> Generate(
        List<LiveKernelDump>? dumps = null,
        List<NvlddmkmError>? nvErrors = null,
        List<AppCrashEvent>? appCrashes = null,
        List<DriverInstallEvent>? drivers = null,
        int maxDays = 30,
        bool sortDescending = true,
        bool deepAnalysis = false,
        string? cdbPath = null) =>
        LiveKernelDumpReport.Generate(
            dumps ?? new List<LiveKernelDump>(),
            nvErrors ?? new List<NvlddmkmError>(),
            appCrashes ?? new List<AppCrashEvent>(),
            drivers ?? new List<DriverInstallEvent>(),
            maxDays,
            sortDescending,
            deepAnalysis,
            cdbPath,
            new CdbDetailsSink(),
            log: null,
            ct: TestContext.Current.CancellationToken,
            health: null,
            cdbCacheRoot: _cdbCacheRoot);

    private static byte[] BuildPageDu64(uint bugcheck, ulong p1 = 0, ulong p2 = 0, ulong p3 = 0, ulong p4 = 0)
    {
        var data = new byte[256];
        System.Text.Encoding.ASCII.GetBytes("PAGEDU64").CopyTo(data, 0);
        BitConverter.GetBytes(bugcheck).CopyTo(data, 0x38);
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x3C);
        BitConverter.GetBytes(p1).CopyTo(data, 0x40);
        BitConverter.GetBytes(p2).CopyTo(data, 0x48);
        BitConverter.GetBytes(p3).CopyTo(data, 0x50);
        BitConverter.GetBytes(p4).CopyTo(data, 0x58);
        return data;
    }

    private LiveKernelDump WriteDumpAndDescribe(string dir, string name, string category, uint bugcheck)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, BuildPageDu64(bugcheck));
        var fi = new FileInfo(path);
        return new LiveKernelDump(path, name, category, fi.LastWriteTime, fi.Length);
    }

    [Fact]
    public async Task Generate_NoDumps_PrintsNoDumpsLine()
    {
        var body = await Generate();

        Assert.Contains("No live kernel dumps found in last 30 day(s).", body);
    }

    [Fact]
    public async Task Generate_EmptyDumpsWithUpstreamFailure_PointsToScopeBlock()
    {
        var health = new CollectorHealth();
        health.Skipped("minidump copy", "UAC prompt declined; crash dump files were not copied");

        var body = await LiveKernelDumpReport.Generate(
            new List<LiveKernelDump>(),
            new(), new(), new(),
            maxDays: 30, sortDescending: true,
            deepAnalysis: true, cdbPath: null,
            sink: new CdbDetailsSink(),
            log: null, ct: TestContext.Current.CancellationToken, health: health, cdbCacheRoot: _cdbCacheRoot);

        Assert.Contains("upstream collection was skipped or failed", body);
        Assert.Contains("SCOPE block", body);
        Assert.DoesNotContain("No live kernel dumps found in last 30 day(s).", body);
    }

    [Fact]
    public async Task Generate_EmptyDumpsNoUpstreamIssue_KeepsCleanWording()
    {
        var body = await LiveKernelDumpReport.Generate(
            new List<LiveKernelDump>(),
            new(), new(), new(),
            maxDays: 30, sortDescending: true,
            deepAnalysis: true, cdbPath: null,
            sink: new CdbDetailsSink(),
            log: null, ct: TestContext.Current.CancellationToken, health: new CollectorHealth(), cdbCacheRoot: _cdbCacheRoot);

        Assert.Contains("No live kernel dumps found in last 30 day(s).", body);
        Assert.DoesNotContain("upstream collection was skipped or failed", body);
    }

    [Fact]
    public async Task Generate_SingleDump_RendersHeaderClassificationAndGpuRelatedMarker()
    {
        var dump = WriteDumpAndDescribe(_tempDir, "WATCHDOG-20260512-2148.dmp", "WATCHDOG", 0x141);

        var body = await Generate(new List<LiveKernelDump> { dump });

        Assert.Contains("[WATCHDOG] WATCHDOG-20260512-2148.dmp", body);
        Assert.Contains("0x141 VIDEO_ENGINE_TIMEOUT_DETECTED", body);
        Assert.Contains("Classification:", body);
        Assert.Contains("live dump", body);
        Assert.Contains("⚠️ **GPU-RELATED**", body);
        Assert.Contains("Source folder: `C:\\Windows\\LiveKernelReports`", body);
    }

    [Fact]
    public async Task Generate_NonGpuDump_OmitsGpuRelatedMarker()
    {
        var dump = WriteDumpAndDescribe(_tempDir, "x.dmp", "OTHER:Foo", 0x1A);

        var body = await Generate(new List<LiveKernelDump> { dump });

        Assert.DoesNotContain("⚠️ **GPU-RELATED**", body);
    }

    [Fact]
    public async Task Generate_DeepAnalysisWithCdbOutput_IncludesWinDbgBlock()
    {
        var dump = WriteDumpAndDescribe(_tempDir, "WATCHDOG-x.dmp", "WATCHDOG", 0x141);
        SeedCdbCache(dump.FullPath, CDB_PROCESS_NAME_SYSTEM_SAMPLE);

        var body = await LiveKernelDumpReport.Generate(
            new List<LiveKernelDump> { dump },
            new(), new(), new(),
            maxDays: 30, sortDescending: true,
            deepAnalysis: true, cdbPath: SyntheticCdbPath(),
            sink: new CdbDetailsSink(),
            log: null, ct: TestContext.Current.CancellationToken, health: null, cdbCacheRoot: _cdbCacheRoot);

        Assert.Contains("**WinDbg Analysis**", body);
        Assert.Contains("**PROCESS_NAME:**", body);
        Assert.Contains("`System`", body);
        Assert.Contains("**MODULE_NAME:**", body);
        Assert.Contains("`nvlddmkm`", body);
        Assert.Contains("PROCESS_NAME = System` is normal for scheduler worker-thread crashes", body);
        Assert.Contains($"(./{CdbDetailsSink.DumpsFilenamePlaceholder}#WATCHDOG-x.dmp)", body);
    }

    [Fact]
    public async Task Generate_DeepAnalysisWithNonSystemProcess_OmitsSystemNote()
    {
        var dump = WriteDumpAndDescribe(_tempDir, "OTHER-x.dmp", "WATCHDOG", 0x141);
        SeedCdbCache(dump.FullPath, CDB_PROCESS_NAME_NON_SYSTEM_SAMPLE);

        var body = await LiveKernelDumpReport.Generate(
            new List<LiveKernelDump> { dump },
            new(), new(), new(),
            maxDays: 30, sortDescending: true,
            deepAnalysis: true, cdbPath: SyntheticCdbPath(),
            sink: new CdbDetailsSink(),
            log: null, ct: TestContext.Current.CancellationToken, health: null, cdbCacheRoot: _cdbCacheRoot);

        Assert.Contains("**WinDbg Analysis**", body);
        Assert.DoesNotContain("scheduler worker-thread", body);
    }

    private const string CDB_PROCESS_NAME_SYSTEM_SAMPLE = @"
*                        Bugcheck Analysis                                    *
BUGCHECK_STR:  0x141
PROCESS_NAME:  System
MODULE_NAME:  nvlddmkm
IMAGE_NAME:  nvlddmkm.sys
FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
STACK_TEXT:
nt!KeBugCheckEx
dxgkrnl!TdrTimedOperationDelay
nvlddmkm+0x12345

";

    private const string CDB_PROCESS_NAME_NON_SYSTEM_SAMPLE = @"
*                        Bugcheck Analysis                                    *
BUGCHECK_STR:  0x141
PROCESS_NAME:  game.exe
MODULE_NAME:  nvlddmkm
IMAGE_NAME:  nvlddmkm.sys
FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
STACK_TEXT:
nt!KeBugCheckEx
nvlddmkm+0x12345

";

    private static string SyntheticCdbPath() =>
        Path.Combine(Path.GetTempPath(), "synthetic_cdb.exe");

    private void SeedCdbCache(string dumpPath, string transcript)
    {
        CdbAnalysisCache.Store(dumpPath, transcript, log: null, cacheRoot: _cdbCacheRoot);
    }

    [Fact]
    public async Task Generate_NoCorrelationsInWindow_PrintsNoneInWindow()
    {
        var t = new DateTime(2026, 5, 12, 21, 48, 56);
        var dump = WriteDumpAtTime("WATCHDOG-x.dmp", "WATCHDOG", 0x141, t);

        var body = await Generate(new List<LiveKernelDump> { dump });

        Assert.Contains("**Correlation** (±60s window):", body);
        Assert.Contains("nvlddmkm 13/14/153:", body);
        Assert.Contains("none in window", body);
        Assert.Contains("app crashes/hangs:", body);
        Assert.Contains("nearest driver install:", body);
    }

    [Fact]
    public async Task Generate_NvlddmkmWithinWindow_RendersClosestMatch()
    {
        var t = new DateTime(2026, 5, 12, 21, 48, 56);
        var dump = WriteDumpAtTime("WATCHDOG-x.dmp", "WATCHDOG", 0x141, t);
        var nv = new List<NvlddmkmError>
        {
            new NvlddmkmError(t.AddSeconds(-1), 153, "", null, null, null, "TDR"),
            new NvlddmkmError(t.AddMinutes(10),  13, "", null, null, null, "SM"),
        };

        var body = await Generate(new List<LiveKernelDump> { dump }, nvErrors: nv);

        Assert.Contains("ID:153", body);
        Assert.DoesNotContain("ID:13 ", body);
    }

    [Fact]
    public async Task Generate_AppCrashWithinWindow_PrintsAppNameAndOffset()
    {
        var t = new DateTime(2026, 5, 12, 21, 48, 56);
        var dump = WriteDumpAtTime("WATCHDOG-x.dmp", "WATCHDOG", 0x141, t);
        var apps = new List<AppCrashEvent>
        {
            new AppCrashEvent(t.AddSeconds(1), "MicrosoftSecurityApp.exe", "", ""),
        };

        var body = await Generate(new List<LiveKernelDump> { dump }, appCrashes: apps);

        Assert.Contains("MicrosoftSecurityApp.exe", body);
        Assert.Contains("+1s", body);
    }

    [Fact]
    public async Task Generate_DriverInstall_PicksMostRecentPrior()
    {
        var t = new DateTime(2026, 5, 12, 21, 48, 56);
        var dump = WriteDumpAtTime("WATCHDOG-x.dmp", "WATCHDOG", 0x141, t);
        var drivers = new List<DriverInstallEvent>
        {
            new DriverInstallEvent(t.AddDays(-300), "31.0.15.5219", ""),
            new DriverInstallEvent(t.AddDays(-20),  "32.0.15.7250", ""),
        };

        var body = await Generate(new List<LiveKernelDump> { dump }, drivers: drivers);

        Assert.Contains("32.0.15.7250", body);
        Assert.DoesNotContain("31.0.15.5219", body);
    }

    [Fact]
    public async Task Generate_NoDriversAtAll_PrintsNoneFound()
    {
        var t = new DateTime(2026, 5, 12, 21, 48, 56);
        var dump = WriteDumpAtTime("WATCHDOG-x.dmp", "WATCHDOG", 0x141, t);

        var body = await Generate(new List<LiveKernelDump> { dump });

        Assert.Contains("nearest driver install: none found", body);
    }

    private LiveKernelDump WriteDumpAtTime(string name, string category, uint bugcheck, DateTime when)
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, BuildPageDu64(bugcheck));
        File.SetLastWriteTime(path, when);
        var fi = new FileInfo(path);
        return new LiveKernelDump(path, name, category, fi.LastWriteTime, fi.Length);
    }

    [Fact]
    public async Task Generate_LocatorCapHit_EchoesSuffixAndNoteInline()
    {
        var t = new DateTime(2026, 5, 12, 21, 48, 56);
        var dump = WriteDumpAtTime("WATCHDOG-x.dmp", "WATCHDOG", 0x141, t);
        var health = new CollectorHealth();
        health.Truncation.LiveKernelScanCap = true;
        health.Truncation.LiveKernelScanTotal = 200;

        var body = await LiveKernelDumpReport.Generate(
            new List<LiveKernelDump> { dump },
            new(), new(), new(),
            maxDays: 30, sortDescending: true,
            deepAnalysis: false, cdbPath: null,
            sink: new CdbDetailsSink(),
            log: null, ct: TestContext.Current.CancellationToken, health: health, cdbCacheRoot: _cdbCacheRoot);

        Assert.Contains("Scanned: 1 dump(s) within last 30 day(s) (capped — see SCOPE block)", body);
        Assert.Contains("> Note: livekernel scan capped at 1 of 200 dump(s)", body);
    }

    [Fact]
    public async Task Generate_NoLocatorCap_OmitsCapSuffixAndNote()
    {
        var t = new DateTime(2026, 5, 12, 21, 48, 56);
        var dump = WriteDumpAtTime("WATCHDOG-x.dmp", "WATCHDOG", 0x141, t);

        var body = await LiveKernelDumpReport.Generate(
            new List<LiveKernelDump> { dump },
            new(), new(), new(),
            maxDays: 30, sortDescending: true,
            deepAnalysis: false, cdbPath: null,
            sink: new CdbDetailsSink(),
            log: null, ct: TestContext.Current.CancellationToken, health: new CollectorHealth(), cdbCacheRoot: _cdbCacheRoot);

        Assert.Contains("Scanned: 1 dump(s) within last 30 day(s)", body);
        Assert.DoesNotContain("(capped — see SCOPE block)", body);
        Assert.DoesNotContain("livekernel scan capped at", body);
    }

    [Fact]
    public async Task Generate_SortAscending_OrdersOldestFirst()
    {
        var t = new DateTime(2026, 5, 12, 12, 0, 0);
        var a = WriteDumpAtTime("a.dmp", "WATCHDOG", 0x141, t);
        var b = WriteDumpAtTime("b.dmp", "WATCHDOG", 0x141, t.AddHours(1));

        var body = await LiveKernelDumpReport.Generate(
            new List<LiveKernelDump> { a, b },
            new(), new(), new(),
            maxDays: 30, sortDescending: false,
            deepAnalysis: false, cdbPath: null,
            sink: new CdbDetailsSink(),
            log: null, ct: TestContext.Current.CancellationToken, health: null, cdbCacheRoot: _cdbCacheRoot);

        var aIdx = body.IndexOf("a.dmp", StringComparison.Ordinal);
        var bIdx = body.IndexOf("b.dmp", StringComparison.Ordinal);
        Assert.True(aIdx < bIdx, "ascending mode should print older entry first");
    }

    [Fact]
    public async Task Generate_DeletedDumpButCachedTranscript_SurfacesAsOrphanWithSourceRemovedMarker()
    {
        var dumpName = "WATCHDOG-20260513-0930.dmp";
        var dumpPath = Path.Combine(_tempDir, dumpName);
        File.WriteAllBytes(dumpPath, new byte[1024]);
        Directory.CreateDirectory(_cdbCacheRoot);
        CdbAnalysisCache.Store(dumpPath, CDB_PROCESS_NAME_SYSTEM_SAMPLE, log: null, cacheRoot: _cdbCacheRoot);
        File.Delete(dumpPath);

        var body = await LiveKernelDumpReport.Generate(
            new List<LiveKernelDump>(),
            new(), new(), new(),
            maxDays: 365, sortDescending: true,
            deepAnalysis: true, cdbPath: SyntheticCdbPath(),
            sink: new CdbDetailsSink(),
            log: null, ct: TestContext.Current.CancellationToken, health: null,
            cdbCacheRoot: _cdbCacheRoot);

        Assert.Contains("Cached analyses (source dump no longer present)", body);
        Assert.Contains(dumpName, body);
        Assert.Contains("_(source removed)_", body);
        Assert.Contains("**FAILURE_BUCKET_ID:**", body);
    }

    [Fact]
    public async Task Generate_OrphanCount_Above_MaxLiveKernelDumpsCap_TrimsToCapAndFlagsTruncation()
    {
        Directory.CreateDirectory(_cdbCacheRoot);
        var baseTime = DateTime.Now;
        for (int i = 0; i < 5; i++)
        {
            var name = $"WATCHDOG-2026{i:D2}05-1200.dmp";
            var path = Path.Combine(_tempDir, name);
            File.WriteAllBytes(path, new byte[64]);
            File.SetLastWriteTime(path, baseTime.AddMinutes(-i));
            CdbAnalysisCache.Store(path, CDB_PROCESS_NAME_SYSTEM_SAMPLE, log: null, cacheRoot: _cdbCacheRoot);
            File.Delete(path);
        }

        var health = new CollectorHealth();
        health.Truncation.MaxLiveKernelDumpsCap = 3;

        var body = await LiveKernelDumpReport.Generate(
            new List<LiveKernelDump>(),
            new(), new(), new(),
            maxDays: 365, sortDescending: true,
            deepAnalysis: false, cdbPath: null,
            sink: new CdbDetailsSink(),
            log: null, ct: TestContext.Current.CancellationToken, health: health,
            cdbCacheRoot: _cdbCacheRoot);

        Assert.True(health.Truncation.LiveKernelOrphanCap);
        Assert.Equal(5, health.Truncation.LiveKernelOrphanTotal);
        Assert.DoesNotContain(health.Notices, n => n.Source == "livekernel orphan cap");
        var orphanHits = 0;
        for (int i = 0; i < 5; i++)
            if (body.Contains($"WATCHDOG-2026{i:D2}05-1200.dmp", StringComparison.Ordinal))
                orphanHits++;
        Assert.Equal(3, orphanHits);
    }

    [Fact]
    public async Task Generate_DeepAnalysis_PreservesDateOrderRegardlessOfCdbCompletion()
    {
        var t = new DateTime(2026, 5, 12, 12, 0, 0);
        var a = WriteDumpAtTime("WATCHDOG-a.dmp", "WATCHDOG", 0x141, t);
        var b = WriteDumpAtTime("WATCHDOG-b.dmp", "WATCHDOG", 0x141, t.AddHours(2));

        Func<IReadOnlyList<string>, string, Action<string>?, CancellationToken, CollectorHealth?, string?, Task<IReadOnlyDictionary<string, string?>>> runner =
            (paths, _, _, _, _, _) =>
            {
                IReadOnlyDictionary<string, string?> d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    [a.FullPath] = "BUGCHECK_STR: 0x141\nMODULE_NAME: nvlddmkm\nSTACK_TEXT:\n  nt!FrameA\n\n",
                    [b.FullPath] = "BUGCHECK_STR: 0x141\nMODULE_NAME: nvlddmkm\nSTACK_TEXT:\n  nt!FrameB\n\n",
                };
                return Task.FromResult(d);
            };

        var body = await LiveKernelDumpReport.Generate(
            new List<LiveKernelDump> { a, b },
            new(), new(), new(),
            maxDays: 30, sortDescending: true,
            deepAnalysis: true, cdbPath: SyntheticCdbPath(),
            sink: new CdbDetailsSink(),
            log: null, ct: TestContext.Current.CancellationToken, health: null,
            cdbCacheRoot: _cdbCacheRoot,
            runAllCdb: runner);

        var aIdx = body.IndexOf("WATCHDOG-a.dmp", StringComparison.Ordinal);
        var bIdx = body.IndexOf("WATCHDOG-b.dmp", StringComparison.Ordinal);
        Assert.True(bIdx >= 0 && aIdx > bIdx, "newer dump (b) should render before older one (a)");
    }

    [Fact]
    public async Task Generate_LiveDumpAndOrphan_RendersBothInSeparateSections()
    {
        var live = WriteDumpAndDescribe(_tempDir, "WATCHDOG-live.dmp", "WATCHDOG", 0x141);

        var orphanName = "WATCHDOG-20260513-2200.dmp";
        var orphanPath = Path.Combine(_tempDir, orphanName);
        File.WriteAllBytes(orphanPath, new byte[2048]);
        Directory.CreateDirectory(_cdbCacheRoot);
        CdbAnalysisCache.Store(orphanPath, CDB_PROCESS_NAME_SYSTEM_SAMPLE, log: null, cacheRoot: _cdbCacheRoot);
        File.Delete(orphanPath);

        var body = await LiveKernelDumpReport.Generate(
            new List<LiveKernelDump> { live },
            new(), new(), new(),
            maxDays: 365, sortDescending: true,
            deepAnalysis: false, cdbPath: null,
            sink: new CdbDetailsSink(),
            log: null, ct: TestContext.Current.CancellationToken, health: null,
            cdbCacheRoot: _cdbCacheRoot);

        var liveIdx = body.IndexOf("WATCHDOG-live.dmp", StringComparison.Ordinal);
        var orphanHeaderIdx = body.IndexOf("Cached analyses (source dump no longer present)", StringComparison.Ordinal);
        var orphanIdx = body.IndexOf(orphanName, StringComparison.Ordinal);
        Assert.True(liveIdx > 0, "live dump should appear");
        Assert.True(orphanHeaderIdx > liveIdx, "orphan section should follow the live dump section");
        Assert.True(orphanIdx > orphanHeaderIdx, "orphan entry should appear under its header");
    }
}
