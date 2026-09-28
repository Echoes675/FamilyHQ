using FamilyHQ.Core.Models;

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
    bool ClearRecurrence = false,
    // Absent (the default) means the user did not touch reminders, so the write says nothing about
    // them and whatever Google holds — including a set made in the Google Calendar app on a phone —
    // survives untouched. Present means "make the event's reminders exactly this set"; Google
    // replaces the whole overrides array, so a partial set is a deletion of the rest.
    // Absent and EventReminders.ExplicitlyNone must never be collapsed: the second is the user
    // asking for no reminders, and is a write.
    EventReminders? Reminders = null);
