using FLARE.Core;

namespace FLARE.Tests;

public class SetupApiLogTests : IDisposable
{
    private readonly string _tempFile;
    private readonly List<string> _tempDirs = [];

    public SetupApiLogTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"flare_setupapi_test_{Guid.NewGuid():N}.log");
    }

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch { }
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private string CreateFakeWindowsDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"flare_setupapi_windows_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "INF"));
        _tempDirs.Add(dir);
        return dir;
    }

    [Fact]
    public void ParseSetupApiLog_CurrentGenDriver_Parsed()
    {
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2025/03/15 10:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 10:05:00.000",
            @"inf:   Driver Version = 6.14,32.0.15.8129"
        ]);
        var result = EventLogParser.ParseSetupApiLog(_tempFile);
        Assert.Single(result);
        Assert.Equal("32.0.15.8129", result[0].DriverVersion);
    }

    [Fact]
    public void ParseSetupApiLog_OlderGenDriver_Parsed()
    {
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2024/06/01 10:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 10:05:00.000",
            @"inf:   Driver Version = 6.14,31.0.15.5599"
        ]);
        var result = EventLogParser.ParseSetupApiLog(_tempFile);
        Assert.Single(result);
        Assert.Equal("31.0.15.5599", result[0].DriverVersion);
    }

    [Fact]
    public void ParseSetupApiLog_FutureGenDriver_Parsed()
    {
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2026/01/01 10:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 10:05:00.000",
            @"inf:   Driver Version = 6.14,33.0.15.1234"
        ]);
        var result = EventLogParser.ParseSetupApiLog(_tempFile);
        Assert.Single(result);
        Assert.Equal("33.0.15.1234", result[0].DriverVersion);
    }

    [Fact]
    public void ParseSetupApiLog_EmptyFile_ReturnsEmpty()
    {
        File.WriteAllText(_tempFile, "");
        var result = EventLogParser.ParseSetupApiLog(_tempFile);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseSetupApiLog_NonNvidiaVersionPattern_Skipped()
    {
        // The regex deliberately matches only the ".0.15." middle-octet NVIDIA
        // pattern. A non-`.15.` Driver Version line — e.g. a storage/audio driver
        // using a different middle octet — must NOT be treated as an NVIDIA install.
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2025/06/01 10:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 10:05:00.000",
            @"inf:   Driver Version = 6.14,10.0.22621.1234"   // non-NVIDIA vendor pattern
        ]);
        var result = EventLogParser.ParseSetupApiLog(_tempFile);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseSetupApiLog_EntryOlderThanCutoff_Dropped()
    {
        // MaxDays must narrow setupapi history alongside the Event Log queries.
        // Without this filter, a year-old install appears in a "last 7 days"
        // report and implies a correlation that's outside the selected window.
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2023/03/15 10:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 10:05:00.000",
            @"inf:   Driver Version = 6.14,32.0.15.8129"
        ]);

        var cutoff = new DateTime(2025, 1, 1);
        var result = EventLogParser.ParseSetupApiLog(_tempFile, cutoff);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseSetupApiLog_EntryInsideCutoff_Retained()
    {
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2025/06/01 10:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 10:05:00.000",
            @"inf:   Driver Version = 6.14,32.0.15.8129"
        ]);

        var cutoff = new DateTime(2025, 1, 1);
        var result = EventLogParser.ParseSetupApiLog(_tempFile, cutoff);

        Assert.Single(result);
        Assert.Equal("32.0.15.8129", result[0].DriverVersion);
    }

    [Fact]
    public void ParseSetupApiLog_NullCutoff_AcceptsAllEntries()
    {
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2020/01/01 10:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 10:05:00.000",
            @"inf:   Driver Version = 6.14,31.0.15.5599"
        ]);

        var result = EventLogParser.ParseSetupApiLog(_tempFile);

        Assert.Single(result);
    }

    [Fact]
    public void ParseSetupApiLog_PartialSchemeDrift_FiresCanaryEvenWithSomeMatches()
    {
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2025/03/15 10:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 10:05:00.000",
            @"inf:   Driver Version = 6.14,32.0.15.8129",
            @"Boot Session: 2025/04/15 10:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 10:05:00.000",
            @"inf:   Driver Version = 6.14,32.1.20.9999"
        ]);
        var health = new CollectorHealth();

        var result = EventLogParser.ParseSetupApiLog(_tempFile, cutoff: null, log: null, health: health);

        Assert.Single(result);
        Assert.Equal("32.0.15.8129", result[0].DriverVersion);
        Assert.Single(health.Notices);
        Assert.Equal(CollectorNoticeKind.Canary, health.Notices[0].Kind);
        Assert.Equal("setupapi version scheme", health.Notices[0].Source);
        Assert.Contains("1 matched and were parsed", health.Notices[0].Message);
    }

    [Fact]
    public void ParseSetupApiLog_InstallAfterMidnight_RollsIntoNextDay()
    {
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2025/03/15 23:50:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 00:10:00.000",
            @"inf:   Driver Version = 6.14,32.0.15.8129"
        ]);

        var result = EventLogParser.ParseSetupApiLog(_tempFile);

        Assert.Single(result);
        Assert.Equal(new DateTime(2025, 3, 16, 0, 10, 0), result[0].Timestamp);
    }

    [Fact]
    public void ParseSetupApiLog_InstallSameDayAsBoot_DoesNotRoll()
    {
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2025/03/15 10:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 10:05:00.000",
            @"inf:   Driver Version = 6.14,32.0.15.8129"
        ]);

        var result = EventLogParser.ParseSetupApiLog(_tempFile);

        Assert.Single(result);
        Assert.Equal(new DateTime(2025, 3, 15, 10, 5, 0), result[0].Timestamp);
    }

    [Fact]
    public void ParseSetupApiLog_RealDeviceInstallSection_VersionBeforeInstallDevice_Parsed()
    {
        File.WriteAllLines(_tempFile, [
            @">>>  [Device Install (DiInstallDevice) - PCI\VEN_10DE&DEV_2B85&SUBSYS_0000&REV_00\DF3C7E12802DB04800]",
            @">>>  Section start 2026/01/07 21:14:38.163",
            @"     inf:      Provider: NVIDIA",
            @"     inf:      Driver Version: 02/15/2025,32.0.15.7247",
            @"     dvi:           {Install Device - PCI\VEN_10DE&DEV_2B85&SUBSYS_0000&REV_00\DF3C7E12802DB04800} 21:15:01.000",
            @"<<<  Section end 2026/01/07 21:15:02.570",
        ]);

        var result = EventLogParser.ParseSetupApiLog(_tempFile);

        Assert.Single(result);
        Assert.Equal(new DateTime(2026, 1, 7, 21, 14, 38), result[0].Timestamp);
        Assert.Equal("32.0.15.7247", result[0].DriverVersion);
    }

    [Fact]
    public void ParseSetupApiLog_NonNvidiaVersionAfterSectionEnd_DoesNotLatchOntoPriorNvidiaInstall()
    {
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2026/01/03 14:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2B85&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 14:05:00.000",
            @"<<<  Section end 2026/01/03 14:05:01.000",
            @">>>  [SetupUninstallOEMInf - oem55.inf]",
            @">>>  Section start 2026/01/03 14:16:12.448",
            @"     inf:                Provider       = SteelSeries ApS",
            @"     inf:                Driver Version = 03/05/2025,1.0.15.0",
        ]);

        var result = EventLogParser.ParseSetupApiLog(_tempFile);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseSetupApiLog_NoMismatches_DoesNotFireCanary()
    {
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2025/03/15 10:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 10:05:00.000",
            @"inf:   Driver Version = 6.14,32.0.15.8129"
        ]);
        var health = new CollectorHealth();

        EventLogParser.ParseSetupApiLog(_tempFile, cutoff: null, log: null, health: health);

        Assert.Empty(health.Notices);
    }

    [Fact]
    public void ParseSetupApiLog_Cancelled_ThrowsOperationCanceledException()
    {
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2025/03/15 10:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 10:05:00.000",
            @"inf:   Driver Version = 6.14,32.0.15.8129"
        ]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            EventLogParser.ParseSetupApiLog(_tempFile, ct: cts.Token));
    }

    [Fact]
    public void EnumerateSetupApiDevLogs_IncludesCurrentAndRotatedDevLogsOnly()
    {
        var windowsDir = CreateFakeWindowsDir();
        var infDir = Path.Combine(windowsDir, "INF");
        var current = Path.Combine(infDir, "setupapi.dev.log");
        var olderArchive = Path.Combine(infDir, "setupapi.dev.20251226_234358.log");
        var newerArchive = Path.Combine(infDir, "setupapi.dev.20260109_212303.log");
        File.WriteAllText(current, "");
        File.WriteAllText(olderArchive, "");
        File.WriteAllText(newerArchive, "");
        File.WriteAllText(Path.Combine(infDir, "setupapi.offline.log"), "");
        File.WriteAllText(Path.Combine(infDir, "setupapi.setup.log"), "");

        var result = EventLogParser.EnumerateSetupApiDevLogs(windowsDir);

        Assert.Equal([current, olderArchive, newerArchive], result);
    }

    [Fact]
    public void ParseSetupApiDevLogs_CurrentAndRotatedLogs_AreParsed()
    {
        var windowsDir = CreateFakeWindowsDir();
        var infDir = Path.Combine(windowsDir, "INF");
        File.WriteAllLines(Path.Combine(infDir, "setupapi.dev.log"), [
            @"Boot Session: 2026/01/15 19:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2B85&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 19:11:24.000",
            @"inf:   Driver Version = 6.14,32.0.15.7700",
        ]);
        File.WriteAllLines(Path.Combine(infDir, "setupapi.dev.20260109_212303.log"), [
            @"Boot Session: 2026/01/07 20:59:35",
            @"Install Device - PCI\VEN_10DE&DEV_2B85&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 21:15:01.000",
            @"inf:   Driver Version = 6.14,32.0.15.7247",
        ]);

        var result = EventLogParser.ParseSetupApiDevLogs(
                windowsDir,
                cutoff: new DateTime(2026, 1, 1))
            .OrderBy(e => e.Timestamp)
            .ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(new DateTime(2026, 1, 7, 21, 15, 1), result[0].Timestamp);
        Assert.Equal("32.0.15.7247", result[0].DriverVersion);
        Assert.Equal(new DateTime(2026, 1, 15, 19, 11, 24), result[1].Timestamp);
        Assert.Equal("32.0.15.7700", result[1].DriverVersion);
    }

    [Fact]
    public void ParseSetupApiDevLogs_WhenCurrentAndArchiveOverlap_DedupKeepsOneInstall()
    {
        var windowsDir = CreateFakeWindowsDir();
        var infDir = Path.Combine(windowsDir, "INF");
        string[] duplicateInstall =
        [
            @"Boot Session: 2026/01/07 20:59:35",
            @"Install Device - PCI\VEN_10DE&DEV_2B85&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 21:15:01.000",
            @"inf:   Driver Version = 6.14,32.0.15.7247",
        ];
        File.WriteAllLines(Path.Combine(infDir, "setupapi.dev.log"), duplicateInstall);
        File.WriteAllLines(Path.Combine(infDir, "setupapi.dev.20260109_212303.log"), duplicateInstall);

        var parsed = EventLogParser.ParseSetupApiDevLogs(windowsDir);
        var deduped = EventLogParser.DeduplicateDriverInstalls(parsed);

        Assert.Equal(2, parsed.Count);
        Assert.Single(deduped);
        Assert.Equal("32.0.15.7247", deduped[0].DriverVersion);
    }
}

