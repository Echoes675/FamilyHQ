namespace FamilyHQ.Simulator.DTOs;

public class BackdoorEventRequest
{
    public string UserId { get; set; } = string.Empty;
    public string? CalendarId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsAllDay { get; set; }

    // FHQ-18.11: an RFC 5545 RRULE line (e.g. "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=6").
    // When supplied the seeded event becomes a recurring-series master. Null for
    // ordinary events, preserving existing backdoor behaviour.
    public string? RecurrenceRule { get; set; }
}
