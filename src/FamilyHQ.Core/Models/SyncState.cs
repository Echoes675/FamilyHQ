namespace FamilyHQ.Core.Models;

public class SyncState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CalendarInfoId { get; set; }
    
    public string? SyncToken { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public DateTimeOffset? SyncWindowStart { get; set; }
    public DateTimeOffset? SyncWindowEnd { get; set; }

    // Navigation properties
    public CalendarInfo CalendarInfo { get; set; } = null!;
}
