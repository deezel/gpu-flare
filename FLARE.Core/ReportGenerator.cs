using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FLARE.Core;

public sealed record ReportInput(
    GpuInfo Gpu,
    SystemInfo? System,
    List<NvlddmkmError> Errors,
    List<SystemCrashEvent>? Crashes = null,
    List<EventLogParser.AppCrashEvent>? AppCrashes = null,
    List<EventLogParser.DriverInstallEvent>? DriverInstalls = null,
    string? DumpAnalysis = null,
    int MaxTimelineEntries = 0,
    bool SortDescending = true,
    bool RedactIdentifiers = false,
    CollectorHealth? Health = null,
    int? DumpsCopiedThisRun = null);

public static class ReportGenerator
{
    const string Sep = "------------------------------------------------------------------------";

    sealed class RenderContext
    {
        public StringBuilder Sb { get; }
        public ReportInput Input { get; }
        public GpuInfo Gpu => Input.Gpu;
        public SystemInfo? System => Input.System;
        public List<NvlddmkmError> Errors => Input.Errors;
        public List<SystemCrashEvent> Crashes { get; }
        public List<EventLogParser.AppCrashEvent> AppCrashes { get; }
        public List<EventLogParser.DriverInstallEvent> DriverInstalls { get; }
        public string? DumpAnalysis => Input.DumpAnalysis;
        public int MaxTimelineEntries => Input.MaxTimelineEntries;
        public bool SortDescending => Input.SortDescending;
        public bool RedactIdentifiers => Input.RedactIdentifiers;
        public int Section { get; set; } = 1;
        public DateTime Now { get; }

        public CollectorHealth? Health => Input.Health;
        public CollectionTruncation? Truncation => Input.Health?.Truncation;
        public bool HasFailure(string source) =>
            Health?.Notices.Any(n => n.Kind == CollectorNoticeKind.Failure && n.Source == source) == true;

        public bool GpuErrorsCapped => Truncation?.GpuErrorsResultCap == true;
        public EventLogRetentionInfo? SystemEventLog => Health?.SystemEventLog;
        public EventLogRetentionInfo? ApplicationEventLog => Health?.ApplicationEventLog;
        public DateTime? RequestedWindowStart =>
            Truncation?.RequestedMaxDays > 0 ? Now.AddDays(-Truncation.RequestedMaxDays) : null;
        public bool SystemLogCutsIntoRequestedWindow =>
            RequestedWindowStart is DateTime requested &&
            SystemEventLog?.OldestRecordTimestamp is DateTime oldest &&
            oldest > requested;
        public bool ApplicationLogCutsIntoRequestedWindow =>
            RequestedWindowStart is DateTime requested &&
            ApplicationEventLog?.OldestRecordTimestamp is DateTime oldest &&
            oldest > requested;
        public string GpuCapSuffix => GpuErrorsCapped ? " (capped — see SCOPE block)" : "";

        private ReportAnalysis.ErrorConcentration? _concentration;
        public ReportAnalysis.ErrorConcentration Concentration =>
            _concentration ??= ReportAnalysis.ComputeConcentration(Errors, Gpu);

        public RenderContext(StringBuilder sb, ReportInput input)
        {
            Sb = sb;
            Input = input;
            Crashes = input.Crashes ?? [];
            AppCrashes = input.AppCrashes ?? [];
            DriverInstalls = input.DriverInstalls ?? [];
            Now = DateTime.Now;
        }

        public string Redact(string value) => RedactIdentifiers ? ReportRedaction.RedactedMark : value;

        public string ScrubPath(string value) => RedactIdentifiers ? ReportRedaction.ScrubUserPaths(value) : value;

        public IEnumerable<T> ByTime<T>(IEnumerable<T> items, Func<T, DateTime> key) =>
            SortDescending ? items.OrderByDescending(key) : items.OrderBy(key);
    }

    public static string Generate(ReportInput input)
    {
        var ctx = new RenderContext(new StringBuilder(), input);

        RenderHeader(ctx);
        RenderScope(ctx);
        RenderGpuIdentification(ctx);
        RenderSystemIdentification(ctx);
        RenderErrorSummary(ctx);
        RenderFrequencyChart(ctx);
        RenderDriverInstallHistory(ctx);
        RenderErrorTimeline(ctx);
        RenderSystemCrashes(ctx);
        RenderAppCrashCorrelation(ctx);
        RenderAllAppCrashes(ctx);
        RenderDumpAnalysis(ctx);
        RenderSummary(ctx);
        RenderFooter(ctx);

        var result = ctx.Sb.ToString();

        // Parity with the log's RedactAll: per-field redaction above covers fields FLARE
        // owns, this catches machine-name / user-path strings that leaked through event-log
        // or cdb .Description text we emit verbatim. Idempotent on already-scrubbed markers.
        return ctx.RedactIdentifiers
            ? ReportRedaction.RedactAll(result, Environment.MachineName)
            : result;
    }

    static void AppendSectionHeader(RenderContext ctx, string title)
    {
        ctx.Sb.AppendLine($"{ctx.Section++}. {title}");
        ctx.Sb.AppendLine(Sep);
    }

