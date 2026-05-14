using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace FLARE.Core;

public static class LiveKernelDumpReport
{
    public static string Generate(
        List<LiveKernelDump> dumps,
        List<NvlddmkmError> nvlddmkmErrors,
        List<EventLogParser.AppCrashEvent> appCrashes,
        List<EventLogParser.DriverInstallEvent> driverInstalls,
        int maxDays,
        bool sortDescending,
        bool deepAnalysis,
        string? cdbPath,
        CdbDetailsSink sink,
        Action<string>? log,
        CancellationToken ct,
        CollectorHealth? health,
        string? cdbCacheRoot = null)
    {
        var sb = new StringBuilder();
        var orphans = ScanOrphans(dumps, maxDays, cdbCacheRoot, health);

        if (dumps.Count == 0 && orphans.Count == 0)
        {
            var upstreamIssue = health?.Notices.Any(n =>
                (n.Kind == CollectorNoticeKind.Failure || n.Kind == CollectorNoticeKind.Skipped) &&
                (n.Source == "minidump copy" || n.Source == "livekernel scan")) == true;
            if (upstreamIssue)
            {
                sb.AppendLine($"No live kernel dumps available — upstream collection was skipped or failed. See SCOPE block.");
            }
            else
            {
                sb.AppendLine($"No live kernel dumps found in last {maxDays} day(s).");
            }
            return sb.ToString();
        }

        if (dumps.Count == 0)
        {
            sb.AppendLine($"No live kernel dumps in last {maxDays} day(s), but FLARE has cached `!analyze -v` transcripts for {orphans.Count} previously-analyzed dump(s) whose source `.dmp` file(s) have since been removed. The preserved analyses follow.");
            sb.AppendLine();
            RenderOrphans(sb, orphans, sortDescending, nvlddmkmErrors, appCrashes, driverInstalls, sink, log, health);
            return sb.ToString();
        }

        sb.AppendLine("Live kernel dumps are non-BSOD kernel dumps Windows captures when a recoverable or attempted-recovery kernel fault occurs. GPU hangs often appear here without a matching nvlddmkm 13/14/153 event and without a WER application crash. Treat these as primary diagnostic evidence for the \"no event log spam, no BSOD, but something hung\" case.");
        sb.AppendLine();
        sb.AppendLine(@"Source folder: `C:\Windows\LiveKernelReports`");
        var capHit = health?.Truncation.LiveKernelScanCap == true;
        var capTotal = health?.Truncation.LiveKernelScanTotal ?? 0;
        var capSuffix = capHit ? " (capped — see SCOPE block)" : "";
        sb.AppendLine($"Scanned: {dumps.Count} dump(s) within last {maxDays} day(s){capSuffix}");
        if (orphans.Count > 0)
        {
            var orphanCap = health?.Truncation.MaxLiveKernelDumpsCap ?? 0;
            var orphanSuffix = health?.Truncation.LiveKernelOrphanCap == true
                ? $" (capped at {orphanCap} — see SCOPE block)"
                : "";
            sb.AppendLine($"Orphans: {orphans.Count} cached analysis/-es for dumps whose source `.dmp` has been removed (rendered below).{orphanSuffix}");
        }
        if (deepAnalysis && cdbPath != null)
            sb.AppendLine($"Deep analysis (cdb): enabled (`{cdbPath}`)");
        else if (deepAnalysis)
            sb.AppendLine("Deep analysis (cdb): unavailable (cdb.exe not found)");
        if (capHit)
        {
            sb.AppendLine();
            sb.AppendLine($"> Note: livekernel scan capped at {dumps.Count} of {capTotal} dump(s); older dump(s) in the window were not analyzed. See SCOPE block.");
        }
        sb.AppendLine();

        var ordered = sortDescending
            ? dumps.OrderByDescending(d => d.Timestamp).ToList()
            : dumps.OrderBy(d => d.Timestamp).ToList();

        foreach (var d in ordered)
        {
            ct.ThrowIfCancellationRequested();
            var info = DumpAnalyzer.AnalyzeDump(d.FullPath, log);
            sb.AppendLine($"### {d.Timestamp:yyyy-MM-dd HH:mm:ss} — [{d.Category}] {d.FileName}");
            sb.AppendLine();
            sb.AppendLine($"- **Size:** {FormatSize(d.FileSize)}");
            if (info == null)
            {
                sb.AppendLine("- _(could not parse dump header)_");
                sb.AppendLine();
                continue;
            }
            sb.AppendLine($"- **Code:** `0x{info.BugcheckCode:X} {info.BugcheckName}`");
            sb.AppendLine($"- **Classification:** {Classify(info.BugcheckCode)}");
            if (info.IsHeuristicMatch)
                sb.AppendLine("- **Parameters:** _(omitted — bugcheck located by heuristic scan, parameter offsets speculative)_");
            else
                sb.AppendLine($"- **Parameters:** `0x{info.Param1:X} 0x{info.Param2:X} 0x{info.Param3:X} 0x{info.Param4:X}`");
            if (info.IsGpuRelated)
            {
                sb.AppendLine();
                sb.AppendLine("> ⚠️ **GPU-RELATED**");
            }
            sb.AppendLine();

            if (deepAnalysis && cdbPath != null)
            {
                var cached = CdbAnalysisCache.TryLoad(d.FullPath, log, cdbCacheRoot);
                string? transcript;
                if (cached != null)
                {
                    log?.Invoke($"  {d.FileName}: using cached cdb analysis");
                    transcript = cached;
                }
                else
                {
                    log?.Invoke($"  Analyzing {d.FileName} ({FormatSize(d.FileSize)}) with cdb...");
                    transcript = CdbRunner.RunCdbAnalysis(cdbPath, d.FullPath, log, ct);
                    if (transcript != null)
                    {
                        CdbAnalysisCache.Store(d.FullPath, transcript, log, cdbCacheRoot);
                        log?.Invoke($"  {d.FileName}: cdb analysis done");
                    }
                    else
                    {
                        log?.Invoke($"  {d.FileName}: cdb analysis failed (no output or timed out)");
                    }
                }
                if (transcript != null)
                {
                    var summary = CdbRunner.ExtractCdbSummary(transcript, log, health);
                    if (summary != null)
                    {
                        sb.AppendLine($"**WinDbg Analysis** — [full stack trace](./{CdbDetailsSink.DumpsFilenamePlaceholder}#{d.FileName}):");
                        sb.AppendLine();
                        sb.Append(sink.EmitInlineAndArchive(DumpSection.LiveKernel, d.FileName, summary));
                        if (System.Text.RegularExpressions.Regex.IsMatch(summary, @"PROCESS_NAME:\s+System\b"))
                        {
                            sb.AppendLine();
                            sb.AppendLine("> **Note:** `PROCESS_NAME = System` is normal for scheduler worker-thread crashes — rely on `MODULE_NAME` / `IMAGE_NAME` / `FAILURE_BUCKET_ID` for attribution.");
                        }
                        sb.AppendLine();
                    }
                    else
                    {
                        sb.AppendLine("_WinDbg Analysis: no reportable summary could be extracted from cdb output._");
                        health?.Failure($"livekernel cdb: {d.FileName}", "cdb ran but FLARE could not extract a reportable summary");
                        sb.AppendLine();
                    }
                }
                else
                {
                    sb.AppendLine("_WinDbg Analysis: unavailable (cdb produced no usable output or timed out)._");
                    health?.Failure($"livekernel cdb: {d.FileName}", "cdb produced no usable output or timed out");
                    sb.AppendLine();
                }
            }

            AppendCorrelation(sb, d.Timestamp, nvlddmkmErrors, appCrashes, driverInstalls);
            sb.AppendLine();
        }

        RenderOrphans(sb, orphans, sortDescending, nvlddmkmErrors, appCrashes, driverInstalls, sink, log, health);

        return sb.ToString();
    }

