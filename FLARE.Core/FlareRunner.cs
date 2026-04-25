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
    public string ReportDir { get; set; } = FlareStorage.ReportsDir();
    public bool SortDescending { get; set; } = true;

    // Default on: leaking UUID + computer name shouldn't be the default share path.
    public bool RedactIdentifiers { get; set; } = true;

    internal string? MinidumpsDir { get; set; }
}

public class FlareResult
{
    public GpuInfo? Gpu { get; set; }
    public SystemInfo? System { get; set; }
    public List<NvlddmkmError> Errors { get; set; } = [];
    public List<SystemCrashEvent> Crashes { get; set; } = [];
    public List<EventLogParser.AppCrashEvent> AppCrashes { get; set; } = [];
    public List<EventLogParser.DriverInstallEvent> DriverInstalls { get; set; } = [];
    public string? DumpAnalysis { get; set; }
    public string Report { get; set; } = "";
    public string SavedPath { get; set; } = "";
    public bool HasSmErrors { get; set; }
    public CollectorHealth? Health { get; set; }
}

public sealed record FlareDependencies(
    Func<Action<string>?, CancellationToken, GpuInfo> CollectGpu,
    Func<Action<string>?, CancellationToken, SystemInfo> CollectSystem,
    Func<int, int, Action<string>?, CancellationToken, List<NvlddmkmError>> PullGpuErrors,
    Func<int, Action<string>?, CancellationToken, List<SystemCrashEvent>> PullCrashEvents,
    Func<int, Action<string>?, CancellationToken, List<EventLogParser.AppCrashEvent>> PullAppCrashEvents,
    Func<int, Action<string>?, CancellationToken, List<EventLogParser.DriverInstallEvent>> PullDriverInstalls,
    Func<string, DateTime?, Action<string>?, CancellationToken, List<string>> CopyCrashDumps,
    Func<string, bool, DateTime?, Action<string>?, CancellationToken, string> GenerateDumpReport)
{
    // Observable from the caller: the same instance the default collectors capture
    // goes into ReportInput.Health, so custom-deps callers can pre-seed or inspect it.
    public CollectorHealth Health { get; init; } = new();

    public static FlareDependencies Default() => Default(new CollectorHealth());

    public static FlareDependencies Default(CollectorHealth health) => new(
        CollectGpu:         (log, ct)            => GpuInfo.Collect(log, ct, health),
        CollectSystem:      (log, ct)            => SystemInfo.Collect(log, ct, health),
        PullGpuErrors:      (days, max, log, ct) => EventLogParser.PullGpuErrors(days, max, log, ct, health),
        PullCrashEvents:    (days, log, ct)      => EventLogParser.PullCrashEvents(days, log, ct, health),
        PullAppCrashEvents: (days, log, ct)      => EventLogParser.PullAppCrashEvents(days, log, ct, health),
        PullDriverInstalls: (days, log, ct)      => EventLogParser.PullDriverInstalls(days, log, ct, health),
        CopyCrashDumps:     (destDir, cutoff, log, ct) => ElevatedDumpCopy.CopyViaElevatedHelper(destDir, cutoff, log, ct, health),
        GenerateDumpReport: (dumpDir, deep, cutoff, log, ct) =>
            DumpAnalyzer.GenerateDumpReport(dumpDir, deep, log, ct, cutoff, health))
    {
        Health = health,
    };
}

