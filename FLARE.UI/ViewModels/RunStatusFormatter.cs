using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FLARE.Core;

namespace FLARE.UI.ViewModels;

internal static class RunStatusFormatter
{
    public static string GetRunStatusText(FlareResult result, DateTime? now = null)
    {
        var eventLogFailed = result.Health?.Notices.Any(n =>
            n.Kind == CollectorNoticeKind.Failure && n.Source == "Event Log: nvlddmkm") == true;
        if (eventLogFailed)
            return "Event Log collection failed - see report";

        var limitedHistory = HasLimitedSystemEventLogHistory(result, now);
        if (result.Errors.Count == 0)
            return limitedHistory
                ? "No retained nvlddmkm errors found (limited history)"
                : "No nvlddmkm errors found";

        if (!limitedHistory)
            return result.HasSmErrors
                ? "nvlddmkm errors found (SM coordinates present)"
                : "nvlddmkm errors found (no SM coordinates)";

        return result.HasSmErrors
            ? "nvlddmkm errors found (SM coords; limited history)"
            : "nvlddmkm errors found (no SM coords; limited history)";
    }

    public static string? GetEventLogRetentionSummary(FlareResult result, DateTime? now = null)
    {
        var summaries = new List<string>();

        var systemSummary = GetSystemEventLogRetentionSummary(result, now);
        if (systemSummary != null)
            summaries.Add(systemSummary);

        var applicationSummary = GetApplicationEventLogRetentionSummary(result, now);
        if (applicationSummary != null)
            summaries.Add(applicationSummary);

        return summaries.Count == 0
            ? null
            : string.Join(Environment.NewLine, summaries);
    }

    private static string? GetSystemEventLogRetentionSummary(FlareResult result, DateTime? now)
    {
        if (!HasLimitedSystemEventLogHistory(result, now))
            return null;

        var retention = result.Health?.SystemEventLog;
        if (retention?.OldestRecordTimestamp is not DateTime oldestSystem)
            return null;

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(retention.LogMode))
            details.Add($"mode {retention.LogMode}");
        if (retention.MaximumSizeInBytes.HasValue)
            details.Add($"max {FormatByteSize(retention.MaximumSizeInBytes.Value)}");
        var detailText = details.Count > 0 ? $" ({string.Join(", ", details)})" : "";

        var oldestGpuText = retention.OldestRelevantEventTimestamp.HasValue
            ? $"oldest nvlddmkm 13/14/153 record is {retention.OldestRelevantEventTimestamp.Value:yyyy-MM-dd HH:mm:ss}"
            : "no retained nvlddmkm 13/14/153 records were found";

        return string.Join(Environment.NewLine, new[]
        {
            "System Event Log history is limited:",
            $"  retained since {oldestSystem:yyyy-MM-dd HH:mm:ss}{detailText}",
            $"  {oldestGpuText}.",
            "  Earlier requested days are unavailable.",
        });
    }

    private static string? GetApplicationEventLogRetentionSummary(FlareResult result, DateTime? now)
    {
        if (!HasLimitedApplicationEventLogHistory(result, now))
            return null;

        var retention = result.Health?.ApplicationEventLog;
        if (retention?.OldestRecordTimestamp is not DateTime oldestApplication)
            return null;

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(retention.LogMode))
            details.Add($"mode {retention.LogMode}");
        if (retention.MaximumSizeInBytes.HasValue)
            details.Add($"max {FormatByteSize(retention.MaximumSizeInBytes.Value)}");
        var detailText = details.Count > 0 ? $" ({string.Join(", ", details)})" : "";

        var relevantDescription = string.IsNullOrWhiteSpace(retention.OldestRelevantEventDescription)
            ? "Application Error 1000 / Application Hang 1002"
            : retention.OldestRelevantEventDescription;
        var oldestRelevantText = retention.OldestRelevantEventTimestamp.HasValue
            ? $"oldest {relevantDescription} record is {retention.OldestRelevantEventTimestamp.Value:yyyy-MM-dd HH:mm:ss}"
            : $"no retained {relevantDescription} records were found";

        return string.Join(Environment.NewLine, new[]
        {
            "Application Event Log history is limited:",
            $"  retained since {oldestApplication:yyyy-MM-dd HH:mm:ss}{detailText}",
            $"  {oldestRelevantText}.",
            "  Earlier requested days are unavailable.",
        });
    }

    public static bool HasLimitedSystemEventLogHistory(FlareResult result, DateTime? now = null)
    {
        var requestedMaxDays = result.Health?.Truncation.RequestedMaxDays ?? 0;
        var oldest = result.Health?.SystemEventLog?.OldestRecordTimestamp;
        if (requestedMaxDays <= 0 || !oldest.HasValue)
            return false;

        var requestedStart = (now ?? DateTime.Now).AddDays(-requestedMaxDays);
        return oldest.Value > requestedStart;
    }

    private static bool HasLimitedApplicationEventLogHistory(FlareResult result, DateTime? now = null)
    {
        var requestedMaxDays = result.Health?.Truncation.RequestedMaxDays ?? 0;
        var oldest = result.Health?.ApplicationEventLog?.OldestRecordTimestamp;
        if (requestedMaxDays <= 0 || !oldest.HasValue)
            return false;

        var requestedStart = (now ?? DateTime.Now).AddDays(-requestedMaxDays);
        return oldest.Value > requestedStart;
    }

    static string FormatByteSize(long bytes)
    {
        if (bytes >= 1024 * 1024)
            return string.Format(CultureInfo.InvariantCulture, "{0:F1} MiB", bytes / 1024d / 1024d);
        if (bytes >= 1024)
            return string.Format(CultureInfo.InvariantCulture, "{0:F1} KiB", bytes / 1024d);
        return string.Format(CultureInfo.InvariantCulture, "{0} bytes", bytes);
    }
}
