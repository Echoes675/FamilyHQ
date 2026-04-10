namespace FamilyHQ.Core.Models;

public class CalendarEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GoogleEventId { get; set; } = null!;

    public string Title { get; set; } = null!;
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public bool IsAllDay { get; set; }

    public string? Location { get; set; }
    public string? Description { get; set; }

    // FK to the CalendarInfo that owns this event in Google (individual or shared calendar).
    public Guid OwnerCalendarInfoId { get; set; }

    // Family members assigned to this event (for display projection).
    // For a 1-member event: contains that member's CalendarInfo.
    // For a shared event: contains all assigned members' CalendarInfo rows.
    public ICollection<CalendarInfo> Members { get; set; } = new List<CalendarInfo>();
}