public static class FlareRunner
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static FlareResult Run(FlareOptions options, Action<string>? log = null, CancellationToken ct = default, FlareDependencies? deps = null)
    {
        deps ??= FlareDependencies.Default();
        var health = deps.Health;
        var effectiveMaxDays = Math.Min(Math.Max(options.MaxDays, 1), EventLogParser.MaxDaysLimit);
        var effectiveMaxEvents = Math.Min(Math.Max(options.MaxEvents, 1), EventLogParser.MaxEventsLimit);
        options.MaxDays = effectiveMaxDays;
        options.MaxEvents = effectiveMaxEvents;
        health.Truncation.RequestedMaxDays = effectiveMaxDays;
        health.Truncation.MaxEventsCap = effectiveMaxEvents;
        var result = new FlareResult();
        void Log(string msg) => log?.Invoke(RedactLogMessage(msg, options.RedactIdentifiers));

        Log("FLARE - Fault Log Analysis & Reboot Examination");
        Log("================================================\n");

        Log("Collecting GPU information...");
        ct.ThrowIfCancellationRequested();
        result.Gpu = deps.CollectGpu(Log, ct);
        Log($"  GPU:    {result.Gpu.Name}");
        Log($"  Driver: {result.Gpu.DriverVersion}");
        Log($"  UUID:   {result.Gpu.Uuid}");
        Log($"  SMs:    {result.Gpu.SmCount}");

        Log("\nCollecting system information...");
        ct.ThrowIfCancellationRequested();
        result.System = deps.CollectSystem(Log, ct);
        Log($"  Board:  {result.System.BoardManufacturer} {result.System.BoardProduct}");
        Log($"  BIOS:   {result.System.BiosVendor} {result.System.BiosVersion} ({result.System.BiosReleaseDate})");
        Log($"  CPU:    {result.System.ProcessorName}");
        Log($"  RAM:    {result.System.TotalMemoryFormatted}");

        Log($"\nPulling nvlddmkm errors (last {options.MaxDays} days, max {options.MaxEvents})...");
        ct.ThrowIfCancellationRequested();
        result.Errors = deps.PullGpuErrors(options.MaxDays, options.MaxEvents, Log, ct);
        Log($"  Found {result.Errors.Count} entries");

        Log("Pulling system crash events...");
        ct.ThrowIfCancellationRequested();
        result.Crashes = deps.PullCrashEvents(options.MaxDays, Log, ct);
        Log($"  Found {result.Crashes.Count} entries");

        Log("Pulling Application log crash/hang events...");
        ct.ThrowIfCancellationRequested();
        result.AppCrashes = deps.PullAppCrashEvents(options.MaxDays, Log, ct);
        Log($"  Found {result.AppCrashes.Count} Application log entries");

        Log("Pulling driver install history...");
        ct.ThrowIfCancellationRequested();
        result.DriverInstalls = deps.PullDriverInstalls(options.MaxDays, Log, ct);
        Log($"  Found {result.DriverInstalls.Count} entries");

        var dumpDir = options.MinidumpsDir ?? FlareStorage.MinidumpsDir();
        var dumpCutoff = DateTime.Now - TimeSpan.FromDays(options.MaxDays);
        Directory.CreateDirectory(options.ReportDir);

        var copiedDumps = new List<string>();
        if (options.DeepAnalyze)
        {
            Directory.CreateDirectory(dumpDir);
            Log("Copying crash dump files...");
            ct.ThrowIfCancellationRequested();
            copiedDumps = deps.CopyCrashDumps(dumpDir, dumpCutoff, Log, ct);
            if (copiedDumps.Count > 0)
                Log($"  Copied {copiedDumps.Count} new dump(s) to {dumpDir}");
            else
                Log("  No new dumps to copy");
        }
        else if (!options.DeepAnalyze)
        {
            Log("Crash dump analysis skipped (disabled).");
            health.Skipped("minidump analysis", "disabled by user; no crash dump files were copied or analyzed");
        }

        if (options.DeepAnalyze && Directory.Exists(dumpDir) && Directory.GetFiles(dumpDir, "*.dmp").Length > 0)
        {
            var dumpCrashRows = EventLogParser.EnumerateMinidumpFiles(dumpDir, dumpCutoff, Log, health);
            if (dumpCrashRows.Count > 0)
            {
                result.Crashes = result.Crashes.Concat(dumpCrashRows).OrderBy(e => e.Timestamp).ToList();
            }

            Log("Analyzing crash dumps...");
            ct.ThrowIfCancellationRequested();
            result.DumpAnalysis = deps.GenerateDumpReport(dumpDir, options.DeepAnalyze, dumpCutoff, Log, ct);
        }

        Log("Generating report...");
        ct.ThrowIfCancellationRequested();
        result.Report = ReportGenerator.Generate(new ReportInput(
            Gpu: result.Gpu,
            System: result.System,
            Errors: result.Errors,
            Crashes: result.Crashes,
            AppCrashes: result.AppCrashes,
            DriverInstalls: result.DriverInstalls,
            DumpAnalysis: result.DumpAnalysis,
            SortDescending: options.SortDescending,
            RedactIdentifiers: options.RedactIdentifiers,
            Health: health,
            DumpsCopiedThisRun: copiedDumps.Count));
        var savePath = ReportGenerator.SaveUnique(result.Report, options.ReportDir, DateTime.Now);
        result.SavedPath = savePath;
        result.HasSmErrors = result.Errors.Any(e => e.Gpc.HasValue);
        result.Health = health;

        Log($"\nReport saved to: {savePath}");

        return result;
    }

    private static string RedactLogMessage(string msg, bool redactIdentifiers) =>
        redactIdentifiers ? ReportRedaction.RedactAll(msg, Environment.MachineName) : msg;
}
