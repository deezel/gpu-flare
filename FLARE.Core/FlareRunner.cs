using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;

namespace FLARE.Core;

public class FlareOptions
{
    public int MaxDays { get; set; } = 365;
    public int MaxEvents { get; set; } = 5000;
    public bool DeepAnalyze { get; set; }
    public string ReportDir { get; set; } = "reports";
    public string OutputPath { get; set; } = "";
    public bool SortDescending { get; set; } = true;
}

public class FlareResult
{
    public GpuInfo? Gpu { get; set; }
    public List<NvlddmkmError> Errors { get; set; } = [];
    public List<SystemCrashEvent> Crashes { get; set; } = [];
    public List<EventLogParser.AppCrashEvent> AppCrashes { get; set; } = [];
    public List<EventLogParser.DriverInstallEvent> DriverInstalls { get; set; } = [];
    public string? DumpAnalysis { get; set; }
    public string Report { get; set; } = "";
    public string SavedPath { get; set; } = "";
    public bool HasSmErrors { get; set; }
}

public static class FlareRunner
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static FlareResult Run(FlareOptions options, Action<string>? log = null, CancellationToken ct = default)
    {
        var result = new FlareResult();
        void Log(string msg) => log?.Invoke(msg);

        Log("FLARE - Fault Log Analysis & Reboot Examination");
        Log("================================================\n");

        if (!IsElevated())
            Log("Note: Run as Administrator to include crash dump files.\n");

        // 1. GPU info
        Log("Collecting GPU information...");
        ct.ThrowIfCancellationRequested();
        result.Gpu = GpuInfo.Collect(Log);
        Log($"  GPU:    {result.Gpu.Name}");
        Log($"  Driver: {result.Gpu.DriverVersion}");
        Log($"  UUID:   {result.Gpu.Uuid}");
        Log($"  SMs:    {result.Gpu.SmCount}");

        // 2. Event log errors
        Log($"\nPulling nvlddmkm errors (last {options.MaxDays} days, max {options.MaxEvents})...");
        ct.ThrowIfCancellationRequested();
        result.Errors = EventLogParser.PullGpuErrors(options.MaxDays, options.MaxEvents, Log, ct);
        Log($"  Found {result.Errors.Count} entries");

        // 3. System crash events
        Log("Pulling system crash events...");
        ct.ThrowIfCancellationRequested();
        result.Crashes = EventLogParser.PullCrashEvents(options.MaxDays, Log, ct);
        Log($"  Found {result.Crashes.Count} entries");

        // 3b. Application crash events
        Log("Pulling application crash events...");
        ct.ThrowIfCancellationRequested();
        result.AppCrashes = EventLogParser.PullAppCrashEvents(options.MaxDays, Log, ct);
        Log($"  Found {result.AppCrashes.Count} entries");

        // 3c. Driver install history
        Log("Pulling driver install history...");
        ct.ThrowIfCancellationRequested();
        result.DriverInstalls = EventLogParser.PullDriverInstalls(options.MaxDays, Log, ct);
        Log($"  Found {result.DriverInstalls.Count} entries");

        // 4. Copy minidumps
        var dumpDir = Path.Combine(options.ReportDir, "minidumps");
        Directory.CreateDirectory(options.ReportDir);
        Directory.CreateDirectory(dumpDir);

        Log("Copying crash dump files...");
        ct.ThrowIfCancellationRequested();
        var copiedDumps = MinidumpCollector.Copy(dumpDir, Log);
        if (copiedDumps.Count > 0)
            Log($"  Copied {copiedDumps.Count} new dump(s) to {dumpDir}");
        else
            Log("  No new dumps to copy");

        // 5. Analyze dumps
        if (Directory.Exists(dumpDir) && Directory.GetFiles(dumpDir, "*.dmp").Length > 0)
        {
            Log("Analyzing crash dumps...");
            ct.ThrowIfCancellationRequested();
            result.DumpAnalysis = DumpAnalyzer.GenerateDumpReport(dumpDir, options.DeepAnalyze, Log, ct);
        }

        // 6. Generate report
        Log("Generating report...");
        ct.ThrowIfCancellationRequested();
        var savePath = !string.IsNullOrEmpty(options.OutputPath)
            ? options.OutputPath
            : Path.Combine(options.ReportDir, $"flare_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        result.Report = ReportGenerator.Generate(result.Gpu, result.Errors, result.Crashes, result.AppCrashes, result.DriverInstalls, result.DumpAnalysis, sortDescending: options.SortDescending);
        ReportGenerator.Save(result.Report, savePath);
        result.SavedPath = savePath;
        result.HasSmErrors = result.Errors.Any(e => e.Gpc.HasValue);

        Log($"\nReport saved to: {savePath}");

        return result;
    }
}
