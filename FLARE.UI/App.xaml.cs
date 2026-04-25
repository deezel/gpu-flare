using Microsoft.Extensions.DependencyInjection;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using FLARE.Core;
using FLARE.UI.Services;
using FLARE.UI.ViewModels;

namespace FLARE.UI;

public partial class App : Application
{
    internal const string SmokeTestArg = "--smoke-test";
    internal const string VersionArg = "--version";

    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Helper mode — elevated child invoked by the main instance. Exit before any UI.
        if (e.Args.Length >= 2 && e.Args[0] == ElevatedDumpCopy.HelperArg)
        {
            DateTime? cutoff = null;
            if (e.Args.Length >= 3 &&
                long.TryParse(e.Args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
            {
                cutoff = new DateTime(ticks, DateTimeKind.Local);
            }
            int exit = ElevatedDumpCopy.RunHelperMode(e.Args[1], cutoff);
            Shutdown(exit);
            return;
        }

        if (e.Args.Length >= 1 && e.Args[0] == VersionArg)
        {
            // FLARE is a WinExe: without AttachConsole the parent shell sees no
            // output from `FLARE.exe --version`. Attaching here lets support
            // conversations ("what version are you on?") be answered from the
            // command line without opening the About dialog. When stdout is
            // redirected (piped or > file), the redirect wins over the attach.
            PrintVersionToParentConsole();
            Shutdown(0);
            return;
        }

        RegisterCrashHandlers();

        var startupNotices = new StartupNotices();

        // One-shot migration from pre-DO_NOT_SHARE layouts. Idempotent — no-op
        // on fresh installs and after the first successful run. Failures trace,
        // feed IStartupNotices for the status bar, and fall through so a bad
        // filesystem state never blocks startup.
        //
        // Default report folder only; users who customized OutputPath in 0.6.x
        // keep their legacy <custom>\minidumps\ intact and move it manually.
        try
        {
            FlareStorage.MigrateLegacyLayout(
                FlareStorage.ReportsDir(),
                msg =>
                {
                    System.Diagnostics.Trace.TraceInformation(msg);
                    if (msg.Contains("failed", StringComparison.OrdinalIgnoreCase))
                        startupNotices.Add(msg);
                });
        }
        catch (Exception ex)
        {
            var warning = $"FLARE: layout migration skipped: {ex.Message}";
            System.Diagnostics.Trace.TraceWarning(warning);
            startupNotices.Add(warning);
        }

        _serviceProvider = BuildServiceProvider(startupNotices);

        if (e.Args.Length >= 1 && e.Args[0] == SmokeTestArg)
        {
            _serviceProvider.GetRequiredService<MainWindow>();
            Shutdown(0);
            return;
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static ServiceProvider BuildServiceProvider(IStartupNotices startupNotices)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton(startupNotices);
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private void RegisterCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            WriteFatalLog("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
            var safeMessage = ReportRedaction.RedactAll(args.Exception.Message, Environment.MachineName);
            MessageBox.Show(
                $"FLARE hit an unhandled exception and will close.\n\n{safeMessage}\n\nA crash log was written to %LOCALAPPDATA%\\FLARE\\fatal.log",
                "FLARE - unexpected error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                WriteFatalLog("AppDomain.UnhandledException", ex);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteFatalLog("UnobservedTaskException", args.Exception);
        };
    }

    // Single-file rollover: once fatal.log exceeds ~1 MB, it's renamed to
    // fatal.log.old (overwriting any prior .old) so a crash loop can't fill
    // LocalAppData. One previous generation is enough context for triage.
    private const long FatalLogRotateBytes = 1 * 1024 * 1024;

    // Serialize the three crash handlers — their append + rotation races otherwise,
    // and the compound-failure case is exactly when entries matter most.
    private static readonly object FatalLogLock = new();

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
    private const int AttachParentProcess = -1;

    private static void PrintVersionToParentConsole()
    {
        // AttachConsole is best-effort. Launching FLARE.exe from Explorer has
        // no parent console to attach to; the call silently fails and the
        // output goes nowhere, which is fine — the target is the shell path.
        AttachConsole(AttachParentProcess);

        var asm = Assembly.GetExecutingAssembly();
        var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var runtime = RuntimeInformation.FrameworkDescription.Replace(".NET ", "");
        Console.WriteLine($"FLARE {infoVer}");
        Console.WriteLine($".NET {runtime} / {RuntimeInformation.RuntimeIdentifier}");
    }

    private static void WriteFatalLog(string source, Exception ex)
    {
        lock (FatalLogLock)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FLARE");
                Directory.CreateDirectory(dir);
                var logPath = Path.Combine(dir, "fatal.log");

                try
                {
                    if (File.Exists(logPath) && new FileInfo(logPath).Length >= FatalLogRotateBytes)
                    {
                        var rolled = logPath + ".old";
                        if (File.Exists(rolled)) File.Delete(rolled);
                        File.Move(logPath, rolled);
                    }
                }
                catch { /* rotation is best-effort; fall through and append anyway */ }

                var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {ex.GetType().FullName}: {ex.Message}\n{ex}\n\n";
                File.AppendAllText(logPath, ReportRedaction.RedactAll(entry, Environment.MachineName));
            }
            catch { /* nothing useful to do if the crash logger itself fails */ }
        }
    }
}
