using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
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

public static class EventLogParser
{
    public static List<NvlddmkmError> PullGpuErrors(int maxDays, int maxEvents, Action<string>? log = null, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDays);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEvents);

        var errors = new List<NvlddmkmError>();

        try
        {
            long msCutoff = (long)TimeSpan.FromDays(maxDays).TotalMilliseconds;
            var xpath = $"*[System[Provider[@Name='nvlddmkm'] and (EventID=13 or EventID=14 or EventID=153) and TimeCreated[timediff(@SystemTime) <= {msCutoff}]]]";
            var query = new EventLogQuery("System", PathType.LogName, xpath);
            using var reader = new EventLogReader(query);

            EventRecord? rec;
            int count = 0;
            while (count < maxEvents && (rec = reader.ReadEvent()) != null)
            {
                using (rec)
                {
                    ct.ThrowIfCancellationRequested();
                    var data = string.Join(" ||| ",
                        rec.Properties.Select(p =>
                            Regex.Replace((p.Value?.ToString() ?? "").Trim(), @"\s+", " ")));
                    var err = ClassifyGpuError(
                        rec.TimeCreated ?? DateTime.MinValue,
                        rec.Id,
                        data);
                    errors.Add(err);
                    count++;
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Could not read Event Log: {ex.Message}");
        }

        return errors.OrderBy(e => e.Timestamp).ToList();
    }

    /// <summary>
    /// Classify an nvlddmkm event's data payload into structured fields.
    /// </summary>
    internal static NvlddmkmError ClassifyGpuError(DateTime timestamp, int eventId, string allData)
    {
        int? gpc = null, tpc = null, sm = null;
        string? errorType = null;

        var locMatch = Regex.Match(allData, @"GPC\s*(\d+),\s*TPC\s*(\d+),\s*SM\s*(\d+)");
        if (locMatch.Success)
        {
            gpc = int.Parse(locMatch.Groups[1].Value);
            tpc = int.Parse(locMatch.Groups[2].Value);
            sm = int.Parse(locMatch.Groups[3].Value);
        }

        var errMatch = Regex.Match(allData,
            @"(?:Exception.*?:\s*)(Illegal Instruction Encoding|Multiple Warp Errors|Illegal Global Access|Page Fault|Misaligned Address|Misaligned PC)",
            RegexOptions.IgnoreCase);
        if (errMatch.Success)
            errorType = errMatch.Groups[1].Value;
        else if (Regex.IsMatch(allData, @"Uncorrectable.*SRAM Error", RegexOptions.IgnoreCase))
            errorType = "Uncorrectable SRAM Error";
        else if (Regex.IsMatch(allData, @"uncorrectable ECC error", RegexOptions.IgnoreCase))
            errorType = "Uncorrectable ECC Error";
        else if (Regex.IsMatch(allData, @"CMDre\s+[0-9a-fA-F]", RegexOptions.IgnoreCase))
            errorType = "Command Re-execution Error (CMDre)";
        else if (Regex.IsMatch(allData, @"Graphics Exception.*ESR", RegexOptions.IgnoreCase))
            errorType = "Graphics Exception (ESR)";
        else if (eventId == 153)
            errorType = "TDR (Timeout Detection and Recovery)";

        var smLine = Regex.Match(allData, @"Graphics SM [^|]+");
        var esrLine = Regex.Match(allData, @"Graphics Exception:[^|]+");
        var eccLine = Regex.Match(allData, @"(An uncorrectable ECC[^|]+|PCIE[^|,]+Uncorrectable[^|]+)");
        var cmdreLine = Regex.Match(allData, @"(CMDre\s+[0-9a-fA-F ]+)");
        var msg = smLine.Success ? smLine.Value.Trim()
            : esrLine.Success ? esrLine.Value.Trim()
            : eccLine.Success ? eccLine.Value.Trim()
            : cmdreLine.Success ? cmdreLine.Value.Trim()
            : $"Event {eventId}";

        return new NvlddmkmError(timestamp, eventId, msg, gpc, tpc, sm, errorType);
    }

    /// <summary>
    /// Parse a single tilde-delimited line (legacy format) into an NvlddmkmError.
    /// Retained for test compatibility; the live pipeline uses ClassifyGpuError directly.
    /// </summary>
    internal static NvlddmkmError? ParseGpuErrorLine(string line)
    {
        var parts = line.Split('~', 3);
        if (parts.Length < 3) return null;
        if (!DateTime.TryParse(parts[0].Trim(), out var ts)) return null;
        int.TryParse(parts[1].Trim(), out var eid);
        var allData = parts[2].Trim();
        return ClassifyGpuError(ts, eid, allData);
    }

    public record AppCrashEvent(
        DateTime Timestamp,
        string Application,
        string FaultingModule,
        string Description
    );

    public static List<AppCrashEvent> PullAppCrashEvents(int maxDays, Action<string>? log = null, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDays);

        var events = new List<AppCrashEvent>();
        long msCutoff = (long)TimeSpan.FromDays(maxDays).TotalMilliseconds;

        // Application Error 1000 — crashes
        try
        {
            var xpath = $"*[System[Provider[@Name='Application Error'] and EventID=1000 and TimeCreated[timediff(@SystemTime) <= {msCutoff}]]]";
            var query = new EventLogQuery("Application", PathType.LogName, xpath);
            using var reader = new EventLogReader(query);

            EventRecord? rec;
            int count = 0;
            while (count < 500 && (rec = reader.ReadEvent()) != null)
            {
                using (rec)
                {
                    ct.ThrowIfCancellationRequested();
                    var ts = rec.TimeCreated ?? DateTime.MinValue;
                    var props = rec.Properties;
                    var app = props.Count > 0 ? props[0].Value?.ToString() ?? "?" : "?";
                    var mod = props.Count > 3 ? props[3].Value?.ToString() ?? "?" : "?";
                    events.Add(new AppCrashEvent(ts, app, mod, $"{app} (faulting module: {mod})"));
                    count++;
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Could not read application crash events: {ex.Message}");
        }

        // Application Hang 1002
        try
        {
            var xpath = $"*[System[Provider[@Name='Application Hang'] and EventID=1002 and TimeCreated[timediff(@SystemTime) <= {msCutoff}]]]";
            var query = new EventLogQuery("Application", PathType.LogName, xpath);
            using var reader = new EventLogReader(query);

            EventRecord? rec;
            int count = 0;
            while (count < 500 && (rec = reader.ReadEvent()) != null)
            {
                using (rec)
                {
                    ct.ThrowIfCancellationRequested();
                    var ts = rec.TimeCreated ?? DateTime.MinValue;
                    var props = rec.Properties;
                    var app = props.Count > 0 ? props[0].Value?.ToString() ?? "?" : "?";
                    events.Add(new AppCrashEvent(ts, app, "HANG", $"{app} (hang)"));
                    count++;
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Could not read application hang events: {ex.Message}");
        }

        return events.OrderBy(e => e.Timestamp).ToList();
    }

    /// <summary>
    /// Find app crashes that occurred within a time window of GPU errors.
    /// </summary>
    public static List<(NvlddmkmError gpuError, AppCrashEvent appCrash, double secondsApart)> CorrelateWithAppCrashes(
        List<NvlddmkmError> gpuErrors, List<AppCrashEvent> appCrashes, double windowSeconds = 30)
    {
        var correlations = new List<(NvlddmkmError gpuError, AppCrashEvent appCrash, double secondsApart)>();

        foreach (var gpu in gpuErrors)
        {
            foreach (var app in appCrashes)
            {
                var diff = Math.Abs((gpu.Timestamp - app.Timestamp).TotalSeconds);
                if (diff <= windowSeconds)
                    correlations.Add((gpu, app, diff));
            }
        }

        return correlations.OrderBy(c => c.gpuError.Timestamp).ToList();
    }

    public record DriverInstallEvent(
        DateTime Timestamp,
        string DriverVersion,
        string Description
    );

    public static List<DriverInstallEvent> PullDriverInstalls(int maxDays, Action<string>? log = null, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDays);

        var events = new List<DriverInstallEvent>();
        long msCutoff = (long)TimeSpan.FromDays(maxDays).TotalMilliseconds;

        // Kernel-PnP driver installs (Event IDs 400, 410)
        try
        {
            var xpath = $"*[System[Provider[@Name='Microsoft-Windows-Kernel-PnP'] and (EventID=400 or EventID=410) and TimeCreated[timediff(@SystemTime) <= {msCutoff}]]]";
            var query = new EventLogQuery("System", PathType.LogName, xpath);
            using var reader = new EventLogReader(query);

            EventRecord? rec;
            int count = 0;
            while (count < 200 && (rec = reader.ReadEvent()) != null)
            {
                using (rec)
                {
                    ct.ThrowIfCancellationRequested();
                    string msg;
                    try { msg = rec.FormatDescription() ?? ""; }
                    catch { msg = ""; }

                    if (!Regex.IsMatch(msg, "nvlddmkm|NVIDIA|nvidia", RegexOptions.IgnoreCase)) continue;

                    var verMatch = Regex.Match(msg, @"(\d+\.\d+\.\d+\.\d+)");
                    if (!verMatch.Success) continue;

                    var cleaned = Regex.Replace(msg, @"\r?\n", " ");
                    var truncated = cleaned.Substring(0, Math.Min(200, cleaned.Length));
                    var ts = rec.TimeCreated ?? DateTime.MinValue;
                    events.Add(new DriverInstallEvent(ts, verMatch.Groups[1].Value, truncated));
                    count++;
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Could not read driver install events: {ex.Message}");
        }

        // DeviceSetupManager/Admin — best effort, log may not exist on all systems
        try
        {
            var xpath = $"*[System[TimeCreated[timediff(@SystemTime) <= {msCutoff}]]]";
            var query = new EventLogQuery("Microsoft-Windows-DeviceSetupManager/Admin", PathType.LogName, xpath);
            using var reader = new EventLogReader(query);

            EventRecord? rec;
            int count = 0;
            while (count < 500 && (rec = reader.ReadEvent()) != null)
            {
                using (rec)
                {
                    ct.ThrowIfCancellationRequested();
                    string msg;
                    try { msg = rec.FormatDescription() ?? ""; }
                    catch { msg = ""; }

                    if (!Regex.IsMatch(msg, "nvlddmkm|NVIDIA|nvidia|display", RegexOptions.IgnoreCase)) continue;

                    var verMatch = Regex.Match(msg, @"(\d+\.\d+\.\d+\.\d+)");
                    if (!verMatch.Success) continue;

                    var cleaned = Regex.Replace(msg, @"\r?\n", " ");
                    var truncated = cleaned.Substring(0, Math.Min(200, cleaned.Length));
                    var ts = rec.TimeCreated ?? DateTime.MinValue;
                    events.Add(new DriverInstallEvent(ts, verMatch.Groups[1].Value, truncated));
                    count++;
                }
            }
        }
        catch { }

        // Also parse setupapi.dev.log for longer history
        try
        {
            var setupApiPath = @"C:\Windows\INF\setupapi.dev.log";
            if (File.Exists(setupApiPath))
                events.AddRange(ParseSetupApiLog(setupApiPath));
        }
        catch { }

        // Deduplicate: same version within same hour = same install from different sources
        return events
            .OrderBy(e => e.Timestamp)
            .GroupBy(e => $"{e.DriverVersion}_{e.Timestamp:yyyy-MM-dd HH}")
            .Select(g => g.First())
            .ToList();
    }

    internal static List<DriverInstallEvent> ParseSetupApiLog(string path)
    {
        var results = new List<DriverInstallEvent>();
        var lines = File.ReadAllLines(path);

        DateTime? currentBootSession = null;
        DateTime? lastInstallTime = null;
        string? pendingVersion = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Track boot sessions for date context
            var bootMatch = Regex.Match(line, @"Boot Session: (\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2})");
            if (bootMatch.Success)
            {
                DateTime.TryParse(bootMatch.Groups[1].Value.Replace('/', '-'), out var bs);
                currentBootSession = bs;
                continue;
            }

            // Look for NVIDIA GPU device install with timestamp
            var installMatch = Regex.Match(line, @"Install Device - PCI\\VEN_10DE.*\}\s+(\d{2}:\d{2}:\d{2}\.\d+)");
            if (installMatch.Success && currentBootSession.HasValue)
            {
                var timeStr = installMatch.Groups[1].Value.Split('.')[0];
                if (TimeSpan.TryParse(timeStr, out var tod))
                    lastInstallTime = currentBootSession.Value.Date + tod;
                continue;
            }

            // Capture driver version near an install (inf: Driver Version: or inf: Driver Version =)
            var verMatch = Regex.Match(line, @"inf:\s+Driver Version\s*[=:]\s*\S+,(\d+\.0\.15\.\d+)");
            if (verMatch.Success)
            {
                pendingVersion = verMatch.Groups[1].Value;

                // If we have a recent install timestamp, pair them
                if (lastInstallTime.HasValue &&
                    (pendingVersion != null))
                {
                    results.Add(new DriverInstallEvent(lastInstallTime.Value, pendingVersion,
                        $"setupapi: {pendingVersion}"));
                    lastInstallTime = null;
                    pendingVersion = null;
                }
            }
        }

        return results;
    }

    public static List<SystemCrashEvent> PullCrashEvents(int maxDays, Action<string>? log = null, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDays);

        var events = new List<SystemCrashEvent>();
        long msCutoff = (long)TimeSpan.FromDays(maxDays).TotalMilliseconds;

        // BSODs (WER-SystemErrorReporting 1001)
        try
        {
            var xpath = $"*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001 and TimeCreated[timediff(@SystemTime) <= {msCutoff}]]]";
            var query = new EventLogQuery("System", PathType.LogName, xpath);
            using var reader = new EventLogReader(query);

            EventRecord? rec;
            int count = 0;
            while (count < 200 && (rec = reader.ReadEvent()) != null)
            {
                using (rec)
                {
                    ct.ThrowIfCancellationRequested();
                    string msg;
                    try { msg = rec.FormatDescription() ?? ""; }
                    catch { msg = ""; }
                    var cleaned = Regex.Replace(msg, @"\r?\n", " ");
                    var ts = rec.TimeCreated ?? DateTime.MinValue;
                    events.Add(new SystemCrashEvent(ts, "BSOD", 1001, cleaned));
                    count++;
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Could not read crash events: {ex.Message}");
        }

        // Unexpected reboots (Kernel-Power 41)
        try
        {
            var xpath = $"*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41 and TimeCreated[timediff(@SystemTime) <= {msCutoff}]]]";
            var query = new EventLogQuery("System", PathType.LogName, xpath);
            using var reader = new EventLogReader(query);

            EventRecord? rec;
            int count = 0;
            while (count < 200 && (rec = reader.ReadEvent()) != null)
            {
                using (rec)
                {
                    ct.ThrowIfCancellationRequested();
                    var ts = rec.TimeCreated ?? DateTime.MinValue;
                    uint bc = 0;
                    if (rec.Properties.Count > 0 && rec.Properties[0].Value != null)
                    {
                        try { bc = Convert.ToUInt32(rec.Properties[0].Value); } catch { }
                    }
                    var bcHex = $"0x{bc:X8}";
                    var desc = bc switch
                    {
                        0x116 => "VIDEO_TDR_FAILURE (GPU stopped responding)",
                        0x117 => "VIDEO_TDR_TIMEOUT_DETECTED",
                        0x119 => "VIDEO_SCHEDULER_INTERNAL_ERROR",
                        0x278 => "KERNEL_MODE_HEAP_CORRUPTION",
                        0x133 => "DPC_WATCHDOG_VIOLATION",
                        0x307 => "KERNEL_STORAGE_SLOT_IN_USE",
                        0 => "Unexpected power loss / hard reboot",
                        _ => $"Bugcheck {bcHex}"
                    };
                    events.Add(new SystemCrashEvent(ts, "REBOOT", 41,
                        $"Unexpected reboot: {desc} (code {bcHex})"));
                    count++;
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Could not read kernel power events: {ex.Message}");
        }

        // Minidump files
        try
        {
            var dumpDir = MinidumpCollector.GetSystemDumpDir();
            if (Directory.Exists(dumpDir))
            {
                foreach (var dmp in Directory.GetFiles(dumpDir, "*.dmp"))
                {
                    var info = new FileInfo(dmp);
                    var kb = (int)Math.Round(info.Length / 1024.0);
                    events.Add(new SystemCrashEvent(info.LastWriteTime, "MINIDUMP", 0,
                        $"Crash dump: {info.Name} ({kb} KB)"));
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Could not enumerate minidump files: {ex.Message}");
        }

        return events.OrderBy(e => e.Timestamp).ToList();
    }
}
