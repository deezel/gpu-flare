using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FLARE.Core;
using FLARE.UI.Services;

namespace FLARE.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsService _settingsService;
    private readonly StringBuilder _logBuilder = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _outputPath;

    [ObservableProperty]
    private int _maxDays = 365;

    [ObservableProperty]
    private int _maxEvents = 5000;

    [ObservableProperty]
    private bool _deepAnalyze;

    [ObservableProperty]
    private bool _sortDescending = true;

    [ObservableProperty]
    private bool _cdbFound;

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

    public MainViewModel(IFileDialogService fileDialogService, ISettingsService settingsService)
    {
        _fileDialogService = fileDialogService;
        _settingsService = settingsService;

        var currentDir = Directory.GetCurrentDirectory();
        _outputPath = Path.Combine(currentDir, "reports");

        LoadSettings();
        DetectCdb();
        CheckElevation();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.LoadSettings();
        if (!string.IsNullOrWhiteSpace(settings.OutputPath))
            OutputPath = settings.OutputPath;
        if (settings.MaxDays > 0)
            MaxDays = settings.MaxDays;
        if (settings.MaxEvents > 0)
            MaxEvents = settings.MaxEvents;
        SortDescending = settings.SortDescending;
    }

    private void SaveSettings()
    {
        var settings = _settingsService.LoadSettings();
        settings.OutputPath = OutputPath;
        settings.MaxDays = MaxDays;
        settings.MaxEvents = MaxEvents;
        settings.SortDescending = SortDescending;
        _settingsService.SaveSettings(settings);
    }

    private void DetectCdb()
    {
        bool elevated = FlareRunner.IsElevated();
        var cdbPath = DumpAnalyzer.FindCdb();
        CdbFound = cdbPath != null && elevated;

        if (!elevated)
            CdbStatusText = "Requires administrator elevation";
        else if (cdbPath == null)
            CdbStatusText = "cdb.exe not found — install WinDbg to enable: winget install Microsoft.WinDbg";
        else
            CdbStatusText = $"cdb.exe: {cdbPath}";
    }

    private void CheckElevation()
    {
        ElevationText = FlareRunner.IsElevated() ? "Administrator" : "Not elevated (minidumps may be inaccessible)";
    }

    partial void OnOutputPathChanged(string value) => SaveSettings();
    partial void OnMaxDaysChanged(int value) => SaveSettings();
    partial void OnMaxEventsChanged(int value) => SaveSettings();
    partial void OnSortDescendingChanged(bool value) => SaveSettings();
    partial void OnIsRunningChanged(bool value) => RunCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        var path = _fileDialogService.SelectFolderDialog(
            Directory.Exists(OutputPath) ? OutputPath : null);
        if (!string.IsNullOrEmpty(path))
            OutputPath = path;
    }

    private bool CanRun() => !IsRunning && !string.IsNullOrWhiteSpace(OutputPath);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task Run()
    {
        IsRunning = true;
        StatusText = "Running...";
        BottomStatusText = "Analysis in progress...";
        _logBuilder.Clear();
        LogOutput = "";

        try
        {
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            var options = new FlareOptions
            {
                MaxDays = MaxDays,
                MaxEvents = MaxEvents,
                DeepAnalyze = DeepAnalyze,
                SortDescending = SortDescending,
                ReportDir = OutputPath
            };

            FlareResult? result = null;
            await Task.Run(() =>
            {
                result = FlareRunner.Run(options, msg =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _logBuilder.AppendLine(msg);
                        LogOutput = _logBuilder.ToString();
                    });
                }, ct);
            }, ct);

            if (result != null)
            {
                _logBuilder.AppendLine();
                _logBuilder.AppendLine("=== REPORT ===");
                _logBuilder.AppendLine(result.Report);
                LogOutput = _logBuilder.ToString();

                StatusText = result.HasSmErrors ? "SM-coordinate errors found" : "No SM-coordinate errors";
                BottomStatusText = $"Report saved to: {result.SavedPath}";
            }
        }
        catch (OperationCanceledException)
        {
            _logBuilder.AppendLine("\nAnalysis cancelled.");
            LogOutput = _logBuilder.ToString();
            StatusText = "Cancelled";
            BottomStatusText = "Analysis was cancelled by user";
        }
        catch (Exception ex)
        {
            _logBuilder.AppendLine($"\nError: {ex.Message}");
            LogOutput = _logBuilder.ToString();
            StatusText = "Error";
            BottomStatusText = $"Failed: {ex.Message}";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
        }
    }
}
