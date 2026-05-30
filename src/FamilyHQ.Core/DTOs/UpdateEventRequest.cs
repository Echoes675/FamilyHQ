namespace FamilyHQ.Core.DTOs;

public record UpdateEventRequest(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    // Recurrence toggle for the single-event update channel (UpdateAsync), expressed as two fields
    // so that "set/keep a recurrence" is distinguishable from "no change" and from "remove":
    //   • RecurrenceRule non-null on a currently NON-recurring event → turn recurrence ON (the event
    //     is promoted to a series in place and its instances are materialised by a window reconcile).
    //   • ClearRecurrence true on a currently recurring event → turn recurrence OFF (the series is
    //     collapsed to a single event and the orphaned instance rows are removed).
    //   • Both unset → no recurrence change (legacy non-recurring update behaviour).
    // The presentation layer builds RecurrenceRule from a RecurrenceSpec via RecurrenceRuleBuilder.
    string? RecurrenceRule = null,
    bool ClearRecurrence = false);
