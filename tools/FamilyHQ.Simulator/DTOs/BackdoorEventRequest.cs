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

    // FHQ-161: the IANA zone the seeded master is anchored to (Google's start.timeZone), e.g.
    // "Europe/London". Expansion holds this zone's wall clock across a DST transition. Null keeps
    // the legacy fixed-UTC expansion.
    public string? StartTimeZone { get; set; }
}