public class EventLogParserTests
{
    private static readonly DateTime Ts = new(2025, 1, 15, 10, 30, 0);

    [Fact]
    public void ClassifyGpuError_SmCoordinates_Extracted()
    {
        var result = EventLogParser.ClassifyGpuError(
            Ts, 13, "Graphics SM Warp Exception on GPC 3, TPC 1, SM 0: Illegal Instruction Encoding");
        Assert.Equal(3, result.Gpc);
        Assert.Equal(1, result.Tpc);
        Assert.Equal(0, result.Sm);
        Assert.Equal("Illegal Instruction Encoding", result.ErrorType);
        Assert.Equal(13, result.EventId);
    }

    [Fact]
    public void ClassifyGpuError_TdrEvent153_ClassifiedCorrectly()
    {
        var result = EventLogParser.ClassifyGpuError(
            Ts, 153, "Display driver nvlddmkm stopped responding");
        Assert.Equal("TDR (Timeout Detection and Recovery)", result.ErrorType);
        Assert.Null(result.Gpc);
    }

    [Fact]
    public void ClassifyGpuError_EccError_ClassifiedCorrectly()
    {
        var result = EventLogParser.ClassifyGpuError(
            Ts, 14, "An uncorrectable ECC error was detected");
        Assert.Equal("Uncorrectable ECC Error", result.ErrorType);
    }

