using System;
using System.Collections.Generic;

namespace FLARE.Core;

public enum CollectorNoticeKind
{
    Canary,
    Failure,
    Skipped,
}

public sealed record CollectorNotice(CollectorNoticeKind Kind, string Source, string Message);

public sealed record EventLogRetentionInfo(
    string LogName,
    string? LogMode,
    long? MaximumSizeInBytes,
    long? FileSizeBytes,
    long? RecordCount,
    long? OldestRecordNumber,
    DateTime? OldestRecordTimestamp,
    DateTime? OldestRelevantEventTimestamp = null,
    string? OldestRelevantEventDescription = null);

// Aggregates all run-scoped signals that need to reach the saved report: cap
// hits (Truncation), Event Log retention, plus collector canaries and failures.
// Threaded through the pipeline so anything a reader of the .txt would otherwise
// miss lands in the SCOPE block next to the requested window.
public sealed class CollectorHealth
{
    public CollectionTruncation Truncation { get; init; } = new();
    public EventLogRetentionInfo? SystemEventLog { get; set; }
    public EventLogRetentionInfo? ApplicationEventLog { get; set; }
    public List<CollectorNotice> Notices { get; } = [];

    public void Canary(string source, string message) =>
        Notices.Add(new CollectorNotice(CollectorNoticeKind.Canary, source, message));

    public void Failure(string source, string message) =>
        Notices.Add(new CollectorNotice(CollectorNoticeKind.Failure, source, message));

    public void Skipped(string source, string message) =>
        Notices.Add(new CollectorNotice(CollectorNoticeKind.Skipped, source, message));
}
