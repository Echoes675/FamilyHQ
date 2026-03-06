namespace FamilyHQ.Core.Models;

public class CalendarEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GoogleEventId { get; set; } = null!;
    public Guid CalendarInfoId { get; set; }
    
    public string Title { get; set; } = null!;
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public bool IsAllDay { get; set; }
    
    public string? Location { get; set; }
    public string? Description { get; set; }

    // Navigation properties
    public CalendarInfo CalendarInfo { get; set; } = null!;
}