    static void RenderScope(RenderContext ctx)
    {
        var h = ctx.Health;
        if (h == null) return;

        var t = h.Truncation;
        bool hasWindow = t.RequestedMaxDays > 0;
        bool hasEventLogRetention = h.SystemEventLog != null || h.ApplicationEventLog != null;
        bool hasCaps = t.Any;
        bool hasNotices = h.Notices.Count > 0;

        if (!hasWindow && !hasEventLogRetention && !hasCaps && !hasNotices) return;

        var sb = ctx.Sb;
        sb.AppendLine("SCOPE OF THIS REPORT");
        sb.AppendLine(Sep);

        if (hasWindow)
            sb.AppendLine($"  Requested window: last {t.RequestedMaxDays} day(s).");

        var renderedRetention = false;
        if (h.SystemEventLog != null)
        {
            if (hasWindow) sb.AppendLine();
            RenderSystemEventLogRetention(ctx, h.SystemEventLog);
            renderedRetention = true;
        }

        if (h.ApplicationEventLog != null)
        {
            if (hasWindow || renderedRetention) sb.AppendLine();
            RenderApplicationEventLogRetention(ctx, h.ApplicationEventLog);
        }

        if (hasCaps)
        {
            if (hasWindow || hasEventLogRetention) sb.AppendLine();
            sb.AppendLine("  One or more collectors hit their per-run caps — the sections below are");
            sb.AppendLine("  based on a *subset* of the events in the requested window. Narrow Max Days,");
            sb.AppendLine("  or raise the relevant cap in code, and re-run for a complete view.");
            sb.AppendLine();
            sb.AppendLine("  Capped sources:");
            if (t.GpuErrorsResultCap)
                sb.AppendLine($"    - nvlddmkm errors: Max Events cap ({t.MaxEventsCap}) reached.");
            if (t.BsodResultCap)
                sb.AppendLine($"    - BSOD events (WER 1001): {EventLogParser.BsodCap} cap reached.");
            if (t.RebootResultCap)
                sb.AppendLine($"    - Unexpected reboots (Kernel-Power 41): {EventLogParser.RebootCap} cap reached.");
            if (t.AppCrashesResultCap)
                sb.AppendLine($"    - Application crashes/hangs: {EventLogParser.AppCrashCap} cap reached.");
        }

        if (hasNotices)
        {
            if (hasWindow || hasEventLogRetention || hasCaps) sb.AppendLine();
            sb.AppendLine("  Collector health:");
            sb.AppendLine("  A collector could not run, was skipped, or read a value whose format did not");
            sb.AppendLine("  match what FLARE expected. Sections below may be partial or missing. Surfaced");
            sb.AppendLine("  here so a reader of the saved report — not just the run log — can see what");
            sb.AppendLine("  the pipeline skipped.");
            foreach (var n in h.Notices)
            {
                var label = n.Kind switch
                {
                    CollectorNoticeKind.Canary => "format-drift",
                    CollectorNoticeKind.Skipped => "skipped",
                    _ => "failed",
                };
                var source = ctx.RedactIdentifiers ? RedactNotice(n.Source) : n.Source;
                var message = ctx.RedactIdentifiers ? RedactNotice(n.Message) : n.Message;
                AppendWrappedNotice(sb, $"    - [{label}] {source}: ", message);
            }
        }

        sb.AppendLine();
    }

    static void RenderSystemEventLogRetention(RenderContext ctx, EventLogRetentionInfo retention)
    {
        var sb = ctx.Sb;
        sb.AppendLine("  System Event Log retention:");

        AppendEventLogDetails(sb, retention);

        if (retention.OldestRecordTimestamp.HasValue)
            sb.AppendLine($"    - Oldest retained System record: {retention.OldestRecordTimestamp.Value:yyyy-MM-dd HH:mm:ss}.");
        else
            sb.AppendLine("    - Oldest retained System record: unavailable.");

        if (retention.OldestRelevantEventTimestamp.HasValue)
            sb.AppendLine($"    - Oldest retained nvlddmkm 13/14/153 record: {retention.OldestRelevantEventTimestamp.Value:yyyy-MM-dd HH:mm:ss}.");
        else
            sb.AppendLine("    - No retained nvlddmkm 13/14/153 records were found.");

        if (ctx.SystemLogCutsIntoRequestedWindow &&
            ctx.RequestedWindowStart.HasValue &&
            retention.OldestRecordTimestamp.HasValue)
        {
            sb.AppendLine($"    - Requested window starts {ctx.RequestedWindowStart.Value:yyyy-MM-dd HH:mm:ss};");
            sb.AppendLine($"      System records before {retention.OldestRecordTimestamp.Value:yyyy-MM-dd HH:mm:ss} are not available in the live log.");
            sb.AppendLine("      Absence before that timestamp is not evidence that no GPU errors occurred.");
        }
    }

    static void RenderApplicationEventLogRetention(RenderContext ctx, EventLogRetentionInfo retention)
    {
        var sb = ctx.Sb;
        var relevantDescription = string.IsNullOrWhiteSpace(retention.OldestRelevantEventDescription)
            ? "Application Error 1000 / Application Hang 1002"
            : retention.OldestRelevantEventDescription;

        sb.AppendLine("  Application Event Log retention:");

        AppendEventLogDetails(sb, retention);

        if (retention.OldestRecordTimestamp.HasValue)
            sb.AppendLine($"    - Oldest retained Application record: {retention.OldestRecordTimestamp.Value:yyyy-MM-dd HH:mm:ss}.");
        else
            sb.AppendLine("    - Oldest retained Application record: unavailable.");

        if (retention.OldestRelevantEventTimestamp.HasValue)
            sb.AppendLine($"    - Oldest retained {relevantDescription} record: {retention.OldestRelevantEventTimestamp.Value:yyyy-MM-dd HH:mm:ss}.");
        else
            sb.AppendLine($"    - No retained {relevantDescription} records were found.");

        if (ctx.ApplicationLogCutsIntoRequestedWindow &&
            ctx.RequestedWindowStart.HasValue &&
            retention.OldestRecordTimestamp.HasValue)
        {
            sb.AppendLine($"    - Requested window starts {ctx.RequestedWindowStart.Value:yyyy-MM-dd HH:mm:ss};");
            sb.AppendLine($"      Application records before {retention.OldestRecordTimestamp.Value:yyyy-MM-dd HH:mm:ss} are not available in the live log.");
            sb.AppendLine("      Absence before that timestamp is not evidence that no application crash/hang events occurred.");
        }
    }

