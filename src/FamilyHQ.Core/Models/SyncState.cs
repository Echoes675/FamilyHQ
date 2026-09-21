namespace FamilyHQ.Core.Models;

public class SyncState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CalendarInfoId { get; set; }
    
    public string? SyncToken { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset? SyncWindowStart { get; set; }
    public DateTimeOffset? SyncWindowEnd { get; set; }

    // FHQ-189: when this calendar was first synced WITH reminders in the field mask.
    //
    // Null on every row that existed before reminders were added, and the trigger for exactly one
    // forced full sync of that calendar. Incremental sync never re-sends an unchanged event, so
    // without this the events already in production would never gain their reminders — no amount
    // of ordinary syncing would backfill them.
    public DateTimeOffset? RemindersSyncedAt { get; set; }

    // Navigation properties
    public CalendarInfo CalendarInfo { get; set; } = null!;
}