    [Fact]
    public void ClassifyGpuError_SramError_ClassifiedCorrectly()
    {
        var result = EventLogParser.ClassifyGpuError(
            Ts, 14, "Uncorrectable SRAM Error detected");
        Assert.Equal("Uncorrectable SRAM Error", result.ErrorType);
    }

    [Fact]
    public void ClassifyGpuError_CmdreError_ClassifiedCorrectly()
    {
        var result = EventLogParser.ClassifyGpuError(Ts, 14, "CMDre 0A 1B 2C");
        Assert.Equal("Command Re-execution Error (CMDre)", result.ErrorType);
    }

    [Fact]
    public void ClassifyGpuError_EsrError_ClassifiedCorrectly()
    {
        var result = EventLogParser.ClassifyGpuError(
            Ts, 13, "Graphics Exception: ESR 0x00000040");
        Assert.Equal("Graphics Exception (ESR)", result.ErrorType);
    }

    [Fact]
    public void ClassifyGpuError_NoSmCoords_CoordsAreNull()
    {
        var result = EventLogParser.ClassifyGpuError(
            Ts, 14, "PCIE error Uncorrectable something");
        Assert.Null(result.Gpc);
        Assert.Null(result.Tpc);
        Assert.Null(result.Sm);
    }

    [Theory]
    [InlineData("Illegal Instruction Encoding")]
    [InlineData("Multiple Warp Errors")]
    [InlineData("Illegal Global Access")]
    [InlineData("Page Fault")]
    [InlineData("Misaligned Address")]
    [InlineData("Misaligned PC")]
    public void ClassifyGpuError_AllExceptionTypes_Recognized(string errorType)
    {
        var result = EventLogParser.ClassifyGpuError(
            Ts, 13, $"Graphics SM Warp Exception on GPC 0, TPC 0, SM 0: {errorType}");
        Assert.Equal(errorType, result.ErrorType);
    }

