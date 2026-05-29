namespace FamilyHQ.Core.DTOs;

public record CreateEventRequest(
    IReadOnlyList<Guid> MemberCalendarInfoIds, // min 1, no duplicates; determines shared vs individual calendar
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description, // user-visible description; [members:...] tag is managed automatically
    // Optional RFC 5545 RRULE line (e.g. "RRULE:FREQ=WEEKLY;BYDAY=MO"). When non-null the event is
    // created as a recurring series master and its instances are materialised by a window reconcile.
    // Null (the default) creates a single, non-recurring event. The presentation layer builds this
    // string from a RecurrenceSpec via RecurrenceRuleBuilder; carrying the canonical RRULE keeps the
    // FamilyHQ.Core DTO free of a dependency on the FamilyHQ.Services recurrence engine.
    string? RecurrenceRule = null);
