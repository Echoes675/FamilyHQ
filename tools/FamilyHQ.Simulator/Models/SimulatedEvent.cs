namespace FamilyHQ.Simulator.Models;

public class SimulatedEvent
{
    public string Id { get; set; } = string.Empty;
    public string CalendarId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string? ContentHash { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public string? UserId { get; set; }
    public bool IsDeleted { get; set; }

    // FHQ-18.11 recurrence READ emulation.
    // RecurrenceRule, when set, marks this row as a series MASTER: an RFC 5545 RRULE
    // line (e.g. "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=6"). events.list?singleEvents=true
    // expands it into per-occurrence instances; events.get returns it carrying a
    // recurrence array. Null for ordinary (non-recurring) events.
    public string? RecurrenceRule { get; set; }

    // RecurringEventId/OriginalStartTime model exception instances (a single occurrence
    // moved or modified out of the series). Not yet emitted by the expander — reserved for
    // Pass 2 so the store schema is forward-compatible.
    public string? RecurringEventId { get; set; }
    public DateTime? OriginalStartTime { get; set; }

    // FHQ-18.11 (Pass 4): a CANCELLED occurrence override. When the app deletes a single
    // occurrence ("This event"), Google records the slot with status "cancelled" rather than
    // returning an instance. The simulator mirrors this by storing a tombstone override row
    // (RecurringEventId + OriginalStartTime identify the cancelled slot) with this flag set;
    // expansion under singleEvents=true then OMITS that slot entirely. A cancellation overrides
    // any prior content exception on the same slot — the occurrence simply disappears.
    public bool IsCancelled { get; set; }
}