    [Fact]
    public void WarnIfClassifierDrift_AllEventsUnclassified_EmitsWarning()
    {
        var errors = new List<NvlddmkmError>
        {
            new(Ts, 13, "Event 13", null, null, null, null),
            new(Ts, 14, "Event 14", null, null, null, null),
        };
        var logs = new List<string>();
        EventLogParser.WarnIfClassifierDrift(errors, logs.Add);
        Assert.Contains(logs, l => l.Contains("none matched the known payload shapes"));
    }

    [Fact]
    public void WarnIfClassifierDrift_AnyEventHasCoords_NoWarning()
    {
        var errors = new List<NvlddmkmError>
        {
            new(Ts, 13, "Event 13", 1, 0, 0, null),
            new(Ts, 14, "Event 14", null, null, null, null),
        };
        var logs = new List<string>();
        EventLogParser.WarnIfClassifierDrift(errors, logs.Add);
        Assert.Empty(logs);
    }

    [Fact]
    public void WarnIfClassifierDrift_AnyEventHasErrorType_NoWarning()
    {
        var errors = new List<NvlddmkmError>
        {
            new(Ts, 153, "TDR", null, null, null, "TDR (Timeout Detection and Recovery)"),
            new(Ts, 13, "Event 13", null, null, null, null),
        };
        var logs = new List<string>();
        EventLogParser.WarnIfClassifierDrift(errors, logs.Add);
        Assert.Empty(logs);
    }

