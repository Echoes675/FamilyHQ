namespace FamilyHQ.Core.Models;

public enum SyncJobStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3
}

public enum SyncJobSource
{
    Webhook = 0,
    Periodic = 1
}

/// <summary>
/// A durable unit of sync work. Enqueued by the webhook (targeted, CalendarInfoId set)
/// or the periodic timer (sync-all, CalendarInfoId null) and drained by CalendarSyncWorker.
/// Failed/Completed rows are terminal audit history, pruned after a retention window;
/// they never block new enqueues.
/// </summary>
public class CalendarSyncJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = null!;

    /// <summary>Null = sync all calendars for the user (periodic). Set = targeted (webhook).</summary>
    public Guid? CalendarInfoId { get; set; }

    public SyncJobStatus Status { get; set; } = SyncJobStatus.Pending;

    public SyncJobSource Source { get; set; }

    public string? ChannelId { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset EnqueuedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>When a retryable failure becomes eligible to run again (backoff).</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }
}
