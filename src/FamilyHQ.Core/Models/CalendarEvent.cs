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
