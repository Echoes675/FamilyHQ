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

    /// <summary>
    /// RFC 5545 RRULE string for recurring events (e.g., "FREQ=WEEKLY;BYDAY=MO,WE,FR").
    /// Null for non-recurring events.
    /// </summary>
    public string? RecurrenceRule { get; set; }

    /// <summary>
    /// For exception instances: the original start time of the master event occurrence
    /// that this instance overrides. Stored as ISO 8601 string.
    /// </summary>
    public string? RecurrenceId { get; set; }

    /// <summary>
    /// True if this event is an exception to a recurring series
    /// (i.e., a modified or deleted instance).
    /// </summary>
    public bool IsRecurrenceException { get; set; }

    /// <summary>
    /// For exception instances: the ID of the master recurring event.
    /// </summary>
    public Guid? MasterEventId { get; set; }
}
