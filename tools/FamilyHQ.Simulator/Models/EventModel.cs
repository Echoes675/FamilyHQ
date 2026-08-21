namespace FamilyHQ.Simulator.Models;

public class EventModel
{
    public string Id { get; set; } = "";
    public string CalendarId { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    /// <summary>
    /// Optional event description. May contain a [members: ...] tag to designate
    /// which member calendars this event appears in when stored on the shared calendar.
    /// </summary>
    public string? Description { get; set; }
    /// <summary>
    /// Additional calendar IDs that this event appears in.
    /// Seeds EventAttendee rows so the event surfaces on each attendee calendar's feed.
    /// </summary>
    public List<string> AttendeeCalendarIds { get; set; } = new();

    /// <summary>
    /// FHQ-18.11: when set, an RFC 5545 RRULE line marking this seeded event as a
    /// recurring-series master (e.g. "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=6"). Null for
    /// ordinary events.
    /// </summary>
    public string? RecurrenceRule { get; set; }

    /// <summary>
    /// FHQ-161: the IANA zone a seeded recurring master is anchored to (Google's start.timeZone),
    /// e.g. "Europe/London". Expansion holds this zone's WALL CLOCK across a DST transition, as
    /// Google does. Null seeds a master with no zone, which expands at fixed UTC instants.
    /// </summary>
    public string? StartTimeZone { get; set; }
}