    private static List<CdbAnalysisCache.CachedDumpAnalysis> ScanOrphans(
        List<LiveKernelDump> dumps,
        int maxDays,
        string? cdbCacheRoot,
        CollectorHealth? health)
    {
        var cutoff = EventLogParser.MaxDaysToMidnightCutoff(maxDays);
        var seenNames = new HashSet<string>(dumps.Select(d => d.FileName), StringComparer.OrdinalIgnoreCase);
        var all = CdbAnalysisCache.EnumerateValid(cdbCacheRoot)
            .Where(c => !seenNames.Contains(c.DumpFileName))
            .Where(c => LooksLikeLiveKernelDump(c.DumpFileName))
            .Where(c => c.DumpMtime >= cutoff)
            .OrderByDescending(c => c.DumpMtime)
            .ToList();

        var cap = health?.Truncation.MaxLiveKernelDumpsCap ?? 0;
        if (cap > 0 && all.Count > cap)
        {
            var total = all.Count;
            if (health != null)
            {
                health.Truncation.LiveKernelOrphanCap = true;
                health.Truncation.LiveKernelOrphanTotal = total;
            }
            return all.Take(cap).ToList();
        }
        return all;
    }

    private static void RenderOrphans(
        StringBuilder sb,
        List<CdbAnalysisCache.CachedDumpAnalysis> orphans,
        bool sortDescending,
        List<NvlddmkmError> nv,
        List<EventLogParser.AppCrashEvent> apps,
        List<EventLogParser.DriverInstallEvent> drivers,
        CdbDetailsSink sink,
        Action<string>? log,
        CollectorHealth? health)
    {
        if (orphans.Count == 0) return;

        var ordered = sortDescending
            ? orphans.OrderByDescending(c => c.DumpMtime).ToList()
            : orphans.OrderBy(c => c.DumpMtime).ToList();

        sb.AppendLine("### Cached analyses (source dump no longer present)");
        sb.AppendLine();
        sb.AppendLine("_FLARE has cdb `!analyze -v` transcripts for the dump(s) below, but the source `.dmp` file(s) have been removed from `%LOCALAPPDATA%\\FLARE\\DO_NOT_SHARE\\LiveKernelDumps\\`. The analysis is preserved here; delete the matching `.cdb.txt` file under `%LOCALAPPDATA%\\FLARE\\DO_NOT_SHARE\\CdbCache\\` to remove the entry._");
        sb.AppendLine();

        foreach (var o in ordered)
        {
            sb.AppendLine($"#### {o.DumpMtime:yyyy-MM-dd HH:mm:ss} — {o.DumpFileName} _(source removed)_");
            sb.AppendLine();
            sb.AppendLine($"- **Original size:** {FormatSize(o.DumpSize)}");
            sb.AppendLine();

            var summary = CdbRunner.ExtractCdbSummary(o.Transcript, log, health);
            if (summary != null)
            {
                sb.AppendLine($"**WinDbg Analysis** — from cached transcript, [full stack trace](./{CdbDetailsSink.DumpsFilenamePlaceholder}#{o.DumpFileName}):");
                sb.AppendLine();
                sb.Append(sink.EmitInlineAndArchive(DumpSection.LiveKernel, o.DumpFileName, summary));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("_WinDbg Analysis: cached transcript could not be parsed into a summary._");
                health?.Failure($"livekernel cdb orphan: {o.DumpFileName}", "cached transcript could not be parsed into a summary");
                sb.AppendLine();
            }

            AppendCorrelation(sb, o.DumpMtime, nv, apps, drivers);
            sb.AppendLine();
        }
    }

