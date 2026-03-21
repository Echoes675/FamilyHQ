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

    // FK to the CalendarInfo that is the Google organiser for this event.
    // Used to select the correct calendarId for events.update, events.move, events.delete.
    public Guid OwnerCalendarInfoId { get; set; }

    // True when Google's organizer.Self = false at sync time. Informational only.
    public bool IsExternallyOwned { get; set; }

    // Navigation properties
    public ICollection<CalendarInfo> Calendars { get; set; } = new List<CalendarInfo>();
}
