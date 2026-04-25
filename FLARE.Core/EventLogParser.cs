using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace FLARE.Core;

public record NvlddmkmError(
    DateTime Timestamp,
    int EventId,
    string Message,
    int? Gpc, int? Tpc, int? Sm,
    string? ErrorType
);

public record SystemCrashEvent(
    DateTime Timestamp,
    string Source,
    int EventId,
    string Description
);

public static partial class EventLogParser
{
    public const int MaxDaysLimit = 3650;
    public const int MaxEventsLimit = 100_000;
    internal const double AppCrashCorrelationWindowSeconds = 30;

    public const int BsodCap = 200;
    public const int RebootCap = 200;
    public const int AppCrashCap = 50_000;
    internal const string SystemLogName = "System";
    internal const string ApplicationLogName = "Application";
    internal const string NvlddmkmGpuEventXPath =
        "*[System[Provider[@Name='nvlddmkm'] and (EventID=13 or EventID=14 or EventID=153)]]";
    internal const string ApplicationCrashHangXPath =
        "*[System[(Provider[@Name='Application Error'] and EventID=1000) or (Provider[@Name='Application Hang'] and EventID=1002)]]";

    // 500ms guard against backtrack-hangs from a malformed nvlddmkm payload.
    internal static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

    [GeneratedRegex(@"GPC\s*(\d+),\s*TPC\s*(\d+),\s*SM\s*(\d+)", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex GpuCoordsRegex();

    [GeneratedRegex(
        @"(?:Exception.*?:\s*)(Illegal Instruction Encoding|Multiple Warp Errors|Illegal Global Access|Page Fault|Misaligned Address|Misaligned PC)",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex GpuExceptionRegex();

    [GeneratedRegex(@"Uncorrectable.*SRAM Error", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex SramErrorRegex();

    [GeneratedRegex(@"uncorrectable ECC error", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex EccErrorRegex();

    [GeneratedRegex(@"CMDre\s+[0-9a-fA-F]", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex CmdreHintRegex();

    [GeneratedRegex(@"Graphics Exception.*ESR", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex GraphicsExceptionEsrRegex();

    [GeneratedRegex(@"Graphics SM [^|]+", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex GraphicsSmLineRegex();

    [GeneratedRegex(@"Graphics Exception:[^|]+", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex GraphicsExceptionLineRegex();

    [GeneratedRegex(@"(An uncorrectable ECC[^|]+|PCIE[^|,]+Uncorrectable[^|]+)", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex EccLineRegex();

    [GeneratedRegex(@"(CMDre\s+[0-9a-fA-F ]+)", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex CmdreLineRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex WhitespaceRunRegex();

    internal static List<T> ReadEvents<T>(
        string logName,
        string xpath,
        int cap,
        Action<string>? log,
        string warningPrefix,
        CancellationToken ct,
        Func<EventRecord, T?> map,
        int? scanCap = null,
        string? scanTruncationWarning = null,
        string? resultTruncationWarning = null,
        Action? onResultCapHit = null,
        Action? onScanCapHit = null,
        Action<Exception>? onFailure = null) where T : class
    {
        var results = new List<T>();
        try
        {
            var query = CreateEventLogQuery(logName, xpath);
            using var reader = new EventLogReader(query);

            EventRecord? rec;
            int matched = 0;
            int scanned = 0;
            while (matched < cap &&
                   (!scanCap.HasValue || scanned < scanCap.Value) &&
                   (rec = reader.ReadEvent()) != null)
            {
                scanned++;
                using (rec)
                {
                    ct.ThrowIfCancellationRequested();
                    var mapped = map(rec);
                    if (mapped != null)
                    {
                        results.Add(mapped);
                        matched++;
                    }
                }
            }

            var stoppedByResultCap = matched >= cap;
            var stoppedByScanCap = scanCap.HasValue && scanned >= scanCap.Value && results.Count < cap;
            if (stoppedByResultCap || stoppedByScanCap)
            {
                ct.ThrowIfCancellationRequested();
                using var extra = reader.ReadEvent();
                if (extra != null)
                {
                    if (stoppedByResultCap)
                    {
                        log?.Invoke(resultTruncationWarning ??
                            $"Warning: Event Log read reached its {cap} result cap; results may be incomplete.");
                        onResultCapHit?.Invoke();
                    }
                    else
                    {
                        log?.Invoke(scanTruncationWarning ??
                            $"Warning: Event Log scan stopped after {scanned} record(s); {results.Count} matched.");
                        onScanCapHit?.Invoke();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrEmpty(warningPrefix))
                log?.Invoke($"{warningPrefix}: {ex.Message}");
            onFailure?.Invoke(ex);
        }
        return results;
    }

    internal static EventLogQuery CreateEventLogQuery(string logName, string xpath) =>
        new(logName, PathType.LogName, xpath) { ReverseDirection = true };

    internal static EventLogRetentionInfo? InspectSystemEventLogRetention(
        Action<string>? log = null,
        CollectorHealth? health = null,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            string? mode;
            long? maxSize;
            using (var config = new EventLogConfiguration(SystemLogName))
            {
                mode = config.LogMode.ToString();
                maxSize = config.MaximumSizeInBytes;
            }

            var info = EventLogSession.GlobalSession.GetLogInformation(SystemLogName, PathType.LogName);
            var oldestRecord = ReadOldestEventTimestamp(SystemLogName, xpath: null, ct);
            var oldestGpuEvent = ReadOldestEventTimestamp(SystemLogName, NvlddmkmGpuEventXPath, ct);

            return new EventLogRetentionInfo(
                SystemLogName,
                mode,
                maxSize,
                info.FileSize,
                info.RecordCount,
                info.OldestRecordNumber,
                oldestRecord,
                OldestRelevantEventTimestamp: oldestGpuEvent,
                OldestRelevantEventDescription: "nvlddmkm 13/14/153");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Could not inspect System Event Log retention: {ex.Message}");
            health?.Failure("Event Log: System retention", ex.Message);
            return null;
        }
    }

    internal static EventLogRetentionInfo? InspectApplicationEventLogRetention(
        Action<string>? log = null,
        CollectorHealth? health = null,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            string? mode;
            long? maxSize;
            using (var config = new EventLogConfiguration(ApplicationLogName))
            {
                mode = config.LogMode.ToString();
                maxSize = config.MaximumSizeInBytes;
            }

            var info = EventLogSession.GlobalSession.GetLogInformation(ApplicationLogName, PathType.LogName);
            var oldestRecord = ReadOldestEventTimestamp(ApplicationLogName, xpath: null, ct);
            var oldestAppCrashOrHang = ReadOldestEventTimestamp(ApplicationLogName, ApplicationCrashHangXPath, ct);

            return new EventLogRetentionInfo(
                ApplicationLogName,
                mode,
                maxSize,
                info.FileSize,
                info.RecordCount,
                info.OldestRecordNumber,
                oldestRecord,
                OldestRelevantEventTimestamp: oldestAppCrashOrHang,
                OldestRelevantEventDescription: "Application Error 1000 / Application Hang 1002");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Could not inspect Application Event Log retention: {ex.Message}");
            health?.Failure("Event Log: Application retention", ex.Message);
            return null;
        }
    }

    internal static DateTime? ReadOldestEventTimestamp(
        string logName,
        string? xpath,
        CancellationToken ct = default)
    {
        var query = xpath == null
            ? new EventLogQuery(logName, PathType.LogName)
            : new EventLogQuery(logName, PathType.LogName, xpath);
        query.ReverseDirection = false;

        using var reader = new EventLogReader(query);
        ct.ThrowIfCancellationRequested();
        using var rec = reader.ReadEvent();
        ct.ThrowIfCancellationRequested();
        return rec?.TimeCreated;
    }

    public static List<NvlddmkmError> PullGpuErrors(int maxDays, int maxEvents, Action<string>? log = null, CancellationToken ct = default, CollectorHealth? health = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDays);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEvents);
        if (maxDays > MaxDaysLimit) maxDays = MaxDaysLimit;
        if (maxEvents > MaxEventsLimit) maxEvents = MaxEventsLimit;

        if (health != null && health.SystemEventLog == null)
            health.SystemEventLog = InspectSystemEventLogRetention(log, health, ct);

        long msCutoff = (long)TimeSpan.FromDays(maxDays).TotalMilliseconds;
        var xpath = $"*[System[Provider[@Name='nvlddmkm'] and (EventID=13 or EventID=14 or EventID=153) and TimeCreated[timediff(@SystemTime) <= {msCutoff}]]]";

        var errors = ReadEvents<NvlddmkmError>(
            SystemLogName, xpath, maxEvents, log, "Warning: Could not read Event Log", ct,
            rec => ClassifyGpuError(
                rec.TimeCreated ?? DateTime.MinValue,
                rec.Id,
                NormalizeEventProperties(rec.Properties.Select(p => p.Value))),
            resultTruncationWarning:
                $"Warning: nvlddmkm event read reached Max Events ({maxEvents}); GPU error history may be incomplete. Raise Max Events or narrow Max Days if needed.",
            onResultCapHit: () => { if (health != null) health.Truncation.GpuErrorsResultCap = true; },
            onFailure: ex => health?.Failure("Event Log: nvlddmkm", ex.Message));

        WarnIfClassifierDrift(errors, log, health);

        return errors.OrderBy(e => e.Timestamp).ToList();
    }

    internal const int ClassifierDriftMinUnclassified = 5;
    internal const int ClassifierDriftMinPercent = 25;

    internal static void WarnIfClassifierDrift(IReadOnlyList<NvlddmkmError> errors, Action<string>? log, CollectorHealth? health = null)
    {
        if (errors.Count == 0) return;

        int unclassified = 0;
        foreach (var e in errors)
            if (e.ErrorType == null && !e.Gpc.HasValue) unclassified++;

        if (unclassified == 0) return;

        bool allUnclassified = unclassified == errors.Count;
        bool partialDrift = unclassified >= ClassifierDriftMinUnclassified
                            && unclassified * 100 >= errors.Count * ClassifierDriftMinPercent;
        if (!allUnclassified && !partialDrift) return;

        var msg = allUnclassified
            ? $"{errors.Count} nvlddmkm event(s) read but none matched the known payload shapes " +
              "(no SM coordinates, no error-type classification). The classifier may need updating for a driver-side format change."
            : $"{unclassified} of {errors.Count} nvlddmkm event(s) ({unclassified * 100 / errors.Count}%) had neither SM coordinates nor a recognized error type. " +
              "The classifier may need updating for a partial driver-side format change.";
        log?.Invoke($"Warning: {msg}");
        health?.Canary("nvlddmkm classifier", msg);
    }

    // " ||| " separator: prevents adjacent property values forming a phrase (e.g. "SM 0"
    // bleeding into the next field) that would falsely trigger classifier regexes.
    internal static string NormalizeEventProperties(IEnumerable<object?> values) =>
        string.Join(" ||| ",
            values.Select(v => WhitespaceRunRegex().Replace((v?.ToString() ?? "").Trim(), " ")));

    internal static NvlddmkmError ClassifyGpuError(DateTime timestamp, int eventId, string allData)
    {
        int? gpc = null, tpc = null, sm = null;
        string? errorType = null;

        var locMatch = GpuCoordsRegex().Match(allData);
        // TryParse rather than Parse: the \d+ capture has no length cap, and an
        // oversized numeric field in a malformed payload would otherwise throw
        // OverflowException and blank the whole batch at the outer catch.
        if (locMatch.Success &&
            int.TryParse(locMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var gpcVal) &&
            int.TryParse(locMatch.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var tpcVal) &&
            int.TryParse(locMatch.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var smVal))
        {
            gpc = gpcVal;
            tpc = tpcVal;
            sm = smVal;
        }

        var errMatch = GpuExceptionRegex().Match(allData);
        if (errMatch.Success)
            errorType = errMatch.Groups[1].Value;
        else if (SramErrorRegex().IsMatch(allData))
            errorType = "Uncorrectable SRAM Error";
        else if (EccErrorRegex().IsMatch(allData))
            errorType = "Uncorrectable ECC Error";
        else if (CmdreHintRegex().IsMatch(allData))
            errorType = "Command Re-execution Error (CMDre)";
        else if (GraphicsExceptionEsrRegex().IsMatch(allData))
            errorType = "Graphics Exception (ESR)";
        else if (eventId == 153)
            errorType = "TDR (Timeout Detection and Recovery)";

        var smLine = GraphicsSmLineRegex().Match(allData);
        var esrLine = GraphicsExceptionLineRegex().Match(allData);
        var eccLine = EccLineRegex().Match(allData);
        var cmdreLine = CmdreLineRegex().Match(allData);
        var msg = smLine.Success ? smLine.Value.Trim()
            : esrLine.Success ? esrLine.Value.Trim()
            : eccLine.Success ? eccLine.Value.Trim()
            : cmdreLine.Success ? cmdreLine.Value.Trim()
            : $"Event {eventId}";

        return new NvlddmkmError(timestamp, eventId, msg, gpc, tpc, sm, errorType);
    }

    public record AppCrashEvent(
        DateTime Timestamp,
        string Application,
        string FaultingModule,
        string Description
    );

    public static List<AppCrashEvent> PullAppCrashEvents(int maxDays, Action<string>? log = null, CancellationToken ct = default, CollectorHealth? health = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDays);
        if (maxDays > MaxDaysLimit) maxDays = MaxDaysLimit;

        if (health != null && health.ApplicationEventLog == null)
            health.ApplicationEventLog = InspectApplicationEventLogRetention(log, health, ct);

        long msCutoff = (long)TimeSpan.FromDays(maxDays).TotalMilliseconds;
        var events = new List<AppCrashEvent>();

        // App-crash scope is defined by MaxDays; a high cap protects memory on pathological
        // boxes without silently degrading "last N days" into "newest N rows in the window"
        // the way a small cap would. Cap-hit disclosed in SCOPE block.
        int crash1000Total = 0, crash1000ShapeFails = 0;
        var crash1000 = ReadEvents<AppCrashEvent>(
            ApplicationLogName,
            $"*[System[Provider[@Name='Application Error'] and EventID=1000 and TimeCreated[timediff(@SystemTime) <= {msCutoff}]]]",
            AppCrashCap, log, "Warning: Could not read application crash events", ct,
            rec =>
            {
                var ts = rec.TimeCreated ?? DateTime.MinValue;
                var props = rec.Properties;
                crash1000Total++;
                if (props.Count < 4) crash1000ShapeFails++;
                var app = props.Count > 0 ? props[0].Value?.ToString() ?? "?" : "?";
                var mod = props.Count > 3 ? props[3].Value?.ToString() ?? "?" : "?";
                return new AppCrashEvent(ts, app, mod, $"{app} (faulting module: {mod})");
            },
            resultTruncationWarning:
                $"Warning: Application Error 1000 read reached {AppCrashCap} entries; app crash history may be incomplete. Narrow Max Days if needed.",
            onResultCapHit: () => { if (health != null) health.Truncation.AppCrashesResultCap = true; },
            onFailure: ex => health?.Failure("Event Log: application crashes", ex.Message));
        events.AddRange(crash1000);
        WarnIfAppEventShapeDrift("Application Error 1000", crash1000Total, crash1000ShapeFails, expectedMinProps: 4, log, health);

        int hang1002Total = 0, hang1002ShapeFails = 0;
        var hang1002 = ReadEvents<AppCrashEvent>(
            ApplicationLogName,
            $"*[System[Provider[@Name='Application Hang'] and EventID=1002 and TimeCreated[timediff(@SystemTime) <= {msCutoff}]]]",
            AppCrashCap, log, "Warning: Could not read application hang events", ct,
            rec =>
            {
                var ts = rec.TimeCreated ?? DateTime.MinValue;
                var props = rec.Properties;
                hang1002Total++;
                if (props.Count < 1) hang1002ShapeFails++;
                var app = props.Count > 0 ? props[0].Value?.ToString() ?? "?" : "?";
                return new AppCrashEvent(ts, app, "HANG", $"{app} (hang)");
            },
            resultTruncationWarning:
                $"Warning: Application Hang 1002 read reached {AppCrashCap} entries; hang history may be incomplete. Narrow Max Days if needed.",
            onResultCapHit: () => { if (health != null) health.Truncation.AppCrashesResultCap = true; },
            onFailure: ex => health?.Failure("Event Log: application hangs", ex.Message));
        events.AddRange(hang1002);
        WarnIfAppEventShapeDrift("Application Hang 1002", hang1002Total, hang1002ShapeFails, expectedMinProps: 1, log, health);

        return events.OrderBy(e => e.Timestamp).ToList();
    }

    internal const int AppEventShapeDriftMinFailures = 5;
    internal const int AppEventShapeDriftMinPercent = 25;

    internal static void WarnIfAppEventShapeDrift(string source, int total, int shapeFails, int expectedMinProps, Action<string>? log, CollectorHealth? health)
    {
        if (total == 0 || shapeFails == 0) return;
        bool allFailed = shapeFails == total;
        bool partialDrift = shapeFails >= AppEventShapeDriftMinFailures
                            && shapeFails * 100 >= total * AppEventShapeDriftMinPercent;
        if (!allFailed && !partialDrift) return;

        var msg = allFailed
            ? $"{total} {source} event(s) read but none carried the expected {expectedMinProps}-property payload shape. " +
              "The WER event schema may have changed and per-app attribution will be empty."
            : $"{shapeFails} of {total} {source} event(s) ({shapeFails * 100 / total}%) lacked the expected {expectedMinProps}-property payload shape; " +
              "some app/module attributions will render as '?'.";
        log?.Invoke($"Warning: {msg}");
        health?.Canary(source + " payload", msg);
    }

    public static List<(NvlddmkmError gpuError, AppCrashEvent appCrash, double secondsApart)> CorrelateWithAppCrashes(
        List<NvlddmkmError> gpuErrors, List<AppCrashEvent> appCrashes, double windowSeconds = AppCrashCorrelationWindowSeconds)
    {
        var gpu = gpuErrors.OrderBy(e => e.Timestamp).ToList();
        var app = appCrashes.OrderBy(a => a.Timestamp).ToList();

        var correlations = new List<(NvlddmkmError gpuError, AppCrashEvent appCrash, double secondsApart)>();
        var window = TimeSpan.FromSeconds(windowSeconds);
        int lo = 0;

        foreach (var g in gpu)
        {
            while (lo < app.Count && app[lo].Timestamp < g.Timestamp - window)
                lo++;

            for (int i = lo; i < app.Count; i++)
            {
                var diff = app[i].Timestamp - g.Timestamp;
                if (diff > window) break;
                correlations.Add((g, app[i], Math.Abs(diff.TotalSeconds)));
            }
        }

        return correlations;
    }

    public record DriverInstallEvent(
        DateTime Timestamp,
        string DriverVersion,
        string Description
    );

    public static List<DriverInstallEvent> PullDriverInstalls(int maxDays, Action<string>? log = null, CancellationToken ct = default, CollectorHealth? health = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDays);
        if (maxDays > MaxDaysLimit) maxDays = MaxDaysLimit;

        long msCutoff = (long)TimeSpan.FromDays(maxDays).TotalMilliseconds;
        var cutoff = DateTime.Now - TimeSpan.FromDays(maxDays);
        var events = new List<DriverInstallEvent>();

        // Driver-install scope is defined by MaxDays. Keep the event-log queries
        // unbounded within that window so older installs remain available for
        // timeline attribution instead of being silently dropped by a recent-only cap.
        int pnpMismatches = 0;
        var pnp = ReadEvents<DriverInstallEvent>(
            "System",
            $"*[System[Provider[@Name='Microsoft-Windows-Kernel-PnP'] and (EventID=400 or EventID=410) and TimeCreated[timediff(@SystemTime) <= {msCutoff}]]]",
            int.MaxValue, log, "Warning: Could not read driver install events", ct,
            rec => MapDriverInstall(rec, "nvlddmkm|NVIDIA", onSchemeMismatch: () => pnpMismatches++),
            onFailure: ex => health?.Failure("Event Log: Kernel-PnP driver installs", ex.Message));
        events.AddRange(pnp);
        WarnIfDriverInstallSchemeDrift("Kernel-PnP driver install", pnpMismatches, pnp.Count, log, health);

        int dsmMismatches = 0;
        var dsm = ReadEvents<DriverInstallEvent>(
            "Microsoft-Windows-DeviceSetupManager/Admin",
            $"*[System[EventID=161 and TimeCreated[timediff(@SystemTime) <= {msCutoff}]]]",
            int.MaxValue, log, "", ct,
            rec => MapDeviceSetupManagerDriverInstall(rec, onSchemeMismatch: () => dsmMismatches++),
            onFailure: ex =>
            {
                if (ex is EventLogNotFoundException) return;
                log?.Invoke($"Warning: Could not read DeviceSetupManager driver install events: {ex.Message}");
                health?.Failure("Event Log: DeviceSetupManager driver installs", ex.Message);
            });
        events.AddRange(dsm);
        WarnIfDriverInstallSchemeDrift("DeviceSetupManager driver install", dsmMismatches, dsm.Count, log, health);

        try
        {
            events.AddRange(ParseSetupApiDevLogs(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                cutoff, log, health, ct));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: setupapi log discovery failed: {ex.Message}");
            health?.Failure("setupapi log discovery", ex.Message);
        }

        return DeduplicateDriverInstalls(events);
    }

    internal static List<DriverInstallEvent> ParseSetupApiDevLogs(
        string windowsDir,
        DateTime? cutoff = null,
        Action<string>? log = null,
        CollectorHealth? health = null,
        CancellationToken ct = default)
    {
        var events = new List<DriverInstallEvent>();
        foreach (var setupApiPath in EnumerateSetupApiDevLogs(windowsDir))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                events.AddRange(ParseSetupApiLog(setupApiPath, cutoff, log, health, ct));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Warning: setupapi log parse failed ({Path.GetFileName(setupApiPath)}): {ex.Message}");
                health?.Failure($"setupapi log: {Path.GetFileName(setupApiPath)}", ex.Message);
            }
        }

        return events;
    }

    internal static List<string> EnumerateSetupApiDevLogs(string windowsDir)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(windowsDir)) return results;

        var infDir = Path.Combine(windowsDir, "INF");
        var current = Path.Combine(infDir, "setupapi.dev.log");
        if (File.Exists(current))
            results.Add(current);

        if (!Directory.Exists(infDir)) return results;

        foreach (var archive in Directory.EnumerateFiles(infDir, "setupapi.dev.*.log")
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!results.Contains(archive, StringComparer.OrdinalIgnoreCase))
                results.Add(archive);
        }

        return results;
    }

    internal static List<DriverInstallEvent> DeduplicateDriverInstalls(IEnumerable<DriverInstallEvent> events)
    {
        var deduped = new List<DriverInstallEvent>();
        foreach (var e in events.OrderBy(e => e.Timestamp))
        {
            var duplicate = deduped.Any(existing =>
                string.Equals(existing.DriverVersion, e.DriverVersion, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs((e.Timestamp - existing.Timestamp).TotalMinutes) <= 60);
            if (!duplicate)
                deduped.Add(e);
        }
        return deduped;
    }

    private static DriverInstallEvent? MapDriverInstall(EventRecord rec, string vendorPattern, Action? onSchemeMismatch = null)
    {
        string msg;
        try { msg = rec.FormatDescription() ?? ""; }
        catch { msg = ""; }
        var ts = rec.TimeCreated ?? DateTime.MinValue;

        return MapDriverInstallMessage(ts, msg, vendorPattern, onSchemeMismatch);
    }

    private static DriverInstallEvent? MapDeviceSetupManagerDriverInstall(EventRecord rec, Action? onSchemeMismatch = null)
    {
        string msg;
        try { msg = rec.FormatDescription() ?? ""; }
        catch { msg = ""; }
        var ts = rec.TimeCreated ?? DateTime.MinValue;

        return MapDeviceSetupManagerDriverInstallMessage(ts, rec.Id, msg, onSchemeMismatch);
    }

    internal static DriverInstallEvent? MapDriverInstallMessage(DateTime timestamp, string msg, string vendorPattern, Action? onSchemeMismatch = null)
    {
        if (!Regex.IsMatch(msg, vendorPattern, RegexOptions.IgnoreCase, RegexTimeout)) return null;

        var matches = Regex.Matches(msg, @"\d+\.\d+\.\d+\.\d+", RegexOptions.None, RegexTimeout);
        if (matches.Count == 0)
        {
            if (LooksVersionBearing(msg))
                onSchemeMismatch?.Invoke();
            return null;
        }

        string? version = null;
        foreach (Match m in matches)
        {
            if (IsNvidiaWddmDriverVersion(m.Value))
            {
                version = m.Value;
                break;
            }
        }
        if (version == null)
        {
            onSchemeMismatch?.Invoke();
            return null;
        }

        var cleaned = Regex.Replace(msg, @"\r?\n", " ", RegexOptions.None, RegexTimeout);
        var truncated = cleaned.Substring(0, Math.Min(200, cleaned.Length));
        return new DriverInstallEvent(timestamp, version, truncated);
    }

    internal static DriverInstallEvent? MapDeviceSetupManagerDriverInstallMessage(DateTime timestamp, int eventId, string msg, Action? onSchemeMismatch = null)
    {
        if (eventId != 161) return null;
        return MapDriverInstallMessage(timestamp, msg, "NVIDIA", onSchemeMismatch);
    }

    internal static bool LooksVersionBearing(string msg) =>
        Regex.IsMatch(msg, @"\b(?:Current\s+)?Version\b", RegexOptions.IgnoreCase, RegexTimeout);

    internal static bool IsNvidiaWddmDriverVersion(string version) =>
        Regex.IsMatch(version, @"^\d{2,3}\.0\.15\.\d{4}$", RegexOptions.None, RegexTimeout);

    internal static void WarnIfDriverInstallSchemeDrift(string source, int mismatches, int matched, Action<string>? log, CollectorHealth? health)
    {
        if (mismatches == 0) return;
        var msg = matched == 0
            ? $"{mismatches} {source} event(s) matched the NVIDIA driver-install filter, but none included a driver version FLARE could use. " +
              "This affects only Driver Install History; it may be empty or incomplete."
            : $"{mismatches} {source} event(s) matched the NVIDIA driver-install filter but did not include a driver version FLARE could use " +
              $"({matched} matched and were parsed). This affects only Driver Install History; it may be incomplete.";
        log?.Invoke($"Warning: {msg}");
        health?.Canary(source + " version scheme", msg);
    }

    internal static List<DriverInstallEvent> ParseSetupApiLog(
        string path,
        DateTime? cutoff = null,
        Action<string>? log = null,
        CollectorHealth? health = null,
        CancellationToken ct = default)
    {
        var results = new List<DriverInstallEvent>();

        DateTime? currentBootSession = null;
        DateTime? pendingInstallTime = null;
        bool inNvidiaInstallSection = false;
        bool nvidiaVersionCaptured = false;
        int schemeMismatchesInNvidiaContext = 0;

        void ResetNvidiaContext()
        {
            pendingInstallTime = null;
            inNvidiaInstallSection = false;
            nvidiaVersionCaptured = false;
        }

        foreach (var line in File.ReadLines(path))
        {
            ct.ThrowIfCancellationRequested();

            if (Regex.IsMatch(line, @"^>>>\s+\[Device Install .* - PCI\\VEN_10DE", RegexOptions.None, RegexTimeout))
            {
                ResetNvidiaContext();
                inNvidiaInstallSection = true;
                continue;
            }

            if (line.StartsWith(">>>  [", StringComparison.Ordinal))
            {
                ResetNvidiaContext();
                continue;
            }

            if (line.StartsWith("<<<  Section end", StringComparison.Ordinal))
            {
                ResetNvidiaContext();
                continue;
            }

            if (inNvidiaInstallSection)
            {
                var sectionStartMatch = Regex.Match(
                    line,
                    @"^>>>\s+Section start (\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2})\.\d+",
                    RegexOptions.None,
                    RegexTimeout);
                if (sectionStartMatch.Success)
                {
                    if (DateTime.TryParseExact(
                            sectionStartMatch.Groups[1].Value,
                            "yyyy/MM/dd HH:mm:ss",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out var sectionStart))
                        pendingInstallTime = sectionStart;
                    continue;
                }
            }

            var bootMatch = Regex.Match(line, @"Boot Session: (\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2})", RegexOptions.None, RegexTimeout);
            if (bootMatch.Success)
            {
                ResetNvidiaContext();
                if (DateTime.TryParseExact(
                        bootMatch.Groups[1].Value,
                        "yyyy/MM/dd HH:mm:ss",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var bs))
                    currentBootSession = bs;
                continue;
            }

            var nvidiaInstallMatch = Regex.Match(line, @"Install Device - PCI\\VEN_10DE.*\}\s+(\d{2}:\d{2}:\d{2}\.\d+)", RegexOptions.None, RegexTimeout);
            if (nvidiaInstallMatch.Success && currentBootSession.HasValue && !nvidiaVersionCaptured)
            {
                var timeStr = nvidiaInstallMatch.Groups[1].Value.Split('.')[0];
                if (TimeSpan.TryParse(timeStr, CultureInfo.InvariantCulture, out var tod))
                {
                    var ts = currentBootSession.Value.Date + tod;
                    if (tod < currentBootSession.Value.TimeOfDay)
                        ts = ts.AddDays(1);
                    pendingInstallTime = ts;
                }
                continue;
            }

            // Any non-NVIDIA install ends the NVIDIA install block — stops a subsequent
            // N.0.15.M version line from latching onto a stale NVIDIA install timestamp.
            if (Regex.IsMatch(line, @"Install Device - PCI\\VEN_", RegexOptions.None, RegexTimeout))
            {
                ResetNvidiaContext();
                continue;
            }

            // Match any 4-part version first, then post-filter for the ".0.15." NVIDIA-WDDM
            // signature. When we see a version in an NVIDIA install context that doesn't match
            // the signature, count it
            // as a scheme-mismatch canary — catches silent-empty if NVIDIA reworks the scheme.
            var verMatch = Regex.Match(line, @"inf:\s+Driver Version\s*[=:]\s*\S+,(\d+\.\d+\.\d+\.\d+)", RegexOptions.None, RegexTimeout);
            if (verMatch.Success)
            {
                var version = verMatch.Groups[1].Value;
                if (IsNvidiaWddmDriverVersion(version))
                {
                    if (pendingInstallTime.HasValue)
                    {
                        if (!cutoff.HasValue || pendingInstallTime.Value >= cutoff.Value)
                            results.Add(new DriverInstallEvent(pendingInstallTime.Value, version,
                                $"setupapi: {version}"));
                        if (inNvidiaInstallSection)
                        {
                            nvidiaVersionCaptured = true;
                            pendingInstallTime = null;
                        }
                        else
                        {
                            ResetNvidiaContext();
                        }
                    }
                }
                else if (pendingInstallTime.HasValue)
                {
                    schemeMismatchesInNvidiaContext++;
                    if (inNvidiaInstallSection)
                    {
                        nvidiaVersionCaptured = true;
                        pendingInstallTime = null;
                    }
                    else
                    {
                        ResetNvidiaContext();
                    }
                }
            }
        }

        if (schemeMismatchesInNvidiaContext > 0)
        {
            var msg =
                $"setupapi log contained {schemeMismatchesInNvidiaContext} NVIDIA driver install(s) without a driver version FLARE could use " +
                $"({results.Count} matched and were parsed). This affects only Driver Install History; it may be incomplete.";
            log?.Invoke($"Warning: {msg}");
            health?.Canary("setupapi version scheme", msg);
        }

        return results;
    }

    public static List<SystemCrashEvent> PullCrashEvents(int maxDays, Action<string>? log = null, CancellationToken ct = default, CollectorHealth? health = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDays);
        if (maxDays > MaxDaysLimit) maxDays = MaxDaysLimit;

        long msCutoff = (long)TimeSpan.FromDays(maxDays).TotalMilliseconds;
        var events = new List<SystemCrashEvent>();

        events.AddRange(ReadEvents<SystemCrashEvent>(
            "System",
            $"*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001 and TimeCreated[timediff(@SystemTime) <= {msCutoff}]]]",
            BsodCap, log, "Warning: Could not read crash events", ct,
            rec =>
            {
                string msg;
                try { msg = rec.FormatDescription() ?? ""; }
                catch { msg = ""; }
                var cleaned = Regex.Replace(msg, @"\r?\n", " ", RegexOptions.None, RegexTimeout);
                var ts = rec.TimeCreated ?? DateTime.MinValue;
                return new SystemCrashEvent(ts, "BSOD", 1001, cleaned);
            },
            resultTruncationWarning:
                $"Warning: BSOD event read reached {BsodCap} entries; system crash history may be incomplete. Narrow Max Days if needed.",
            onResultCapHit: () => { if (health != null) health.Truncation.BsodResultCap = true; },
            onFailure: ex => health?.Failure("Event Log: BSOD events", ex.Message)));

        int bugcheckShapeFailures = 0;
        string? firstBugcheckShapeType = null;
        events.AddRange(ReadEvents<SystemCrashEvent>(
            "System",
            $"*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41 and TimeCreated[timediff(@SystemTime) <= {msCutoff}]]]",
            RebootCap, log, "Warning: Could not read kernel power events", ct,
            rec =>
            {
                var ts = rec.TimeCreated ?? DateTime.MinValue;
                uint bc = 0;
                bool shapeFailed = false;
                if (rec.Properties.Count > 0 && rec.Properties[0].Value != null)
                {
                    try { bc = Convert.ToUInt32(rec.Properties[0].Value); }
                    catch
                    {
                        shapeFailed = true;
                        bugcheckShapeFailures++;
                        firstBugcheckShapeType ??= rec.Properties[0].Value?.GetType().Name ?? "null";
                    }
                }
                if (shapeFailed)
                {
                    return new SystemCrashEvent(ts, "REBOOT", 41,
                        "Unexpected reboot: bugcheck payload unparseable (code unknown)");
                }
                var bcHex = $"0x{bc:X8}";
                var desc = BugcheckCatalog.GetKernelPowerDescription(bc);
                return new SystemCrashEvent(ts, "REBOOT", 41,
                    $"Unexpected reboot: {desc} (code {bcHex})");
            },
            resultTruncationWarning:
                $"Warning: Kernel-Power event read reached {RebootCap} entries; reboot history may be incomplete. Narrow Max Days if needed.",
            onResultCapHit: () => { if (health != null) health.Truncation.RebootResultCap = true; },
            onFailure: ex => health?.Failure("Event Log: Kernel-Power events", ex.Message)));

        if (bugcheckShapeFailures > 0)
        {
            var msg =
                $"{bugcheckShapeFailures} Kernel-Power 41 event(s) had a bugcheck property that was not a UInt32 " +
                $"(first seen as {firstBugcheckShapeType}). Those rows render as 'bugcheck payload unparseable'; the payload parser may need updating.";
            log?.Invoke($"Warning: {msg}");
            health?.Canary("Kernel-Power 41 bugcheck", msg);
        }

        return events.OrderBy(e => e.Timestamp).ToList();
    }

    public static List<SystemCrashEvent> EnumerateMinidumpFiles(string dumpDir, DateTime cutoff, Action<string>? log = null, CollectorHealth? health = null)
    {
        var events = new List<SystemCrashEvent>();
        try
        {
            if (!Directory.Exists(dumpDir)) return events;
            foreach (var dmp in Directory.GetFiles(dumpDir, "*.dmp"))
            {
                var info = new FileInfo(dmp);
                if (info.LastWriteTime < cutoff) continue;
                var kb = (int)Math.Round(info.Length / 1024.0);
                events.Add(new SystemCrashEvent(info.LastWriteTime, "MINIDUMP", 0,
                    $"Crash dump: {info.Name} ({kb} KB)"));
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Could not enumerate minidump files: {ex.Message}");
            health?.Failure("minidump enumeration", ex.Message);
        }
        return events;
    }
}