    [Fact]
    public void WarnIfClassifierDrift_EmptyList_NoWarning()
    {
        var logs = new List<string>();
        EventLogParser.WarnIfClassifierDrift(new List<NvlddmkmError>(), logs.Add);
        Assert.Empty(logs);
    }

    [Fact]
    public void WarnIfClassifierDrift_AllUnclassifiedWithHealth_RecordsCanary()
    {
        var errors = new List<NvlddmkmError>
        {
            new(Ts, 13, "Event 13", null, null, null, null),
            new(Ts, 14, "Event 14", null, null, null, null),
        };
        var health = new CollectorHealth();

        EventLogParser.WarnIfClassifierDrift(errors, log: null, health);

        Assert.Single(health.Notices);
        Assert.Equal(CollectorNoticeKind.Canary, health.Notices[0].Kind);
        Assert.Equal("nvlddmkm classifier", health.Notices[0].Source);
    }

    [Fact]
    public void WarnIfClassifierDrift_HasClassifiedEvents_DoesNotRecordCanary()
    {
        var errors = new List<NvlddmkmError>
        {
            new(Ts, 13, "Event 13", 1, 0, 0, null),
        };
        var health = new CollectorHealth();

        EventLogParser.WarnIfClassifierDrift(errors, log: null, health);

        Assert.Empty(health.Notices);
    }

    [Fact]
    public void WarnIfClassifierDrift_MajorityUnclassifiedAboveFloor_EmitsPartialCanary()
    {
        var errors = new List<NvlddmkmError>
        {
            new(Ts, 13, "Event 13", 1, 0, 0, "Page Fault"),
            new(Ts, 13, "Event 13", null, null, null, null),
            new(Ts, 13, "Event 13", null, null, null, null),
            new(Ts, 13, "Event 13", null, null, null, null),
            new(Ts, 13, "Event 13", null, null, null, null),
            new(Ts, 13, "Event 13", null, null, null, null),
        };
        var health = new CollectorHealth();
        var logs = new List<string>();

        EventLogParser.WarnIfClassifierDrift(errors, logs.Add, health);

        Assert.Single(health.Notices);
        Assert.Equal(CollectorNoticeKind.Canary, health.Notices[0].Kind);
        Assert.Contains("5 of 6", health.Notices[0].Message);
        Assert.Contains("partial driver-side format change", health.Notices[0].Message);
        Assert.Contains(logs, l => l.Contains("5 of 6"));
    }

    [Fact]
    public void WarnIfClassifierDrift_MajorityUnclassifiedBelowFloor_NoCanary()
    {
        var errors = new List<NvlddmkmError>
        {
            new(Ts, 13, "Event 13", 1, 0, 0, "Page Fault"),
            new(Ts, 13, "Event 13", null, null, null, null),
            new(Ts, 13, "Event 13", null, null, null, null),
            new(Ts, 13, "Event 13", null, null, null, null),
        };
        var health = new CollectorHealth();

        EventLogParser.WarnIfClassifierDrift(errors, log: null, health);

        Assert.Empty(health.Notices);
    }

    [Fact]
    public void WarnIfClassifierDrift_MinorityUnclassifiedAboveFloor_NoCanary()
    {
        var errors = new List<NvlddmkmError>();
        for (int i = 0; i < 20; i++)
            errors.Add(new(Ts, 13, "Event 13", 1, 0, 0, "Page Fault"));
        for (int i = 0; i < 5; i++)
            errors.Add(new(Ts, 14, "Event 14", null, null, null, null));
        var health = new CollectorHealth();

        EventLogParser.WarnIfClassifierDrift(errors, log: null, health);

        Assert.Empty(health.Notices);
    }