    private static readonly string[] LiveKernelFileNamePrefixes =
        ["WATCHDOG", "LIVEKERNELDUMP", "DISPLAYDIAG"];

    private static bool LooksLikeLiveKernelDump(string fileName) =>
        LiveKernelFileNamePrefixes.Any(p => fileName.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static string Classify(uint code) =>
        BugcheckCatalog.IsLiveDumpCode(code)
            ? $"{BugcheckCatalog.GetName(code)} (live dump; not a system bugcheck)"
            : BugcheckCatalog.GetName(code);

    // Live kernel dumps are produced after TDR-style recovery sequences that may take seconds to
    // complete; 60s catches the dump-creation lag against the triggering nvlddmkm event without
    // catching unrelated traffic.
    private static readonly TimeSpan CorrelationWindow = TimeSpan.FromSeconds(60);

    private static void AppendCorrelation(
        StringBuilder sb,
        DateTime dumpTime,
        List<NvlddmkmError> nv,
        List<EventLogParser.AppCrashEvent> apps,
        List<EventLogParser.DriverInstallEvent> drivers)
    {
        var windowSecs = CorrelationWindow.TotalSeconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        sb.AppendLine($"**Correlation** (±{windowSecs}s window):");
        sb.AppendLine();

        var nvMatches = nv.Where(e => (e.Timestamp - dumpTime).Duration() <= CorrelationWindow)
                          .OrderBy(e => (e.Timestamp - dumpTime).Duration())
                          .ToList();
        if (nvMatches.Count == 0)
            sb.AppendLine("- nvlddmkm 13/14/153: none in window");
        else
        {
            var closest = nvMatches[0];
            var off = FormatOffset(closest.Timestamp, dumpTime);
            var suffix = nvMatches.Count > 1 ? $" ({nvMatches.Count} total)" : "";
            sb.AppendLine($"- nvlddmkm 13/14/153: {closest.Timestamp:yyyy-MM-dd HH:mm:ss}  ID:{closest.EventId}  {closest.ErrorType ?? ""} {off}{suffix}".TrimEnd());
        }

        var appMatches = apps.Where(a => (a.Timestamp - dumpTime).Duration() <= CorrelationWindow)
                             .OrderBy(a => (a.Timestamp - dumpTime).Duration())
                             .ToList();
        if (appMatches.Count == 0)
            sb.AppendLine("- app crashes/hangs: none in window");
        else
        {
            var closest = appMatches[0];
            var off = FormatOffset(closest.Timestamp, dumpTime);
            var suffix = appMatches.Count > 1 ? $" ({appMatches.Count} total)" : "";
            sb.AppendLine($"- app crashes/hangs: `{closest.Application}` {off}{suffix}");
        }

        var priorDriver = drivers.Where(x => x.Timestamp <= dumpTime)
                                 .OrderByDescending(x => x.Timestamp)
                                 .FirstOrDefault();
        if (priorDriver == null)
            sb.AppendLine("- nearest driver install: none found");
        else
        {
            var version = NvidiaDriverVersion.ToNvidiaVersion(priorDriver.DriverVersion);
            sb.AppendLine($"- nearest driver install: {priorDriver.Timestamp:yyyy-MM-dd} — {version} ({priorDriver.DriverVersion})");
        }

        sb.AppendLine();
        sb.AppendLine("> Timing proximity is not proof of causality.");
    }

    private static string FormatOffset(DateTime other, DateTime dumpTime)
    {
        var delta = (other - dumpTime).TotalSeconds;
        var sign = delta >= 0 ? "+" : "-";
        return $"{sign}{Math.Abs(delta):F0}s";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024)
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1} MiB", bytes / 1024d / 1024d);
        if (bytes >= 1024)
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1} KiB", bytes / 1024d);
        return $"{bytes} bytes";
    }
}
