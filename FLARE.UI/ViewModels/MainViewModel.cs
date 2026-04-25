using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FLARE.Core;
using FLARE.UI.Services;

namespace FLARE.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IStartupNotices _startupNotices;
    private readonly Func<FlareOptions, Action<string>?, CancellationToken, FlareResult> _runFlare;
    private readonly Func<(string Text, string Tooltip)> _resolveCdbStatus;
    private readonly StringBuilder _logBuilder = new();
    private readonly object _logLock = new();
    private CancellationTokenSource? _cts;
    private bool _suppressSettingsSideEffects;

    private string? _lastSavedPath;
    private string? _activeSettingsSaveWarning;

    private readonly DispatcherTimer _saveDebounce = new()
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };

    private readonly DispatcherTimer _logFlushTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(100),
    };

    public string OutputPath { get; } = FlareStorage.ReportsDir();

    public string OutputPathDisplay => Display(OutputPath);

    private string _cdbStatusTooltipRaw = "";
    public string CdbStatusTooltip => Display(_cdbStatusTooltipRaw);

    [ObservableProperty]
    private int _maxDays = 365;

    [ObservableProperty]
    private int _maxEvents = 5000;

    [ObservableProperty]
    private bool _deepAnalyze;

    [ObservableProperty]
    private bool _sortDescending = true;

    [ObservableProperty]
    private bool _redactIdentifiers = true;

    [ObservableProperty]
    private string _cdbStatusText = "";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _logOutput = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _bottomStatusText = "Ready";

    [ObservableProperty]
    private string _elevationText = "";

    public MainViewModel(ISettingsService settingsService, IStartupNotices startupNotices)
        : this(settingsService, startupNotices,
            (options, log, ct) => FlareRunner.Run(options, log, ct))
    {
    }

    internal MainViewModel(
        ISettingsService settingsService,
        IStartupNotices startupNotices,
        Func<FlareOptions, Action<string>?, CancellationToken, FlareResult> runFlare,
        Func<(string Text, string Tooltip)>? resolveCdbStatus = null)
    {
        _settingsService = settingsService;
        _startupNotices = startupNotices;
        _runFlare = runFlare;
        _resolveCdbStatus = resolveCdbStatus ?? ResolveCdbStatus;

        _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); SaveSettings(); };
        _logFlushTimer.Tick += (_, _) => FlushLog();

        LoadSettings();
        DetectCdb();
        CheckElevation();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.LoadSettings();
        _suppressSettingsSideEffects = true;
        try
        {
            if (settings.MaxDays > 0)
                MaxDays = settings.MaxDays;
            if (settings.MaxEvents > 0)
                MaxEvents = settings.MaxEvents;
            SortDescending = settings.SortDescending;
            RedactIdentifiers = settings.RedactIdentifiers;
        }
        finally
        {
            _suppressSettingsSideEffects = false;
        }

        var notice = _startupNotices.FirstMessage ?? _settingsService.LastLoadWarning;
        if (!string.IsNullOrEmpty(notice))
            BottomStatusText = Display(notice);
    }

    private void SaveSettings()
    {
        var settings = _settingsService.LoadSettings();
        settings.MaxDays = MaxDays;
        settings.MaxEvents = MaxEvents;
        settings.SortDescending = SortDescending;
        settings.RedactIdentifiers = RedactIdentifiers;
        _settingsService.SaveSettings(settings);

        var saveWarning = _settingsService.LastSaveWarning;
        if (!string.IsNullOrEmpty(saveWarning))
        {
            _activeSettingsSaveWarning = Display(saveWarning);
            BottomStatusText = _activeSettingsSaveWarning;
        }
        else if (!string.IsNullOrEmpty(_activeSettingsSaveWarning) &&
                 BottomStatusText == _activeSettingsSaveWarning)
        {
            _activeSettingsSaveWarning = null;
            BottomStatusText = "Ready";
        }
        else
        {
            _activeSettingsSaveWarning = null;
        }
    }

    private void QueueSaveSettings()
    {
        if (_suppressSettingsSideEffects)
            return;

        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    public void FlushPendingSave()
    {
        if (_saveDebounce.IsEnabled)
        {
            _saveDebounce.Stop();
            SaveSettings();
        }
    }

    private void AppendLog(string msg)
    {
        lock (_logLock)
        {
            _logBuilder.AppendLine(msg);
        }
    }

    private void FlushLog()
    {
        string snapshot;
        lock (_logLock) { snapshot = _logBuilder.ToString(); }
        if (snapshot != LogOutput)
            LogOutput = snapshot;
    }

    private void DetectCdb()
    {
        var status = _resolveCdbStatus();
        CdbStatusText = status.Text;
        _cdbStatusTooltipRaw = status.Tooltip;
        OnPropertyChanged(nameof(CdbStatusTooltip));
    }

    private void CheckElevation()
    {
        ElevationText = FlareRunner.IsElevated()
            ? "Administrator"
            : DeepAnalyze
                ? "UAC prompt on run (crash dumps)"
                : "No UAC unless crash dump analysis enabled";
    }

    private string Display(string text) =>
        RedactIdentifiers ? ReportRedaction.RedactAll(text, Environment.MachineName) : text;

    partial void OnMaxDaysChanged(int value) => QueueSaveSettings();
    partial void OnMaxEventsChanged(int value) => QueueSaveSettings();
    partial void OnSortDescendingChanged(bool value) => QueueSaveSettings();
    partial void OnRedactIdentifiersChanged(bool value)
    {
        OnPropertyChanged(nameof(OutputPathDisplay));
        OnPropertyChanged(nameof(CdbStatusTooltip));
        QueueSaveSettings();
    }
    partial void OnDeepAnalyzeChanged(bool value)
    {
        CheckElevation();
    }

    partial void OnIsRunningChanged(bool value) => RunCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        try
        {
            if (!string.IsNullOrEmpty(_lastSavedPath) && File.Exists(_lastSavedPath))
            {
                var psi = new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true };
                psi.ArgumentList.Add($"/select,{_lastSavedPath}");
                System.Diagnostics.Process.Start(psi);
                return;
            }

            if (!Directory.Exists(OutputPath))
                Directory.CreateDirectory(OutputPath);

            var openPsi = new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true };
            openPsi.ArgumentList.Add(OutputPath);
            System.Diagnostics.Process.Start(openPsi);
        }
        catch (Exception ex)
        {
            BottomStatusText = Display($"Could not open output folder: {ex.Message}");
        }
    }

    private bool TryCreateRunOptions(out FlareOptions options, out string? inputNote)
    {
        options = new FlareOptions();
        inputNote = null;

        if (MaxDays < 1 || MaxEvents < 1)
        {
            StatusText = "Check Max Days / Max Events";
            BottomStatusText = "Max Days and Max Events must be at least 1.";
            return false;
        }

        var effectiveMaxDays = Math.Min(MaxDays, EventLogParser.MaxDaysLimit);
        var effectiveMaxEvents = Math.Min(MaxEvents, EventLogParser.MaxEventsLimit);

        if (effectiveMaxDays != MaxDays || effectiveMaxEvents != MaxEvents)
        {
            MaxDays = effectiveMaxDays;
            MaxEvents = effectiveMaxEvents;
            inputNote = $"Input capped to Max Days={effectiveMaxDays}, Max Events={effectiveMaxEvents}.";
        }

        options = new FlareOptions
        {
            MaxDays = effectiveMaxDays,
            MaxEvents = effectiveMaxEvents,
            DeepAnalyze = DeepAnalyze,
            SortDescending = SortDescending,
            RedactIdentifiers = RedactIdentifiers,
            ReportDir = OutputPath,
        };
        return true;
    }

    private bool CanRun() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Run()
    {
        if (!TryCreateRunOptions(out var options, out var inputNote))
            return;

        IsRunning = true;
        StatusText = inputNote != null ? "Input capped" : "Running...";
        BottomStatusText = Display(inputNote != null
            ? $"{inputNote} Analysis in progress..."
            : "Analysis in progress...");
        lock (_logLock) { _logBuilder.Clear(); }
        LogOutput = "";
        _logFlushTimer.Start();
        if (inputNote != null) AppendLog(inputNote);

        try
        {
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            FlareResult? result = null;
            await Task.Run(() =>
            {
                result = _runFlare(options, AppendLog, ct);
            }, ct);

            if (result != null)
                CompleteRun(result);
        }
        catch (OperationCanceledException)
        {
            AppendLog("");
            AppendLog("Analysis cancelled.");
            StatusText = "Cancelled";
            BottomStatusText = "Analysis was cancelled by user";
        }
        catch (Exception ex)
        {
            AppendLog("");
            AppendLog(Display($"Error: {ex.Message}"));
            StatusText = "Error";
            BottomStatusText = Display($"Failed: {ex.Message}");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
            _logFlushTimer.Stop();
            // Final flush so the last batch reaches the UI even though the timer just stopped.
            FlushLog();
        }
    }

    private void CompleteRun(FlareResult result)
    {
        AppendLog("");
        AppendLog("Analysis complete.");
        var retentionSummary = RunStatusFormatter.GetEventLogRetentionSummary(result);
        if (retentionSummary != null)
            AppendLog(retentionSummary);

        _lastSavedPath = result.SavedPath;
        StatusText = RunStatusFormatter.GetRunStatusText(result);
        BottomStatusText = Display($"Report saved to: {result.SavedPath}");
    }

    internal static (string Text, string Tooltip) ResolveCdbStatus()
    {
        var cdbPath = CdbLocator.FindCdb();
        return GetCdbStatus(cdbPath);
    }

    internal static (string Text, string Tooltip) GetCdbStatus(string? resolvedPath)
    {
        if (resolvedPath != null)
            return ("cdb.exe detected",
                $"{resolvedPath}{Environment.NewLine}WinDbg !analyze -v details will be included when crash dump analysis is enabled.");

        return (
            "cdb.exe not found",
            "Crash dumps still use built-in crash dump parsing. Install WinDbg (winget install Microsoft.WinDbg) to add WinDbg !analyze -v details.");
    }
}