    [Fact]
    public void WarnIfAppEventShapeDrift_NoFailures_DoesNotFire()
    {
        var logs = new List<string>();
        var health = new CollectorHealth();

        EventLogParser.WarnIfAppEventShapeDrift("Application Error 1000", total: 10, shapeFails: 0, expectedMinProps: 4, logs.Add, health);

        Assert.Empty(logs);
        Assert.Empty(health.Notices);
    }

    [Fact]
    public void WarnIfAppEventShapeDrift_EmptyTotal_DoesNotFire()
    {
        var health = new CollectorHealth();
        EventLogParser.WarnIfAppEventShapeDrift("Application Hang 1002", total: 0, shapeFails: 0, expectedMinProps: 1, log: null, health);
        Assert.Empty(health.Notices);
    }

    [Fact]
    public void WarnIfAppEventShapeDrift_AllFailed_FiresCanary()
    {
        var logs = new List<string>();
        var health = new CollectorHealth();

        EventLogParser.WarnIfAppEventShapeDrift("Application Error 1000", total: 7, shapeFails: 7, expectedMinProps: 4, logs.Add, health);

        Assert.Single(health.Notices);
        Assert.Equal(CollectorNoticeKind.Canary, health.Notices[0].Kind);
        Assert.Equal("Application Error 1000 payload", health.Notices[0].Source);
        Assert.Contains("none carried the expected", health.Notices[0].Message);
        Assert.Contains(logs, l => l.Contains("schema may have changed"));
    }

    [Fact]
    public void WarnIfAppEventShapeDrift_MajorityFailedAboveFloor_FiresPartialCanary()
    {
        var health = new CollectorHealth();

        EventLogParser.WarnIfAppEventShapeDrift("Application Error 1000", total: 10, shapeFails: 6, expectedMinProps: 4, log: null, health);

        Assert.Single(health.Notices);
        Assert.Contains("6 of 10", health.Notices[0].Message);
        Assert.Contains("render as '?'", health.Notices[0].Message);
    }

    [Fact]
    public void WarnIfAppEventShapeDrift_MajorityFailedBelowFloor_NoCanary()
    {
        var health = new CollectorHealth();

        EventLogParser.WarnIfAppEventShapeDrift("Application Error 1000", total: 6, shapeFails: 4, expectedMinProps: 4, log: null, health);

        Assert.Empty(health.Notices);
    }

    [Fact]
    public void WarnIfAppEventShapeDrift_MinorityFailed_NoCanary()
    {
        var health = new CollectorHealth();

        EventLogParser.WarnIfAppEventShapeDrift("Application Error 1000", total: 100, shapeFails: 10, expectedMinProps: 4, log: null, health);

        Assert.Empty(health.Notices);
    }

    [Fact]
    public void WarnIfDriverInstallSchemeDrift_NoMismatches_DoesNotFire()
    {
        var health = new CollectorHealth();
        EventLogParser.WarnIfDriverInstallSchemeDrift("Kernel-PnP driver install", mismatches: 0, matched: 4, log: null, health);
        Assert.Empty(health.Notices);
    }

    [Fact]
    public void WarnIfDriverInstallSchemeDrift_MismatchesWithSomeMatches_FiresPartialCanary()
    {
        var logs = new List<string>();
        var health = new CollectorHealth();

        EventLogParser.WarnIfDriverInstallSchemeDrift("Kernel-PnP driver install", mismatches: 3, matched: 2, logs.Add, health);

        Assert.Single(health.Notices);
        Assert.Equal(CollectorNoticeKind.Canary, health.Notices[0].Kind);
        Assert.Equal("Kernel-PnP driver install version scheme", health.Notices[0].Source);
        Assert.Contains("2 matched and were parsed", health.Notices[0].Message);
        Assert.Contains("affects only Driver Install History", health.Notices[0].Message);
        Assert.Contains(logs, l => l.Contains("Driver Install History"));
    }

    [Fact]
    public void WarnIfDriverInstallSchemeDrift_MismatchesWithNoMatches_FiresEmptyHistoryCanary()
    {
        var health = new CollectorHealth();

        EventLogParser.WarnIfDriverInstallSchemeDrift("DeviceSetupManager driver install", mismatches: 3, matched: 0, log: null, health);

        Assert.Single(health.Notices);
        Assert.Contains("Driver Install History", health.Notices[0].Message);
        Assert.Contains("may be empty or incomplete", health.Notices[0].Message);
    }

