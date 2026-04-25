using FLARE.Core;
using FLARE.UI.Models;
using FLARE.UI.Services;
using FLARE.UI.ViewModels;

namespace FLARE.Tests;

public class MainViewModelTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Stored { get; set; } = new();
        public int SaveCount { get; private set; }
        public string? LastLoadWarning { get; set; }
        public string? LastSaveWarning { get; set; }
        public string? NextSaveWarning { get; set; }

        public AppSettings LoadSettings() => new()
        {
            MaxDays = Stored.MaxDays,
            MaxEvents = Stored.MaxEvents,
            SortDescending = Stored.SortDescending,
            RedactIdentifiers = Stored.RedactIdentifiers,
        };

        public void SaveSettings(AppSettings settings)
        {
            SaveCount++;
            LastSaveWarning = NextSaveWarning;
            if (LastSaveWarning == null)
                Stored = settings;
        }
    }

    private static MainViewModel NewVm(
        FakeSettingsService? settings = null,
        IStartupNotices? startup = null,
        Func<FlareOptions, Action<string>?, CancellationToken, FlareResult>? runFlare = null,
        Func<(string Text, string Tooltip)>? resolveCdbStatus = null) =>
        new(settings ?? new FakeSettingsService(),
            startup ?? new StartupNotices(),
            runFlare ?? ((options, log, ct) => FlareRunner.Run(options, log, ct)),
            resolveCdbStatus ?? (() => ("cdb.exe not found", "test")));

    [Fact]
    public void Ctor_LoadsSettingsIntoViewModel()
    {
        var settings = new FakeSettingsService
        {
            Stored = new AppSettings
            {
                MaxDays = 45,
                MaxEvents = 2500,
                SortDescending = false,
                RedactIdentifiers = true,
            }
        };
        var vm = NewVm(settings: settings);

        Assert.Equal(45, vm.MaxDays);
        Assert.Equal(2500, vm.MaxEvents);
        Assert.False(vm.SortDescending);
        Assert.True(vm.RedactIdentifiers);
    }

    [Fact]
    public void Ctor_LoadedSettings_DoNotQueueSaveOrRedetectCdbMidHydration()
    {
        var settings = new FakeSettingsService
        {
            Stored = new AppSettings
            {
                MaxDays = 45,
                MaxEvents = 2500,
                SortDescending = false,
                RedactIdentifiers = false,
            }
        };
        var cdbResolveCount = 0;
        var vm = NewVm(
            settings: settings,
            resolveCdbStatus: () =>
            {
                cdbResolveCount++;
                return ("cdb.exe detected", "auto-detected");
            });

        vm.FlushPendingSave();

        Assert.Equal(0, settings.SaveCount);
        Assert.Equal(1, cdbResolveCount);
    }

    [Fact]
    public void Ctor_EmptySettings_UsesDefaults()
    {
        var settings = new FakeSettingsService();
        var vm = NewVm(settings: settings);

        Assert.Equal(365, vm.MaxDays);
        Assert.Equal(5000, vm.MaxEvents);
        Assert.True(vm.SortDescending);
        Assert.True(vm.RedactIdentifiers);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Equal(Path.Combine(localAppData, "FLARE", "Reports"), vm.OutputPath);
    }

    [Fact]
    public async Task Run_WithDeepAnalyzeOff_ForwardsDeepAnalyzeFalse()
    {
        FlareOptions? seen = null;
        var vm = NewVm(runFlare: (options, _, _) =>
        {
            seen = options;
            return new FlareResult { SavedPath = "report.txt" };
        });

        vm.DeepAnalyze = false;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.NotNull(seen);
        Assert.False(seen.DeepAnalyze);
    }

    [Fact]
    public async Task DeepAnalyzeOn_ForwardsDeepAnalyzeTrue()
    {
        FlareOptions? seen = null;
        var vm = NewVm(
            runFlare: (options, _, _) =>
            {
                seen = options;
                return new FlareResult { SavedPath = "report.txt" };
            },
            resolveCdbStatus: () => ("cdb.exe detected", "test"));

        vm.DeepAnalyze = true;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.NotNull(seen);
        Assert.True(seen.DeepAnalyze);
    }

    [Fact]
    public void FlushPendingSave_NothingPending_IsNoOp()
    {
        var settings = new FakeSettingsService();
        var vm = NewVm(settings: settings);

        var before = settings.SaveCount;
        vm.FlushPendingSave();
        Assert.Equal(before, settings.SaveCount);
    }

    [Fact]
    public void FlushPendingSave_AfterPropertyChange_WritesImmediately()
    {
        var settings = new FakeSettingsService();
        var vm = NewVm(settings: settings);

        vm.MaxDays = 99;
        var beforeFlush = settings.SaveCount;

        vm.FlushPendingSave();

        Assert.True(settings.SaveCount > beforeFlush,
            "FlushPendingSave should persist the pending change immediately");
        Assert.Equal(99, settings.Stored.MaxDays);
    }

    [Fact]
    public void Ctor_SettingsServiceReportsLoadWarning_SurfacesInBottomStatus()
    {
        var settings = new FakeSettingsService
        {
            LastLoadWarning = "Settings file was unreadable (test); reset to defaults."
        };
        var vm = NewVm(settings: settings);

        Assert.Equal(settings.LastLoadWarning, vm.BottomStatusText);
    }

    [Fact]
    public async Task Run_WithInvalidNumericInputs_DoesNotStartPipeline()
    {
        var vm = NewVm();
        vm.MaxDays = 0;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.False(vm.IsRunning);
        Assert.Equal("Check Max Days / Max Events", vm.StatusText);
        Assert.Equal("Max Days and Max Events must be at least 1.", vm.BottomStatusText);
        Assert.Equal("", vm.LogOutput);
    }

    [Fact]
    public async Task Run_CompletionLog_DoesNotAppendFullReportBody()
    {
        var reportPath = Path.Combine(Path.GetTempPath(), $"flare_report_{Guid.NewGuid():N}.txt");
        var settings = new FakeSettingsService { Stored = new AppSettings { RedactIdentifiers = false } };
        var vm = NewVm(settings: settings, runFlare: (_, log, _) =>
        {
            log?.Invoke("Collecting GPU information...");
            return new FlareResult
            {
                Report = "GPU Error Analysis Report\nfull saved report body",
                SavedPath = reportPath,
                Health = new CollectorHealth(),
            };
        });

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Contains("Collecting GPU information...", vm.LogOutput);
        Assert.Contains("Analysis complete.", vm.LogOutput);
        Assert.DoesNotContain("GPU Error Analysis Report", vm.LogOutput);
        Assert.DoesNotContain("full saved report body", vm.LogOutput);
        Assert.Equal($"Report saved to: {reportPath}", vm.BottomStatusText);
    }

    [Fact]
    public async Task Run_RedactOn_BottomStatusScrubsSavedPath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var reportPath = Path.Combine(userProfile, "AppData", "Local", "FLARE", "Reports",
            $"flare_report_{Guid.NewGuid():N}.txt");
        var vm = NewVm(runFlare: (_, _, _) => new FlareResult
        {
            SavedPath = reportPath,
            Health = new CollectorHealth(),
        });

        await vm.RunCommand.ExecuteAsync(null);

        Assert.True(vm.RedactIdentifiers);
        Assert.DoesNotContain(userProfile, vm.BottomStatusText);
        Assert.Contains("%USERPROFILE%", vm.BottomStatusText);
    }

    [Fact]
    public void OutputPathDisplay_RedactOn_DoesNotLeakUserProfile()
    {
        var settings = new FakeSettingsService { Stored = new AppSettings { RedactIdentifiers = true } };
        var vm = NewVm(settings: settings);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Contains(userProfile, vm.OutputPath);
        Assert.DoesNotContain(userProfile, vm.OutputPathDisplay);
    }

    [Fact]
    public void OutputPathDisplay_TogglingRedactionRefreshes()
    {
        var settings = new FakeSettingsService { Stored = new AppSettings { RedactIdentifiers = false } };
        var vm = NewVm(settings: settings);

        var raw = vm.OutputPath;
        Assert.Equal(raw, vm.OutputPathDisplay);

        vm.RedactIdentifiers = true;
        Assert.NotEqual(raw, vm.OutputPathDisplay);

        vm.RedactIdentifiers = false;
        Assert.Equal(raw, vm.OutputPathDisplay);
    }

    [Fact]
    public void CdbStatusTooltip_RedactOn_ScrubsResolvedPath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var resolved = Path.Combine(userProfile, "AppData", "Local", "Microsoft", "WindowsApps", "cdbx64.exe");
        var settings = new FakeSettingsService { Stored = new AppSettings { RedactIdentifiers = true } };
        var vm = NewVm(
            settings: settings,
            resolveCdbStatus: () => MainViewModel.GetCdbStatus(resolved));

        Assert.DoesNotContain(userProfile, vm.CdbStatusTooltip);
        Assert.Contains("%USERPROFILE%", vm.CdbStatusTooltip);
    }

    [Fact]
    public async Task Run_Exception_RedactOn_LogPaneScrubsExceptionPath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var leakingPath = Path.Combine(userProfile, "AppData", "Local", "FLARE", "settings.json");
        var vm = NewVm(runFlare: (_, _, _) =>
            throw new InvalidOperationException($"Could not open file '{leakingPath}'."));

        await vm.RunCommand.ExecuteAsync(null);

        Assert.True(vm.RedactIdentifiers);
        Assert.DoesNotContain(userProfile, vm.LogOutput);
        Assert.Contains("%USERPROFILE%", vm.LogOutput);
        Assert.DoesNotContain(userProfile, vm.BottomStatusText);
    }

    [Fact]
    public void StartupNotice_RedactOn_ScrubsBeforeShowingInBottomStatus()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var leakingPath = Path.Combine(userProfile, "AppData", "Local", "FLARE", "CdbCache");
        var startup = new StartupNotices();
        startup.Add($"FLARE migration: cdb cache move failed ({leakingPath}): Access denied.");
        var settings = new FakeSettingsService { Stored = new AppSettings { RedactIdentifiers = true } };

        var vm = NewVm(settings: settings, startup: startup);

        Assert.DoesNotContain(userProfile, vm.BottomStatusText);
        Assert.Contains("%USERPROFILE%", vm.BottomStatusText);
    }

    [Fact]
    public async Task Run_LimitedEventLogHistory_AppendsSummaryAndSetsCompactStatus()
    {
        var reportPath = Path.Combine(Path.GetTempPath(), $"flare_report_{Guid.NewGuid():N}.txt");
        var oldestSystem = DateTime.Now.AddDays(-10).Date.AddHours(13).AddMinutes(40).AddSeconds(33);
        var oldestGpu = DateTime.Now.AddDays(-5).Date.AddHours(9).AddMinutes(50).AddSeconds(23);
        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation { RequestedMaxDays = 365 },
            SystemEventLog = new EventLogRetentionInfo(
                "System",
                "Circular",
                MaximumSizeInBytes: 268435456,
                FileSizeBytes: null,
                RecordCount: null,
                OldestRecordNumber: null,
                OldestRecordTimestamp: oldestSystem,
                OldestRelevantEventTimestamp: oldestGpu,
                OldestRelevantEventDescription: "nvlddmkm 13/14/153"),
        };
        var vm = NewVm(runFlare: (_, _, _) => new FlareResult
        {
            Errors = [new NvlddmkmError(DateTime.Now, 13, "m", 3, 1, 0, "Page Fault")],
            HasSmErrors = true,
            SavedPath = reportPath,
            Health = health,
        });

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal("nvlddmkm errors found (SM coords; limited history)", vm.StatusText);
        Assert.Contains("System Event Log history is limited:", vm.LogOutput);
        Assert.Contains($"retained since {oldestSystem:yyyy-MM-dd HH:mm:ss}", vm.LogOutput);
        Assert.Contains("mode Circular, max 256.0 MiB", vm.LogOutput);
        Assert.Contains($"oldest nvlddmkm 13/14/153 record is {oldestGpu:yyyy-MM-dd HH:mm:ss}", vm.LogOutput);
        Assert.Contains("Earlier requested days are unavailable.", vm.LogOutput);
        Assert.Contains($"limited:{Environment.NewLine}  retained since", vm.LogOutput);
    }

    [Fact]
    public void Ctor_StartupNoticePresent_SurfacesInBottomStatus()
    {
        var startup = new StartupNotices();
        startup.Add("FLARE migration: minidump move failed (test): Access denied.");

        var vm = NewVm(startup: startup);

        Assert.Equal(startup.FirstMessage, vm.BottomStatusText);
    }

    [Fact]
    public void Ctor_StartupNoticeAndSettingsWarning_StartupNoticeWins()
    {
        var settings = new FakeSettingsService
        {
            LastLoadWarning = "Settings file was unreadable (test); reset to defaults."
        };
        var startup = new StartupNotices();
        startup.Add("FLARE migration: cdb cache move failed (test): Access denied.");

        var vm = NewVm(settings: settings, startup: startup);

        Assert.Equal(startup.FirstMessage, vm.BottomStatusText);
    }

    [Fact]
    public void StartupNotices_FirstMessageWins_SecondIsIgnored()
    {
        var notices = new StartupNotices();
        notices.Add("first");
        notices.Add("second");
        Assert.Equal("first", notices.FirstMessage);
    }

    [Fact]
    public void GetCdbStatus_NotFound_StillDescribesBasicDumpParsing()
    {
        var status = MainViewModel.GetCdbStatus(resolvedPath: null);

        Assert.Equal("cdb.exe not found", status.Text);
        Assert.Contains("built-in crash dump parsing", status.Tooltip);
    }

    [Fact]
    public void GetRunStatusText_EventLogFailure_TakesPrecedence()
    {
        var health = new CollectorHealth();
        health.Failure("Event Log: nvlddmkm", "access denied");
        var result = new FlareResult
        {
            Errors = [new NvlddmkmError(DateTime.Now, 13, "m", 1, 0, 0, null)],
            HasSmErrors = true,
            Health = health,
        };

        var status = RunStatusFormatter.GetRunStatusText(result);

        Assert.Equal("Event Log collection failed - see report", status);
    }

    [Fact]
    public void GetRunStatusText_NoErrors_UsesNvlddmkmWording()
    {
        var status = RunStatusFormatter.GetRunStatusText(new FlareResult());

        Assert.Equal("No nvlddmkm errors found", status);
    }

    [Fact]
    public void GetRunStatusText_ErrorsWithoutCoordinates_StatesThat()
    {
        var result = new FlareResult
        {
            Errors = [new NvlddmkmError(DateTime.Now, 153, "m", null, null, null, "TDR")],
            HasSmErrors = false,
        };

        var status = RunStatusFormatter.GetRunStatusText(result);

        Assert.Equal("nvlddmkm errors found (no SM coordinates)", status);
    }

    [Fact]
    public void GetRunStatusText_ErrorsWithCoordinates_StatesThat()
    {
        var result = new FlareResult
        {
            Errors = [new NvlddmkmError(DateTime.Now, 13, "m", 3, 1, 0, null)],
            HasSmErrors = true,
        };

        var status = RunStatusFormatter.GetRunStatusText(result);

        Assert.Equal("nvlddmkm errors found (SM coordinates present)", status);
    }

    [Fact]
    public void GetRunStatusText_LimitedHistory_NoErrors_StatesRetainedScope()
    {
        var now = new DateTime(2026, 4, 25, 12, 0, 0);
        var result = new FlareResult
        {
            Health = new CollectorHealth
            {
                Truncation = new CollectionTruncation { RequestedMaxDays = 365 },
                SystemEventLog = new EventLogRetentionInfo(
                    "System",
                    "Circular",
                    MaximumSizeInBytes: 268435456,
                    FileSizeBytes: null,
                    RecordCount: null,
                    OldestRecordNumber: null,
                    OldestRecordTimestamp: new DateTime(2025, 12, 4, 13, 40, 33),
                    OldestRelevantEventTimestamp: null,
                    OldestRelevantEventDescription: "nvlddmkm 13/14/153"),
            },
        };

        var status = RunStatusFormatter.GetRunStatusText(result, now);

        Assert.Equal("No retained nvlddmkm errors found (limited history)", status);
    }

    [Fact]
    public void GetEventLogRetentionSummary_WhenWindowIsCovered_ReturnsNull()
    {
        var now = new DateTime(2026, 4, 25, 12, 0, 0);
        var result = new FlareResult
        {
            Health = new CollectorHealth
            {
                Truncation = new CollectionTruncation { RequestedMaxDays = 30 },
                SystemEventLog = new EventLogRetentionInfo(
                    "System",
                    "Circular",
                    MaximumSizeInBytes: 268435456,
                    FileSizeBytes: null,
                    RecordCount: null,
                    OldestRecordNumber: null,
                    OldestRecordTimestamp: new DateTime(2025, 12, 4, 13, 40, 33),
                    OldestRelevantEventTimestamp: null,
                    OldestRelevantEventDescription: "nvlddmkm 13/14/153"),
            },
        };

        Assert.Null(RunStatusFormatter.GetEventLogRetentionSummary(result, now));
    }

    [Fact]
    public void GetEventLogRetentionSummary_ApplicationHistoryLimited_IncludesApplicationLog()
    {
        var now = new DateTime(2026, 4, 25, 12, 0, 0);
        var oldestApplication = new DateTime(2026, 2, 10, 7, 15, 0);
        var oldestCrashOrHang = new DateTime(2026, 2, 12, 20, 30, 5);
        var result = new FlareResult
        {
            Health = new CollectorHealth
            {
                Truncation = new CollectionTruncation { RequestedMaxDays = 365 },
                ApplicationEventLog = new EventLogRetentionInfo(
                    "Application",
                    "Circular",
                    MaximumSizeInBytes: 134217728,
                    FileSizeBytes: null,
                    RecordCount: null,
                    OldestRecordNumber: null,
                    OldestRecordTimestamp: oldestApplication,
                    OldestRelevantEventTimestamp: oldestCrashOrHang,
                    OldestRelevantEventDescription: "Application Error 1000 / Application Hang 1002"),
            },
        };

        var summary = RunStatusFormatter.GetEventLogRetentionSummary(result, now);

        Assert.NotNull(summary);
        Assert.Contains("Application Event Log history is limited:", summary);
        Assert.Contains($"retained since {oldestApplication:yyyy-MM-dd HH:mm:ss}", summary);
        Assert.Contains($"oldest Application Error 1000 / Application Hang 1002 record is {oldestCrashOrHang:yyyy-MM-dd HH:mm:ss}", summary);
        Assert.Contains("Earlier requested days are unavailable.", summary);
    }

    [Fact]
    public void FlushPendingSave_SaveFailureSurfacesBottomStatus()
    {
        var settings = new FakeSettingsService
        {
            NextSaveWarning = "Could not save settings (disk full); changes may not persist."
        };
        var vm = NewVm(settings: settings);

        vm.MaxDays = 99;
        vm.FlushPendingSave();

        Assert.Equal(settings.NextSaveWarning, vm.BottomStatusText);
    }

    [Fact]
    public void FlushPendingSave_SubsequentSuccessClearsSaveWarning()
    {
        var settings = new FakeSettingsService
        {
            NextSaveWarning = "Could not save settings (disk full); changes may not persist."
        };
        var vm = NewVm(settings: settings);

        vm.MaxDays = 99;
        vm.FlushPendingSave();
        settings.NextSaveWarning = null;

        vm.MaxEvents = 1234;
        vm.FlushPendingSave();

        Assert.Equal("Ready", vm.BottomStatusText);
    }
}
