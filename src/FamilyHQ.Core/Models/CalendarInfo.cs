namespace FamilyHQ.Core.Models;

public class CalendarInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GoogleCalendarId { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Color { get; set; }
    public bool IsVisible { get; set; } = true;

    // Navigation properties
    public ICollection<CalendarEvent> Events { get; set; } = new List<CalendarEvent>();
    public SyncState? SyncState { get; set; }
}