    [Fact]
    public void MapDriverInstallMessage_VendorMatchWithoutVersion_DoesNotCountSchemeMismatch()
    {
        var mismatches = 0;

        var result = EventLogParser.MapDriverInstallMessage(
            Ts,
            "Device install requested for NVIDIA Display adapter nvlddmkm",
            "nvlddmkm|NVIDIA",
            () => mismatches++);

        Assert.Null(result);
        Assert.Equal(0, mismatches);
    }

    [Fact]
    public void MapDriverInstallMessage_VersionBearingMessageWithoutParseableWindowsVersion_CountsSchemeMismatch()
    {
        var mismatches = 0;

        var result = EventLogParser.MapDriverInstallMessage(
            Ts,
            "Software NVIDIA CoInstaller Display.Driver was not newer, Version: '596.21'. Current Version: '596.21'.",
            "NVIDIA|display",
            () => mismatches++);

        Assert.Null(result);
        Assert.Equal(1, mismatches);
    }

    [Fact]
    public void MapDeviceSetupManagerDriverInstallMessage_NonVersionEventId_IsIgnored()
    {
        var mismatches = 0;

        var result = EventLogParser.MapDeviceSetupManagerDriverInstallMessage(
            Ts,
            eventId: 160,
            "Software NVIDIA CoInstaller Display.Driver was installed for device 'PCI\\VEN_10DE&DEV_2B85' in 210 ms.",
            () => mismatches++);

        Assert.Null(result);
        Assert.Equal(0, mismatches);
    }

    [Fact]
    public void MapDeviceSetupManagerDriverInstallMessage_Id161VersionEntry_IsParsed()
    {
        var result = EventLogParser.MapDeviceSetupManagerDriverInstallMessage(
            Ts,
            eventId: 161,
            "Software NVIDIA CoInstaller Display.Driver was not newer, Version: '32.0.15.9186'. Current Version: '32.0.15.9597'.");

        Assert.NotNull(result);
        Assert.Equal("32.0.15.9186", result.DriverVersion);
    }

    [Fact]
    public void EnumerateMinidumpFiles_NonexistentDir_DoesNotRecordFailure()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"flare_dump_missing_{Guid.NewGuid():N}");
        var health = new CollectorHealth();

        var result = EventLogParser.EnumerateMinidumpFiles(missing, DateTime.MinValue, log: null, health);

        Assert.Empty(result);
        Assert.Empty(health.Notices);
    }

    [Fact]
    public void ReadEvents_Failure_InvokesFailureCallback()
    {
        var logs = new List<string>();
        var failures = new List<string>();

        var result = EventLogParser.ReadEvents<NvlddmkmError>(
            "FLARE-Definitely-Missing-Event-Log",
            "*",
            1,
            logs.Add,
            "Warning: test event read failed",
            CancellationToken.None,
            _ => null,
            onFailure: ex => failures.Add(ex.Message));

        Assert.Empty(result);
        Assert.Single(failures);
        Assert.Contains(logs, l => l.Contains("Warning: test event read failed"));
    }

    [Fact]
    public void CreateEventLogQuery_ReadsNewestFirst()
    {
        var query = EventLogParser.CreateEventLogQuery("System", "*");

        Assert.True(query.ReverseDirection);
    }
}

public class DriverInstallDedupTests
{
    [Fact]
    public void DeduplicateDriverInstalls_SameVersionAcrossHourBoundary_Collapses()
    {
        var events = new List<EventLogParser.DriverInstallEvent>
        {
            new(new DateTime(2025, 1, 1, 10, 59, 30), "32.0.15.8129", "a"),
            new(new DateTime(2025, 1, 1, 11, 00, 30), "32.0.15.8129", "b"),
        };

        var result = EventLogParser.DeduplicateDriverInstalls(events);

        Assert.Single(result);
        Assert.Equal("a", result[0].Description);
    }

