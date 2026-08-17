using System;

namespace FamilyHQ.E2E.Data.Models;

public class SimulatorEventModel
{
    public string Id { get; set; } = "";
    public string CalendarId { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    /// <summary>
    /// Optional event description. May contain a [members: Name1, Name2] tag
    /// to designate which member calendars this event appears in when it lives
    /// on the shared calendar.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// FHQ-18.11: when set, an RFC 5545 RRULE line marking this seeded event as a
    /// recurring-series master (e.g. "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=6"). The
    /// Simulator expands the master into per-occurrence instances on events.list and
    /// serves the rule on events.get. Null for ordinary (non-recurring) events.
    /// </summary>
    public string? RecurrenceRule { get; set; }

    /// <summary>
    /// FHQ-161: the IANA zone a seeded recurring master is anchored to (Google's start.timeZone).
    /// The Simulator expands the series holding this zone's WALL CLOCK across a DST transition, as
    /// Google does — so a 19:00 weekly series stays 19:00 in the browser all year. Seeds must set it
    /// to <c>BrowserClock.TimeZoneId</c>; leaving it null reinstates fixed-UTC expansion, which makes
    /// uniform-time assertions fail for the ~2 weeks around each UK transition (intermittent-issues #12).
    /// </summary>
    public string? StartTimeZone { get; set; }
}
