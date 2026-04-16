using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FLARE.Core;

public static class ReportGenerator
{
    public static string Generate(GpuInfo gpu, List<NvlddmkmError> errors,
        List<SystemCrashEvent>? crashes = null,
        List<EventLogParser.AppCrashEvent>? appCrashes = null,
        List<EventLogParser.DriverInstallEvent>? driverInstalls = null,
        string? dumpAnalysis = null,
        int maxTimelineEntries = 0,
        bool sortDescending = true)
    {
        static IEnumerable<T> ByTime<T>(IEnumerable<T> items, Func<T, DateTime> key, bool desc) =>
            desc ? items.OrderByDescending(key) : items.OrderBy(key);

        var sb = new StringBuilder();
        var now = DateTime.Now;
        const string sep = "------------------------------------------------------------------------";

        sb.AppendLine("GPU Error Analysis Report");
        sb.AppendLine($"Generated: {now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(sep);
        sb.AppendLine();

        // 1. GPU Info
        sb.AppendLine("1. GPU IDENTIFICATION");
        sb.AppendLine(sep);
        sb.AppendLine($"  GPU:            {gpu.Name}");
        sb.AppendLine($"  Driver:         {gpu.DriverVersion}");
        sb.AppendLine($"  VBIOS:          {gpu.VbiosVersion}");
        sb.AppendLine($"  UUID:           {gpu.Uuid}");
        sb.AppendLine($"  PCI Bus:        {gpu.PciId}");
        sb.AppendLine($"  SMs:            {gpu.SmCount}");
        sb.AppendLine($"  Memory:         {gpu.MemoryTotal}");
        sb.AppendLine($"  OS:             {Environment.OSVersion}");
        sb.AppendLine($"  Computer:       {Environment.MachineName}");
        sb.AppendLine();

        // 2. Error summary
        sb.AppendLine("2. NVLDDMKM ERROR SUMMARY");
        sb.AppendLine(sep);

        if (errors.Count == 0)
        {
            sb.AppendLine("  No nvlddmkm errors found in Windows Event Log.");
        }
        else
        {
            sb.AppendLine($"  Total errors:   {errors.Count}");
            sb.AppendLine($"  Date range:     {errors.First().Timestamp:yyyy-MM-dd} to {errors.Last().Timestamp:yyyy-MM-dd}");
            sb.AppendLine();

            var withCoords = errors.Where(e => e.Gpc.HasValue && e.Tpc.HasValue && e.Sm.HasValue).ToList();
            var withoutCoords = errors.Where(e => !e.Gpc.HasValue || !e.Tpc.HasValue || !e.Sm.HasValue).ToList();

            if (withCoords.Count > 0)
            {
                sb.AppendLine("  Errors by SM location:");
                var grouped = withCoords
                    .GroupBy(e => $"GPC {e.Gpc}, TPC {e.Tpc}, SM {e.Sm}")
                    .OrderByDescending(g => g.Count());

                foreach (var g in grouped)
                {
                    var pct = (double)g.Count() / withCoords.Count * 100;
                    sb.AppendLine($"    {g.Key}: {g.Count()} errors ({pct:F1}%)");
                }

                sb.AppendLine();

                var uniqueLocations = grouped.Count();
                var totalSMs = gpu.SmCount > 0 ? gpu.SmCount : 170;

                sb.AppendLine("  Analysis:");
                sb.AppendLine($"    Total SMs on this GPU: {totalSMs}");
                sb.AppendLine($"    SM locations with errors: {uniqueLocations}");
                sb.AppendLine($"    Errors with SM coordinates: {withCoords.Count}");

                if (uniqueLocations <= 4 && withCoords.Count >= 2)
                {
                    double log10Prob = withCoords.Count * Math.Log10((double)uniqueLocations / totalSMs);
                    sb.AppendLine();
                    sb.AppendLine($"    If these errors were distributed randomly across all {totalSMs} SMs,");
                    sb.AppendLine($"    the probability of all {withCoords.Count} errors hitting only {uniqueLocations} SM(s)");
                    sb.AppendLine($"    would be approximately 10^{log10Prob:F0} (effectively zero).");
                    sb.AppendLine($"    This suggests the errors are not randomly distributed.");
                }
            }

            if (withoutCoords.Count > 0)
                sb.AppendLine($"\n  Errors without SM coordinates: {withoutCoords.Count}");

            var errorTypes = errors.Where(e => e.ErrorType != null).GroupBy(e => e.ErrorType).OrderByDescending(g => g.Count());
            if (errorTypes.Any())
            {
                sb.AppendLine();
                sb.AppendLine("  Error types:");
                foreach (var g in errorTypes)
                    sb.AppendLine($"    {g.Key}: {g.Count()} occurrences");
            }
        }

        sb.AppendLine();

        // 3. Error frequency
        int nextSection = 3;
        if (errors.Count >= 2)
        {
            sb.AppendLine($"{nextSection}. ERROR FREQUENCY (per week)");
            sb.AppendLine(sep);

            var byWeek = ByTime(
                    errors.GroupBy(e => {
                        var d = e.Timestamp.Date;
                        return d.AddDays(-(int)d.DayOfWeek); // week start (Sunday)
                    }),
                    g => g.Key, sortDescending)
                .ToList();

            int maxCount = byWeek.Max(g => g.Count());
            const int barWidth = 40;

            // Pre-sort driver installs ascending so the "most recent install at or before
            // a given week" lookup is the same regardless of the output iteration order.
            var sortedDrivers = driverInstalls?
                .OrderBy(d => d.Timestamp)
                .ToList();

            foreach (var week in byWeek)
            {
                int barLen = maxCount > 0 ? (int)Math.Ceiling((double)week.Count() / maxCount * barWidth) : 0;
                var bar = new string('#', barLen);
                var weekLabel = week.Key.ToString("yyyy-MM-dd");
                var countStr = week.Count().ToString().PadLeft(5);

                string driverNote = "";
                if (sortedDrivers != null && sortedDrivers.Count > 0)
                {
                    var weekStart = week.Key;
                    var weekEnd = week.Key.AddDays(7);

                    // Drivers installed during this week, oldest-first so "A > B" reads
                    // as "upgraded from A to B".
                    var thisWeek = sortedDrivers
                        .Where(d => d.Timestamp >= weekStart && d.Timestamp < weekEnd)
                        .Select(d => ToNvidiaVersion(d.DriverVersion))
                        .Distinct()
                        .ToList();

                    if (thisWeek.Count > 0)
                    {
                        driverNote = $"  (drv {string.Join(" > ", thisWeek)})";
                    }
                    else
                    {
                        // The active driver during this week is the most recent install
                        // at or before the week start — independent of iteration order.
                        var prior = sortedDrivers.LastOrDefault(d => d.Timestamp < weekStart);
                        if (prior != null)
                            driverNote = $"  (drv {ToNvidiaVersion(prior.DriverVersion)})";
                    }
                }

                sb.AppendLine($"    {weekLabel} {countStr} |{bar}{driverNote}");
            }

            sb.AppendLine();
            nextSection++;
        }

        // Driver version timeline
        if (driverInstalls != null && driverInstalls.Count > 0)
        {
            sb.AppendLine($"{nextSection}. DRIVER INSTALL HISTORY");
            sb.AppendLine(sep);

            foreach (var d in ByTime(driverInstalls, d => d.Timestamp, sortDescending))
                sb.AppendLine($"    {d.Timestamp:yyyy-MM-dd HH:mm:ss}  {ToNvidiaVersion(d.DriverVersion)} ({d.DriverVersion})");

            // Correlate: show error count per driver install period
            if (errors.Count > 0 && driverInstalls.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  Errors per install period:");
                var sorted = driverInstalls.OrderBy(d => d.Timestamp).ToList();

                // Build rows in chronological order, then emit in requested order.
                // The pre-log row (if any) represents the oldest bucket, so it
                // goes first in ascending and last in descending output.
                var rows = new List<string>();
                var preCount = errors.Count(e => e.Timestamp < sorted[0].Timestamp);
                if (preCount > 0)
                    rows.Add($"    (unknown, pre-log): {preCount} errors (to {sorted[0].Timestamp:yyyy-MM-dd})");

                for (int i = 0; i < sorted.Count; i++)
                {
                    var start = sorted[i].Timestamp;
                    var end = i + 1 < sorted.Count ? sorted[i + 1].Timestamp : DateTime.MaxValue;
                    var count = errors.Count(e => e.Timestamp >= start && e.Timestamp < end);
                    var endStr = end == DateTime.MaxValue ? "present" : end.ToString("yyyy-MM-dd");
                    rows.Add($"    {start:yyyy-MM-dd}  {ToNvidiaVersion(sorted[i].DriverVersion),-8} {count,5} errors  (to {endStr})");
                }

                if (sortDescending) rows.Reverse();
                foreach (var row in rows) sb.AppendLine(row);
            }

            sb.AppendLine();
            nextSection++;
        }

        // Error timeline
        if (errors.Count > 0)
        {
            int showCount = maxTimelineEntries > 0 ? maxTimelineEntries : errors.Count;
            var timelineErrors = ByTime(errors, e => e.Timestamp, sortDescending).Take(showCount).ToList();
            int omitted = errors.Count - timelineErrors.Count;

            sb.AppendLine($"{nextSection}. ERROR TIMELINE ({timelineErrors.Count} of {errors.Count} entries)");
            sb.AppendLine(sep);
            if (omitted > 0)
                sb.AppendLine($"  ({omitted} earlier entries omitted, see saved report for full list)");

            foreach (var err in timelineErrors)
            {
                var coords = (err.Gpc.HasValue && err.Tpc.HasValue && err.Sm.HasValue)
                    ? $"GPC {err.Gpc}, TPC {err.Tpc}, SM {err.Sm}"
                    : "(no SM coords)";
                var errType = err.ErrorType ?? "";
                sb.AppendLine($"  {err.Timestamp:yyyy-MM-dd HH:mm:ss}  ID:{err.EventId}  {coords}  {errType}");
            }
            sb.AppendLine();
            nextSection++;
        }

        // 4. System crashes
        if (crashes != null && crashes.Count > 0)
        {
            var bsods = crashes.Where(c => c.Source == "BSOD").ToList();
            var reboots = crashes.Where(c => c.Source == "REBOOT").ToList();
            var dumps = crashes.Where(c => c.Source == "MINIDUMP").ToList();

            sb.AppendLine($"{nextSection}. SYSTEM CRASHES (BSODs, UNEXPECTED REBOOTS)");
            sb.AppendLine(sep);
            sb.AppendLine($"  Blue Screen crashes (BSOD):     {bsods.Count}");
            sb.AppendLine($"  Unexpected reboots:             {reboots.Count}");
            sb.AppendLine($"  Crash dump files:               {dumps.Count}");
            sb.AppendLine();

            if (reboots.Count > 0)
            {
                var byType = reboots.GroupBy(r =>
                {
                    var m = Regex.Match(r.Description, @"Unexpected reboot: (.+?) \(code");
                    return m.Success ? m.Groups[1].Value : r.Description;
                }).OrderByDescending(g => g.Count());

                sb.AppendLine("  Reboot causes:");
                foreach (var g in byType)
                    sb.AppendLine($"    {g.Key,-50} : {g.Count()}");
                sb.AppendLine();
            }

            if (reboots.Count > 0 && errors.Count > 0)
            {
                int correlated = 0;
                foreach (var reboot in reboots)
                {
                    if (errors.Any(e => Math.Abs((e.Timestamp - reboot.Timestamp).TotalMinutes) < 5))
                        correlated++;
                }
                if (correlated > 0)
                {
                    sb.AppendLine($"  Correlation: {correlated} of {reboots.Count} unexpected reboots occurred");
                    sb.AppendLine("  within 5 minutes of nvlddmkm GPU errors.");
                    sb.AppendLine();
                }
            }

            int showCrashes = maxTimelineEntries > 0 ? Math.Min(maxTimelineEntries, crashes.Count) : crashes.Count;
            sb.AppendLine("  Crash timeline:");
            foreach (var c in ByTime(crashes, c => c.Timestamp, sortDescending).Take(showCrashes))
            {
                var prefix = $"    {c.Timestamp:yyyy-MM-dd HH:mm:ss}  [{c.Source}]  ";
                var desc = c.Description.Trim();
                if (prefix.Length + desc.Length <= 120)
                {
                    sb.AppendLine($"{prefix}{desc}");
                }
                else
                {
                    var sentences = desc.Split(". ", StringSplitOptions.RemoveEmptyEntries);
                    sb.Append(prefix);
                    int lineLen = prefix.Length;
                    for (int i = 0; i < sentences.Length; i++)
                    {
                        var s = sentences[i].Trim().TrimEnd('.') + ".";
                        if (i > 0 && lineLen + s.Length > 120)
                        {
                            sb.AppendLine();
                            sb.Append("      ");
                            lineLen = 6;
                        }
                        sb.Append(s);
                        lineLen += s.Length;
                        if (i < sentences.Length - 1) { sb.Append(' '); lineLen++; }
                    }
                    sb.AppendLine();
                }
            }

            if (dumps.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  Crash dump files (available for analysis):");
                foreach (var d in dumps)
                    sb.AppendLine($"    {d.Description}");
            }

            sb.AppendLine();
            nextSection++;
        }

        // 5. Application crash correlation
        if (appCrashes != null && appCrashes.Count > 0 && errors.Count > 0)
        {
            var correlations = EventLogParser.CorrelateWithAppCrashes(errors, appCrashes);
            if (correlations.Count > 0)
            {
                sb.AppendLine($"{nextSection}. APPLICATION CRASH CORRELATION");
                sb.AppendLine(sep);
                sb.AppendLine($"  {correlations.Count} application crash(es) occurred within 30 seconds of a GPU error:");
                sb.AppendLine();

                var grouped = correlations
                    .GroupBy(c => c.appCrash.Application.ToLowerInvariant())
                    .OrderByDescending(g => g.Count());

                foreach (var g in grouped)
                    sb.AppendLine($"    {g.First().appCrash.Application}: {g.Count()} correlated crash(es)");

                sb.AppendLine();
                int showCount = maxTimelineEntries > 0 ? Math.Min(maxTimelineEntries, correlations.Count) : correlations.Count;
                var orderedCorrelations = ByTime(correlations, c => c.gpuError.Timestamp, sortDescending).Take(showCount);
                foreach (var (gpuErr, appErr, secs) in orderedCorrelations)
                {
                    sb.AppendLine($"    {gpuErr.Timestamp:yyyy-MM-dd HH:mm:ss}  GPU error -> {appErr.Application} ({appErr.FaultingModule}) [{secs:F0}s apart]");
                }

                sb.AppendLine();
                nextSection++;
            }
        }

        // Also list all app crashes even if not correlated
        if (appCrashes != null && appCrashes.Count > 0)
        {
            sb.AppendLine($"{nextSection}. APPLICATION CRASHES ({appCrashes.Count} total)");
            sb.AppendLine(sep);

            var byApp = appCrashes
                .GroupBy(a => a.Application.ToLowerInvariant())
                .OrderByDescending(g => g.Count());

            foreach (var g in byApp)
                sb.AppendLine($"    {g.First().Application}: {g.Count()} crash(es)");

            sb.AppendLine();
            nextSection++;
        }

        // 6. Crash dump analysis
        if (!string.IsNullOrEmpty(dumpAnalysis))
        {
            sb.AppendLine($"{nextSection}. CRASH DUMP ANALYSIS");
            sb.AppendLine(sep);
            sb.AppendLine(dumpAnalysis);
            sb.AppendLine();
            nextSection++;
        }

        // Summary
        sb.AppendLine($"{nextSection}. SUMMARY");
        sb.AppendLine(sep);

        var coordErrors = errors.Where(e => e.Gpc.HasValue).ToList();
        if (coordErrors.Count > 0)
        {
            var uniqueLocs = coordErrors.GroupBy(e => $"GPC {e.Gpc}, TPC {e.Tpc}, SM {e.Sm}").Count();
            var totalSMsConc = gpu.SmCount > 0 ? gpu.SmCount : 170;

            sb.AppendLine($"  {errors.Count} GPU errors recorded in Windows Event Log.");
            sb.AppendLine($"  All errors with SM coordinates originate from {uniqueLocs} specific SM location(s)");
            sb.AppendLine($"  out of {totalSMsConc} total SMs on the GPU.");
            sb.AppendLine();
            sb.AppendLine("  The consistent localization of errors to the same SM(s) across");
            sb.AppendLine("  multiple dates and driver versions may suggest a hardware-level issue");
            sb.AppendLine("  with the affected Streaming Multiprocessor(s).");
        }
        else if (errors.Count > 0)
        {
            sb.AppendLine("  nvlddmkm errors detected but without SM coordinate data.");
        }
        else
        {
            sb.AppendLine("  No errors found in Event Log at this time.");
        }

        sb.AppendLine();
        sb.AppendLine(sep);
        sb.AppendLine($"Report generated by FLARE (Fault Log Analysis & Reboot Examination) on {now:yyyy-MM-dd HH:mm:ss}");

        return sb.ToString();
    }

    public static void Save(string report, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, report);
    }

    /// <summary>
    /// Convert Windows internal driver version (e.g. "31.0.15.8129") to NVIDIA format ("581.29").
    /// Formula: last digit of third part + fourth part -> split as 3.2
    /// </summary>
    internal static string ToNvidiaVersion(string winVer)
    {
        var parts = winVer.Split('.');
        if (parts.Length < 4) return winVer;
        var third = parts[2];
        var fourth = parts[3];
        var combined = third[^1] + fourth; // last digit of third + all of fourth
        if (combined.Length >= 5)
            return $"{combined[..3]}.{combined[3..]}";
        return winVer;
    }

    static List<string> WordWrap(string text, int maxWidth)
    {
        var lines = new List<string>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maxWidth)
            {
                lines.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(word);
        }
        if (current.Length > 0) lines.Add(current.ToString());

        return lines;
    }
}
