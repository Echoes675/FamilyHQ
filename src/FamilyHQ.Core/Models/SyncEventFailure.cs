namespace FamilyHQ.Core.Models;

/// <summary>
/// Records a per-event failure encountered during calendar sync. Captured so the
/// remaining events in the batch can still be persisted and the failure surfaced
/// in the diagnostics page.
/// </summary>
public class SyncEventFailure
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = null!;

    public Guid CalendarInfoId { get; set; }

    public string GoogleEventId { get; set; } = null!;

    public string? EventTitle { get; set; }

    public string FailureReason { get; set; } = null!;

    public string ExceptionType { get; set; } = null!;

    public DateTimeOffset FailedAt { get; set; }

    public bool Resolved { get; set; }

    public CalendarInfo CalendarInfo { get; set; } = null!;
}
