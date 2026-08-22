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

    // Google ID of the parent recurring series. Null for non-recurring events.
    public string? GoogleRecurringEventId { get; set; }

    // The original start time of an instance within a series. Set only on exception
    // instances (instances moved or modified from the series default); null otherwise.
    public DateTimeOffset? OriginalStartTime { get; set; }

    // The RRULE text describing the recurrence pattern. Non-null whenever this row
    // represents part of a recurring series (i.e. whenever GoogleRecurringEventId is set).
    public string? RecurrenceRule { get; set; }

    // The IANA zone Google reports as this event's start.timeZone (e.g. "America/New_York").
    //
    // For a recurring instance this is the zone the SERIES is anchored to — the zone Google expands
    // future occurrences in — so it must be sent back unchanged on an edit rather than replaced with
    // the family's configured zone (FHQ-170), and it is the zone a "this and following" COUNT split
    // must enumerate in (FHQ-164).
    //
    // Null when Google supplied none: an all-day event carries no zone by design, and start.timeZone
    // is optional on a single timed event. Existing rows are also null until a fetch actually reports
    // one — the backfill is lazy and opportunistic (FHQ-164 Decision 4), never defaulted.
    public string? IanaTimeZone { get; set; }

    // True when this event belongs to a recurring series.
    public bool IsRecurring => GoogleRecurringEventId is not null;

    // True when this event is an exception instance (moved/modified from its series default).
    public bool IsException => OriginalStartTime is not null;

    // FK to the CalendarInfo that owns this event in Google (individual or shared calendar).
    public Guid OwnerCalendarInfoId { get; set; }

    // Family members assigned to this event (for display projection).
    // For a 1-member event: contains that member's CalendarInfo.
    // For a shared event: contains all assigned members' CalendarInfo rows.
    public ICollection<CalendarInfo> Members { get; set; } = new List<CalendarInfo>();

    // Transient: populated by GoogleCalendarClient.GetEventsAsync from extendedProperties.private["content-hash"].
    // Not persisted. Used by CalendarSyncService to detect webhook self-echoes (FHQ-30).
    public string? ContentHash { get; set; }
}