    static void AppendEventLogDetails(StringBuilder sb, EventLogRetentionInfo retention)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(retention.LogMode))
            details.Add($"mode {retention.LogMode}");
        if (retention.MaximumSizeInBytes.HasValue)
            details.Add($"max {FormatByteSize(retention.MaximumSizeInBytes.Value)}");
        if (retention.FileSizeBytes.HasValue)
            details.Add($"current {FormatByteSize(retention.FileSizeBytes.Value)}");
        if (retention.RecordCount.HasValue)
            details.Add($"{retention.RecordCount.Value.ToString("N0", CultureInfo.InvariantCulture)} record(s)");

        if (details.Count > 0)
            sb.AppendLine($"    - {retention.LogName} log: {string.Join("; ", details)}.");
        else
            sb.AppendLine($"    - {retention.LogName} log: retention settings unavailable.");
    }

    static string FormatByteSize(long bytes)
    {
        if (bytes >= 1024 * 1024)
            return string.Format(CultureInfo.InvariantCulture, "{0:F1} MiB", bytes / 1024d / 1024d);
        if (bytes >= 1024)
            return string.Format(CultureInfo.InvariantCulture, "{0:F1} KiB", bytes / 1024d);
        return string.Format(CultureInfo.InvariantCulture, "{0} bytes", bytes);
    }

    static string RedactNotice(string value) =>
        ReportRedaction.RedactAll(value, Environment.MachineName);

    static void AppendWrappedNotice(StringBuilder sb, string prefix, string message, int width = 100)
    {
        var words = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            sb.AppendLine(prefix.TrimEnd());
            return;
        }

        var continuationPrefix = "      ";
        var line = new StringBuilder(prefix);

        foreach (var word in words)
        {
            var separator = line.Length == prefix.Length ? "" : " ";
            if (line.Length + separator.Length + word.Length > width && line.Length > prefix.Length)
            {
                sb.AppendLine(line.ToString());
                line.Clear();
                line.Append(continuationPrefix).Append(word);
            }
            else
            {
                line.Append(separator).Append(word);
            }
        }

        sb.AppendLine(line.ToString());
    }

    static void RenderHeader(RenderContext ctx)
    {
        var sb = ctx.Sb;
        sb.AppendLine("GPU Error Analysis Report");
        sb.AppendLine($"Generated: {ctx.Now:yyyy-MM-dd HH:mm:ss}");
        if (ctx.RedactIdentifiers)
        {
            sb.AppendLine("Note: UUID and computer name have been redacted; Windows user-profile paths");
            sb.AppendLine("      have been rewritten to %USERPROFILE%. Process names, driver/module names, and stack frames are PRESERVED —");
            sb.AppendLine("      without them the report has no diagnostic value.");
        }
        else
        {
            sb.AppendLine("Note: This report contains the computer name, GPU UUID, and may reference Windows user");
            sb.AppendLine("      paths. Review before sharing publicly, or re-run with redaction on.");
        }
        if (ctx.Gpu.NvidiaDeviceCount > 1)
        {
            sb.AppendLine($"Warning: {ctx.Gpu.NvidiaDeviceCount} NVIDIA GPUs detected. This report covers only the first");
            sb.AppendLine($"         ({ctx.Gpu.Name}, {ctx.Gpu.PciId}). Event log and crash data are system-wide and may");
            sb.AppendLine("         include errors originating from the other adapter(s).");
        }
        sb.AppendLine(Sep);
        sb.AppendLine();
    }

    static void RenderGpuIdentification(RenderContext ctx)
    {
        var sb = ctx.Sb;
        var gpu = ctx.Gpu;

        AppendSectionHeader(ctx, "GPU IDENTIFICATION");

        var hasPrimaryIdentity =
            !string.IsNullOrWhiteSpace(gpu.Name) ||
            !string.IsNullOrWhiteSpace(gpu.DriverVersion) ||
            !string.IsNullOrWhiteSpace(gpu.Uuid) ||
            !string.IsNullOrWhiteSpace(gpu.PciId);

        if (!hasPrimaryIdentity)
        {
            var scopeHint = ctx.Health?.Notices.Count > 0
                ? "; see SCOPE block for collector status"
                : "";
            sb.AppendLine($"  (GPU identification unavailable{scopeHint}.)");
        }

        AppendGpuField(sb, "GPU", gpu.Name);
        AppendGpuField(sb, "Driver", gpu.DriverVersion);
        AppendGpuField(sb, "VBIOS", gpu.VbiosVersion);
        AppendGpuField(sb, "UUID", string.IsNullOrWhiteSpace(gpu.Uuid) ? "" : ctx.Redact(gpu.Uuid));
        AppendGpuField(sb, "PCI Bus", gpu.PciId);
        AppendGpuField(sb, "SMs", gpu.SmCount);
        AppendGpuField(sb, "Memory", gpu.MemoryTotal);
        if (gpu.PcieMaxGen > 0 && gpu.PcieCurrentGen > 0)
        {
            bool widthKnown = gpu.PcieMaxWidth > 0 && gpu.PcieCurrentWidth > 0;
            bool belowMax = gpu.PcieCurrentGen < gpu.PcieMaxGen ||
                            (widthKnown && gpu.PcieCurrentWidth < gpu.PcieMaxWidth);
            var mark = belowMax ? "  [LOWER AT SAMPLE]" : "";
            sb.AppendLine($"  PCIe Gen:       {gpu.PcieCurrentGen} (max {gpu.PcieMaxGen}){mark}");
            if (widthKnown)
                sb.AppendLine($"  PCIe Width:     x{gpu.PcieCurrentWidth} (max x{gpu.PcieMaxWidth})");
            if (belowMax)
            {
                sb.AppendLine("                  Note: this is a sample-time link state. NVIDIA GPUs commonly");
                sb.AppendLine("                  downshift PCIe generation/width while idle for power management.");
                sb.AppendLine("                  Treat as a fault only if the link remains below capability under load.");
            }
        }
        if (gpu.Bar1TotalMib > 0)
        {
            // BAR1 ≥ 1 GiB = Resizable BAR enabled; default cap is ~256 MiB.
            var rebar = gpu.Bar1TotalMib >= 1024 ? "enabled" : "not enabled (default ~256 MiB)";
            sb.AppendLine($"  BAR1 Memory:    {gpu.Bar1TotalMib} MiB  (Resizable BAR: {rebar})");
        }
        sb.AppendLine($"  OS:             {Environment.OSVersion}");
        sb.AppendLine($"  Computer:       {ctx.Redact(Environment.MachineName)}");
        sb.AppendLine();
    }

    static void AppendGpuField(StringBuilder sb, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            sb.AppendLine($"  {label + ":",-15} {value}");
    }

    static void AppendGpuField(StringBuilder sb, string label, int value)
    {
        if (value > 0)
            sb.AppendLine($"  {label + ":",-15} {value}");
    }

    static void RenderSystemIdentification(RenderContext ctx)
    {
        if (ctx.System == null) return;

        var sb = ctx.Sb;
        var system = ctx.System;
        AppendSectionHeader(ctx, "SYSTEM IDENTIFICATION");
        sb.AppendLine($"  System:         {system.SystemManufacturer} {system.SystemProductName}".TrimEnd());
        sb.AppendLine($"  Motherboard:    {system.BoardManufacturer} {system.BoardProduct} (rev {system.BoardVersion})".Replace(" (rev )", ""));
        sb.AppendLine($"  BIOS:           {system.BiosVendor} {system.BiosVersion}".TrimEnd());
        sb.AppendLine($"  BIOS Date:      {system.BiosReleaseDate}");
        sb.AppendLine($"  CPU:            {system.ProcessorName}");
        sb.AppendLine($"  RAM:            {system.TotalMemoryFormatted}");
        sb.AppendLine();
    }

    static void RenderErrorSummary(RenderContext ctx)
    {
        var sb = ctx.Sb;
        var errors = ctx.Errors;
        var gpu = ctx.Gpu;

        AppendSectionHeader(ctx, "NVLDDMKM ERROR SUMMARY");

        if (errors.Count == 0)
        {
            if (ctx.HasFailure("Event Log: nvlddmkm"))
                sb.AppendLine("  No nvlddmkm errors were collected because the Event Log collector failed. See SCOPE block.");
            else if (ctx.SystemLogCutsIntoRequestedWindow)
                sb.AppendLine("  No retained nvlddmkm errors found in Windows Event Log. See SCOPE block for retained range.");
            else
                sb.AppendLine("  No nvlddmkm errors found in Windows Event Log.");
        }
        else
        {
            sb.AppendLine($"  Total errors:   {errors.Count}{ctx.GpuCapSuffix}");
            if (ctx.GpuErrorsCapped)
                sb.AppendLine($"  Collected range: {errors.First().Timestamp:yyyy-MM-dd} to {errors.Last().Timestamp:yyyy-MM-dd}");
            else
                sb.AppendLine($"  Date range:     {errors.First().Timestamp:yyyy-MM-dd} to {errors.Last().Timestamp:yyyy-MM-dd}");
            if (ctx.SystemLogCutsIntoRequestedWindow && ctx.SystemEventLog?.OldestRecordTimestamp is DateTime oldest)
                sb.AppendLine($"  Retention floor: System log starts {oldest:yyyy-MM-dd}; older requested days are unavailable.");
            sb.AppendLine();

            var concentration = ctx.Concentration;

            if (concentration.ErrorsWithCoords > 0)
            {
                sb.AppendLine("  Errors by SM location:");
                foreach (var loc in concentration.LocationsByFrequency)
                {
                    var pct = (double)loc.Count / concentration.ErrorsWithCoords * 100;
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "    {0}: {1} errors ({2:F1}%)", loc.Key, loc.Count, pct));
                }

                sb.AppendLine();

                sb.AppendLine("  Analysis:");
                // singleGpu gate: on a multi-GPU box, one adapter's SM count next to
                // system-wide event counts invites the very misread we guard against below.
                if (concentration.SmCountKnown && concentration.SingleGpu)
                    sb.AppendLine($"    Total SMs on this GPU: {gpu.SmCount}");
                sb.AppendLine($"    SM locations with errors: {concentration.UniqueLocations}");
                sb.AppendLine($"    Errors with SM coordinates: {concentration.ErrorsWithCoords}");

                if (!concentration.SingleGpu)
                {
                    sb.AppendLine();
                    sb.AppendLine($"    Concentration analysis suppressed: {gpu.NvidiaDeviceCount} NVIDIA GPUs detected.");
                    sb.AppendLine("    Event coordinates cannot be attributed to a specific adapter.");
                }
                else if (!concentration.SmCountKnown)
                {
                    sb.AppendLine("    SM count unavailable; concentration analysis omitted.");
                }
                else if (concentration.SuggestsLocalizedFailure)
                {
                    var affectedPct = concentration.AffectedSmFraction * 100.0;
                    sb.AppendLine();
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "    All {0} coordinate-tagged errors fall on {1} of {2} SM(s) ({3:F1}% of the GPU).",
                        concentration.ErrorsWithCoords, concentration.UniqueLocations, gpu.SmCount, affectedPct));
                    sb.AppendLine("    This is a tight recurring cluster in the collected events.");
                    if (ctx.GpuErrorsCapped)
                    {
                        sb.AppendLine("    Max Events cap was reached — older events in the requested window were");
                        sb.AppendLine("    not read, so the full history may include errors on other SMs.");
                    }
                    sb.AppendLine($"    Treat it as a troubleshooting lead, not a conclusion.");
                }
                else if (concentration.IsWeakSingleGpuSignal)
                {
                    sb.AppendLine();
                    sb.AppendLine($"    {concentration.ErrorsWithCoords} coordinate-tagged error(s) across {concentration.UniqueLocations} SM location(s) of {gpu.SmCount}.");
                    if (concentration.Verdict == ReportAnalysis.LocalizationVerdict.SmallSample)
                    {
                        sb.AppendLine($"    Sample below the {ReportAnalysis.StrongEvidenceMinErrors}-error threshold needed to distinguish localization");
                        sb.AppendLine($"    from coincidence — treat as an anecdotal signal, not a conclusion.");
                    }
                    else if (concentration.Verdict == ReportAnalysis.LocalizationVerdict.TooManyLocations)
                    {
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "    Repetition is present, but it spans too many SMs ({0:F1}% of the GPU)",
                            concentration.AffectedSmFraction * 100.0));
                        sb.AppendLine($"    to describe as a tight localized cluster (strong path requires <= {ReportAnalysis.StrongEvidenceMaxLocations} SMs");
                        sb.AppendLine($"    and <= {ReportAnalysis.StrongEvidenceMaxSmFraction * 100.0:F0}% of the GPU) — treat as a clustered signal, not a conclusion.");
                    }
                    else
                    {
                        sb.AppendLine($"    Errors spread across too many SMs (below the {ReportAnalysis.StrongEvidenceMinErrorsPerLocation:F0}-errors-per-location");
                        sb.AppendLine("    threshold needed to distinguish localization from coincidence) —");
                        sb.AppendLine("    treat as an anecdotal signal, not a conclusion.");
                    }
                }
            }

            if (concentration.ErrorsWithoutCoords > 0)
                sb.AppendLine($"\n  Errors without SM coordinates: {concentration.ErrorsWithoutCoords}");

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
    }

    static void RenderFrequencyChart(RenderContext ctx)
    {
        var errors = ctx.Errors;
        if (errors.Count < 2) return;

        var sb = ctx.Sb;
        AppendSectionHeader(ctx, "ERROR FREQUENCY (per week)");

        if (ctx.GpuErrorsCapped)
        {
            sb.AppendLine("  Note: nvlddmkm events were capped this run — weekly counts are a lower");
            sb.AppendLine("  bound and the earliest weeks may be missing entirely. See SCOPE block.");
            sb.AppendLine();
        }

        var byWeek = ctx.ByTime(
                errors.GroupBy(e => {
                    var d = e.Timestamp.Date;
                    return d.AddDays(-(int)d.DayOfWeek); // week start (Sunday)
                }),
                g => g.Key)
            .ToList();

        int maxCount = byWeek.Max(g => g.Count());
        const int barWidth = 40;

        var sortedDrivers = ctx.DriverInstalls
            .OrderBy(d => d.Timestamp)
            .ToList();

        foreach (var week in byWeek)
        {
            int barLen = maxCount > 0 ? (int)Math.Ceiling((double)week.Count() / maxCount * barWidth) : 0;
            var bar = new string('#', barLen);
            var weekLabel = week.Key.ToString("yyyy-MM-dd");
            var countStr = week.Count().ToString().PadLeft(5);

            string driverNote = "";
            if (sortedDrivers.Count > 0)
            {
                var weekStart = week.Key;
                var weekEnd = week.Key.AddDays(7);

                // Oldest-first so "A > B" reads as "upgraded from A to B".
                var thisWeek = sortedDrivers
                    .Where(d => d.Timestamp >= weekStart && d.Timestamp < weekEnd)
                    .Select(d => NvidiaDriverVersion.ToNvidiaVersion(d.DriverVersion))
                    .Distinct()
                    .ToList();

                if (thisWeek.Count > 0)
                {
                    driverNote = $"  (drv {string.Join(" > ", thisWeek)})";
                }
                else
                {
                    var prior = sortedDrivers.LastOrDefault(d => d.Timestamp < weekStart);
                    if (prior != null)
                        driverNote = $"  (drv {NvidiaDriverVersion.ToNvidiaVersion(prior.DriverVersion)})";
                }
            }

            sb.AppendLine($"    {weekLabel} {countStr} |{bar}{driverNote}");
        }

        if (sortedDrivers.Count == 0)
            sb.AppendLine("  (No driver install events matched; chart annotations unavailable.)");

        sb.AppendLine();
    }

    static void RenderDriverInstallHistory(RenderContext ctx)
    {
        var driverInstalls = ctx.DriverInstalls;
        if (driverInstalls.Count == 0) return;

        var sb = ctx.Sb;
        AppendSectionHeader(ctx, "DRIVER INSTALL HISTORY");

        foreach (var d in ctx.ByTime(driverInstalls, d => d.Timestamp))
            sb.AppendLine($"    {d.Timestamp:yyyy-MM-dd HH:mm:ss}  {NvidiaDriverVersion.ToNvidiaVersion(d.DriverVersion)} ({d.DriverVersion})");

        if (ctx.Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  Errors per install period:");
            sb.AppendLine("  Counts include all collected nvlddmkm events in the window (IDs 13/14/153),");
            sb.AppendLine("  whether or not SM coordinates were present.");
            if (ctx.Gpu.NvidiaDeviceCount > 1)
            {
                sb.AppendLine($"  Note: {ctx.Gpu.NvidiaDeviceCount} NVIDIA adapters detected; per-period counts are");
                sb.AppendLine("  system-wide and cannot be attributed to a specific adapter.");
            }
            if (ctx.Truncation?.GpuErrorsResultCap == true)
            {
                sb.AppendLine("  Note: nvlddmkm events were capped; per-period error counts are a lower");
                sb.AppendLine("  bound. See SCOPE block.");
            }
            var retentionFloor = ctx.SystemEventLog?.OldestRecordTimestamp;
            if (retentionFloor.HasValue && driverInstalls.Any(d => d.Timestamp < retentionFloor.Value))
            {
                sb.AppendLine("  Note: setupapi may preserve driver installs older than the live System log.");
                sb.AppendLine($"  Periods before {retentionFloor.Value:yyyy-MM-dd HH:mm:ss} cannot prove zero nvlddmkm errors.");
            }

            var buckets = ComputeDriverPeriodBuckets(ctx.Errors, driverInstalls);

            DateTime? capBlindBefore = ctx.Truncation?.GpuErrorsResultCap == true && ctx.Errors.Count > 0
                ? ctx.Errors.Min(e => e.Timestamp)
                : null;

            var rows = new List<string>();
            foreach (var b in buckets)
                rows.Add(FormatDriverPeriodRow(b, retentionFloor, capBlindBefore));

            if (ctx.SortDescending) rows.Reverse();
            foreach (var row in rows) sb.AppendLine(row);
        }

        sb.AppendLine();
    }

    static void RenderErrorTimeline(RenderContext ctx)
    {
        var errors = ctx.Errors;
        if (errors.Count == 0) return;

        var sb = ctx.Sb;
        int showCount = ctx.MaxTimelineEntries > 0 ? ctx.MaxTimelineEntries : errors.Count;
        var timelineErrors = ctx.ByTime(errors, e => e.Timestamp).Take(showCount).ToList();
        int omitted = errors.Count - timelineErrors.Count;

        AppendSectionHeader(ctx, $"ERROR TIMELINE ({timelineErrors.Count} of {errors.Count} entries)");
        if (omitted > 0)
            sb.AppendLine($"  ({omitted} entries omitted by MaxTimelineEntries cap; raise or clear the cap to see them.)");

        foreach (var err in timelineErrors)
        {
            var coords = (err.Gpc.HasValue && err.Tpc.HasValue && err.Sm.HasValue)
                ? $"GPC {err.Gpc}, TPC {err.Tpc}, SM {err.Sm}"
                : "(no SM coords)";
            var errType = err.ErrorType ?? "";
            sb.AppendLine($"  {err.Timestamp:yyyy-MM-dd HH:mm:ss}  ID:{err.EventId}  {coords}  {errType}");
        }
        sb.AppendLine();
    }

    static void RenderSystemCrashes(RenderContext ctx)
    {
        var crashes = ctx.Crashes;
        if (crashes.Count == 0) return;

        var sb = ctx.Sb;
        var errors = ctx.Errors;
        var bsods = crashes.Where(c => c.Source == "BSOD").ToList();
        var reboots = crashes.Where(c => c.Source == "REBOOT").ToList();
        var dumps = crashes.Where(c => c.Source == "MINIDUMP").ToList();

        AppendSectionHeader(ctx, "SYSTEM CRASHES (BSODs, UNEXPECTED REBOOTS)");
        var bsodSuffix    = ctx.Truncation?.BsodResultCap   == true ? " (capped — see SCOPE block)" : "";
        var rebootSuffix  = ctx.Truncation?.RebootResultCap == true ? " (capped — see SCOPE block)" : "";
        sb.AppendLine($"  Blue Screen crashes (BSOD):     {bsods.Count}{bsodSuffix}");
        sb.AppendLine($"  Unexpected reboots:             {reboots.Count}{rebootSuffix}");
        sb.AppendLine($"  Crash dump files:               {dumps.Count}");
        sb.AppendLine();

        if (reboots.Count > 0)
        {
            var byType = reboots.GroupBy(r =>
            {
                var m = Regex.Match(r.Description, @"^Unexpected reboot: (.+) \(code [^)]+\)$");
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
                if (errors.Any(e => Math.Abs((e.Timestamp - reboot.Timestamp).TotalMinutes) < RebootCorrelationWindowMinutes))
                    correlated++;
            }
            if (correlated > 0)
            {
                sb.AppendLine($"  Timing proximity: {correlated} of {reboots.Count} unexpected reboots landed within");
                sb.AppendLine($"  {RebootCorrelationWindowMinutes} minutes of an nvlddmkm GPU error. Treat as a timing hint, not a cause — a");
                sb.AppendLine($"  {RebootCorrelationWindowMinutes}-minute window will catch some coincidences on systems with many of either.");
                if (ctx.Truncation?.RebootResultCap == true ||
                    ctx.Truncation?.GpuErrorsResultCap == true)
                {
                    sb.AppendLine("  Note: one or more input sources hit their cap — the matched count is a");
                    sb.AppendLine("  lower bound, not a total.");
                }
                sb.AppendLine();
            }
        }

        int showCrashes = ctx.MaxTimelineEntries > 0 ? Math.Min(ctx.MaxTimelineEntries, crashes.Count) : crashes.Count;
        int omittedCrashes = crashes.Count - showCrashes;
        sb.AppendLine("  Crash timeline:");
        if (omittedCrashes > 0)
            sb.AppendLine($"  ({omittedCrashes} entries omitted by MaxTimelineEntries cap; raise or clear the cap to see them.)");
        foreach (var c in ctx.ByTime(crashes, c => c.Timestamp).Take(showCrashes))
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
    }

    static void RenderAppCrashCorrelation(RenderContext ctx)
    {
        var appCrashes = ctx.AppCrashes;
        var errors = ctx.Errors;
        if (appCrashes.Count == 0 || errors.Count == 0) return;

        var correlations = EventLogParser.CorrelateWithAppCrashes(errors, appCrashes);
        if (correlations.Count == 0) return;

        var sb = ctx.Sb;
        AppendSectionHeader(ctx, "APPLICATION CRASH CORRELATION");

        // One app crash inside a burst of N GPU errors fans out to N pairs;
        // distinct counts are what the reader wants, pair count just makes the table add up.
        var distinctAppCrashes = correlations
            .Select(c => (c.appCrash.Timestamp, c.appCrash.Application.ToLowerInvariant()))
            .Distinct()
            .Count();
        var distinctGpuErrors = correlations
            .Select(c => c.gpuError.Timestamp)
            .Distinct()
            .Count();

        sb.AppendLine($"  {distinctAppCrashes} application crash(es) matched {distinctGpuErrors} GPU error(s) within 30 seconds, producing {correlations.Count} correlation pair(s):");
        sb.AppendLine("  Treat as a timing hint, not a cause — a 30-second window will catch some");
        sb.AppendLine("  coincidences on systems with many of either.");
        if (ctx.Truncation?.GpuErrorsResultCap == true)
        {
            sb.AppendLine("  Note: nvlddmkm event read hit Max Events cap — matched counts are a lower");
            sb.AppendLine("  bound, not a total. See SCOPE block.");
        }
        sb.AppendLine();

        // Distinct crashes per app (not pairs) so game.exe inside a burst isn't "5 crashes".
        var grouped = correlations
            .GroupBy(c => c.appCrash.Application.ToLowerInvariant())
            .Select(g => new
            {
                Display = ctx.ScrubPath(g.First().appCrash.Application),
                Crashes = g.Select(c => c.appCrash.Timestamp).Distinct().Count(),
                Pairs = g.Count()
            })
            .OrderByDescending(g => g.Crashes);

        foreach (var g in grouped)
            sb.AppendLine($"    {g.Display}: {g.Crashes} crash(es), {g.Pairs} correlation pair(s)");

        sb.AppendLine();
        int showCount = ctx.MaxTimelineEntries > 0
            ? Math.Min(ctx.MaxTimelineEntries, correlations.Count)
            : Math.Min(DefaultPairRowCap, correlations.Count);
        int omitted = correlations.Count - showCount;
        if (omitted > 0)
            sb.AppendLine($"    ({omitted} further pair(s) omitted; distinct crash + error counts above are complete.)");
        var orderedCorrelations = ctx.ByTime(correlations, c => c.gpuError.Timestamp).Take(showCount);
        foreach (var (gpuErr, appErr, secs) in orderedCorrelations)
        {
            var seconds = secs.ToString("F0", CultureInfo.InvariantCulture);
            var relation = appErr.Timestamp >= gpuErr.Timestamp
                ? $"{seconds}s after GPU error"
                : $"{seconds}s before GPU error";
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "    {0:yyyy-MM-dd HH:mm:ss}  GPU error; {1:yyyy-MM-dd HH:mm:ss}  {2} ({3}) [{4}]",
                gpuErr.Timestamp, appErr.Timestamp, ctx.ScrubPath(appErr.Application), ctx.ScrubPath(appErr.FaultingModule), relation));
        }

        sb.AppendLine();
    }

    internal const int DefaultPairRowCap = 100;
    internal const int RebootCorrelationWindowMinutes = 5;

    static void RenderAllAppCrashes(RenderContext ctx)
    {
        var appCrashes = ctx.AppCrashes;
        if (appCrashes.Count == 0) return;

        var sb = ctx.Sb;
        AppendSectionHeader(ctx, $"APPLICATION CRASHES ({appCrashes.Count} total)");
        sb.AppendLine("  All application crashes in the window, not just GPU-related. The correlation");
        sb.AppendLine("  table above is the GPU-filtered view.");

        var byApp = appCrashes
            .GroupBy(a => a.Application.ToLowerInvariant())
            .OrderByDescending(g => g.Count());

        foreach (var g in byApp)
            sb.AppendLine($"    {ctx.ScrubPath(g.First().Application)}: {g.Count()} crash(es)");

        sb.AppendLine();
    }

    static void RenderDumpAnalysis(RenderContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.DumpAnalysis)) return;

        var sb = ctx.Sb;
        AppendSectionHeader(ctx, "CRASH DUMP ANALYSIS");
        if (ctx.Input.DumpsCopiedThisRun.HasValue)
        {
            var n = ctx.Input.DumpsCopiedThisRun.Value;
            sb.AppendLine(n == 0
                ? "  No new dumps copied from system folder this run; any dump(s) below were staged by an earlier run."
                : $"  {n} new dump(s) copied from system folder this run; older staged dump(s) may also appear below.");
            sb.AppendLine();
        }
        sb.AppendLine(ctx.RedactIdentifiers ? ReportRedaction.RedactCdbSummary(ctx.DumpAnalysis) : ctx.DumpAnalysis);
        sb.AppendLine();
    }

    static void RenderSummary(RenderContext ctx)
    {
        var sb = ctx.Sb;
        var errors = ctx.Errors;
        var gpu = ctx.Gpu;

        AppendSectionHeader(ctx, "SUMMARY");

        var concentration = ctx.Concentration;

        if (concentration.ErrorsWithCoords > 0)
        {
            sb.AppendLine($"  {errors.Count} GPU errors recorded in Windows Event Log.{ctx.GpuCapSuffix}");
            if (concentration.SmCountKnown && concentration.SingleGpu)
            {
                sb.AppendLine($"  All errors with SM coordinates originate from {concentration.UniqueLocations} specific SM location(s)");
                sb.AppendLine($"  out of {gpu.SmCount} total SMs on the GPU.");
            }
            else
            {
                sb.AppendLine($"  All errors with SM coordinates originate from {concentration.UniqueLocations} specific SM location(s).");
            }

            // Multi-GPU: coords may belong to a different adapter than the one reported above,
            // so the localization conclusion is suppressed.
            sb.AppendLine();
            if (!concentration.SingleGpu)
            {
                sb.AppendLine($"  Multi-GPU system ({gpu.NvidiaDeviceCount} NVIDIA adapters): these SM coordinates");
                sb.AppendLine("  cannot be attributed to a specific adapter. Adapter-specific localization conclusions");
                sb.AppendLine("  are suppressed — isolate to one GPU (physically or by driver) and re-run.");
            }
            else if (concentration.SuggestsLocalizedFailure)
            {
                sb.AppendLine("  The repeated concentration of errors on a small subset of SM(s) across");
                sb.AppendLine("  multiple events is a troubleshooting lead worth comparing with driver");
                sb.AppendLine("  install history, crash timing, and thermal/workload context.");
                sb.AppendLine("  It does not by itself identify the root cause.");
                if (ctx.GpuErrorsCapped)
                {
                    sb.AppendLine("  This reads the collected subset only; Max Events cap was reached and older");
                    sb.AppendLine("  events in the requested window were not read. See SCOPE block.");
                }
            }
            else if (concentration.IsWeakSingleGpuSignal)
            {
                if (concentration.Verdict == ReportAnalysis.LocalizationVerdict.SmallSample)
                {
                    sb.AppendLine("  Sample size is below the threshold needed to distinguish localization");
                    sb.AppendLine("  from coincidence. The per-SM grouping above is the raw fact; treat it");
                    sb.AppendLine("  as anecdotal. Re-run with a wider Max Days window or watch for");
                    sb.AppendLine("  recurrence before drawing conclusions.");
                }
                else if (concentration.Verdict == ReportAnalysis.LocalizationVerdict.TooManyLocations)
                {
                    sb.AppendLine("  Errors recur, but across too many SM locations to describe as a tight");
                    sb.AppendLine("  localized cluster. The per-SM grouping above is the raw fact; treat it");
                    sb.AppendLine("  as a broad cluster and watch for a smaller recurring subset before");
                    sb.AppendLine("  drawing conclusions.");
                }
                else
                {
                    sb.AppendLine("  Errors are spread across too many SMs to distinguish localization from");
                    sb.AppendLine("  coincidence. The per-SM grouping above is the raw fact; treat it as");
                    sb.AppendLine("  anecdotal. A tight cluster on a small subset of SMs would be a stronger");
                    sb.AppendLine("  signal — watch for one to develop before drawing conclusions.");
                }
            }
        }
        else if (errors.Count > 0)
        {
            sb.AppendLine("  nvlddmkm errors detected but without SM coordinate data.");
        }
        else
        {
            if (ctx.HasFailure("Event Log: nvlddmkm"))
                sb.AppendLine("  nvlddmkm Event Log collection failed; no conclusion can be drawn from this run.");
            else
                sb.AppendLine("  No errors found in Event Log at this time.");
        }

        sb.AppendLine();
    }

    static void RenderFooter(RenderContext ctx)
    {
        var sb = ctx.Sb;
        sb.AppendLine(Sep);
        sb.AppendLine($"Report generated by FLARE (Fault Log Analysis & Reboot Examination) on {ctx.Now:yyyy-MM-dd HH:mm:ss}");
    }

    internal static string SaveUnique(string report, string reportDir, DateTime timestamp)
    {
        Directory.CreateDirectory(reportDir);

        var stem = $"flare_report_{timestamp:yyyyMMdd_HHmmss}";
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            var suffix = attempt == 0 ? "" : $"_{attempt:D3}";
            var path = Path.Combine(reportDir, $"{stem}{suffix}.txt");
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(report);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
                // Same-second run or another instance won the race. Try a suffix.
            }
        }

        var fallback = Path.Combine(reportDir, $"{stem}_{Guid.NewGuid():N}.txt");
        using (var stream = new FileStream(fallback, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(report);
        }
        return fallback;
    }

    // IsPreLog = synthetic oldest bucket for errors timestamped before the first known install.
    internal sealed record DriverPeriodBucket(
        DateTime Start,
        DateTime? End,
        string Version,
        int ErrorCount,
        bool IsPreLog);

    static string FormatDriverPeriodRow(
        DriverPeriodBucket b,
        DateTime? retentionFloor,
        DateTime? capBlindBefore)
    {
        var endStr = b.End.HasValue ? b.End.Value.ToString("yyyy-MM-dd") : "present";
        var prefix = b.IsPreLog
            ? "    (unknown, pre-log):"
            : $"    {b.Start:yyyy-MM-dd}  {NvidiaDriverVersion.ToNvidiaVersion(b.Version),-8}";

        if (capBlindBefore.HasValue && EndsOnOrBefore(b.End, capBlindBefore.Value))
            return b.IsPreLog
                ? $"{prefix} truncated-out (to {endStr})"
                : $"{prefix} truncated-out  (to {endStr})";

        if (retentionFloor.HasValue && EndsOnOrBefore(b.End, retentionFloor.Value))
            return b.IsPreLog
                ? $"{prefix} not retained (to {endStr})"
                : $"{prefix} not retained (to {endStr})";

        if (capBlindBefore.HasValue && CrossesFloor(b.Start, b.End, capBlindBefore.Value))
            return FormatPartialDriverPeriodRow(b, prefix, endStr, capBlindBefore.Value, "not collected");

        if (retentionFloor.HasValue && CrossesFloor(b.Start, b.End, retentionFloor.Value))
            return FormatPartialDriverPeriodRow(b, prefix, endStr, retentionFloor.Value, "not retained");

        return b.IsPreLog
            ? $"{prefix} {b.ErrorCount} errors (to {endStr})"
            : $"{prefix} {b.ErrorCount,5} errors  (to {endStr})";
    }

    static string FormatPartialDriverPeriodRow(
        DriverPeriodBucket b,
        string prefix,
        string endStr,
        DateTime floor,
        string reason) =>
        b.IsPreLog
            ? $"{prefix} {b.ErrorCount} errors (partial; before {floor:yyyy-MM-dd} {reason}, to {endStr})"
            : $"{prefix} {b.ErrorCount,5} errors  (partial; before {floor:yyyy-MM-dd} {reason}, to {endStr})";

    static bool EndsOnOrBefore(DateTime? end, DateTime floor) =>
        end.HasValue && end.Value <= floor;

    static bool CrossesFloor(DateTime start, DateTime? end, DateTime floor) =>
        start < floor && (!end.HasValue || end.Value > floor);

    internal static List<DriverPeriodBucket> ComputeDriverPeriodBuckets(
        List<NvlddmkmError> errors,
        List<EventLogParser.DriverInstallEvent> driverInstalls)
    {
        var buckets = new List<DriverPeriodBucket>();
        if (driverInstalls.Count == 0) return buckets;

        var sorted = driverInstalls.OrderBy(d => d.Timestamp).ToList();

        var preCount = errors.Count(e => e.Timestamp < sorted[0].Timestamp);
        if (preCount > 0)
            buckets.Add(new DriverPeriodBucket(
                Start: DateTime.MinValue, End: sorted[0].Timestamp,
                Version: "", ErrorCount: preCount, IsPreLog: true));

        for (int i = 0; i < sorted.Count; i++)
        {
            var start = sorted[i].Timestamp;
            DateTime? end = i + 1 < sorted.Count ? sorted[i + 1].Timestamp : null;
            var count = errors.Count(e => e.Timestamp >= start && (!end.HasValue || e.Timestamp < end.Value));
            buckets.Add(new DriverPeriodBucket(
                Start: start, End: end,
                Version: sorted[i].DriverVersion, ErrorCount: count, IsPreLog: false));
        }

        return buckets;
    }

}