    [Fact]
    public void DeduplicateDriverInstalls_DifferentVersions_CloseTogether_AreRetained()
    {
        var events = new List<EventLogParser.DriverInstallEvent>
        {
            new(new DateTime(2025, 1, 1, 10, 59, 30), "32.0.15.8129", "a"),
            new(new DateTime(2025, 1, 1, 11, 00, 30), "32.0.15.9999", "b"),
        };

        var result = EventLogParser.DeduplicateDriverInstalls(events);

        Assert.Equal(2, result.Count);
    }
}

public class NormalizeEventPropertiesTests
{
    // The live path is:  EventRecord.Properties.Select(p => p.Value)  ->  NormalizeEventProperties
    //                    -> ClassifyGpuError. Tests exercise the string-shaping half and
    //                    then re-classify, so a Properties-join regression would show up
    //                    as a classification regression.

    [Fact]
    public void Normalize_CollapsesInternalWhitespaceAndTrims()
    {
        var values = new object?[]
        {
            "   Graphics SM   Warp Exception   ",
            "GPC 3,\tTPC 1,\r\nSM 0"
        };

        var result = EventLogParser.NormalizeEventProperties(values);

        Assert.Equal("Graphics SM Warp Exception ||| GPC 3, TPC 1, SM 0", result);
    }

    [Fact]
    public void Normalize_NullAndMissingValues_BecomeEmptyStrings()
    {
        var values = new object?[] { null, "keep", null };

        var result = EventLogParser.NormalizeEventProperties(values);

        Assert.Equal(" ||| keep ||| ", result);
    }

    [Fact]
    public void Normalize_NonStringValue_UsesToString()
    {
        // EventRecord.Properties can hand back unboxed primitives. ToString() should
        // be enough — ensure the helper doesn't assume string values.
        var values = new object?[] { 13, 0xBADu, "tail" };

        var result = EventLogParser.NormalizeEventProperties(values);

        Assert.Equal("13 ||| 2989 ||| tail", result);
    }

    [Fact]
    public void Normalize_EmptyEnumerable_ReturnsEmpty()
    {
        Assert.Equal("", EventLogParser.NormalizeEventProperties(Array.Empty<object?>()));
    }

    [Fact]
    public void Normalize_ThenClassify_SmCoordinates_ExtractedFromRealPayloadShape()
    {
        // Mirrors the nvlddmkm EventData shape seen in practice: separate Data nodes,
        // the interesting text arriving from multiple properties that only become a
        // classifiable phrase after the join.
        var values = new object?[]
        {
            "  nvlddmkm ",
            "Graphics SM Warp Exception on\nGPC 3, TPC 1, SM 0: Illegal Instruction Encoding",
            "\t\t"
        };

        var joined = EventLogParser.NormalizeEventProperties(values);
        var result = EventLogParser.ClassifyGpuError(new DateTime(2026, 1, 1), 13, joined);

        Assert.Equal(3, result.Gpc);
        Assert.Equal(1, result.Tpc);
        Assert.Equal(0, result.Sm);
        Assert.Equal("Illegal Instruction Encoding", result.ErrorType);
    }

    [Fact]
    public void Normalize_ThenClassify_EccPayload_Classified()
    {
        var values = new object?[]
        {
            null,
            "An uncorrectable ECC error was detected on\r\n   GPU",
        };

        var joined = EventLogParser.NormalizeEventProperties(values);
        var result = EventLogParser.ClassifyGpuError(new DateTime(2026, 1, 1), 14, joined);

        Assert.Equal("Uncorrectable ECC Error", result.ErrorType);
    }

    [Fact]
    public void Normalize_ThenClassify_TdrEvent153_Classified()
    {
        var values = new object?[] { "Display driver nvlddmkm stopped responding" };

        var joined = EventLogParser.NormalizeEventProperties(values);
        var result = EventLogParser.ClassifyGpuError(new DateTime(2026, 1, 1), 153, joined);

        Assert.Equal("TDR (Timeout Detection and Recovery)", result.ErrorType);
        Assert.Null(result.Gpc);
    }
}
