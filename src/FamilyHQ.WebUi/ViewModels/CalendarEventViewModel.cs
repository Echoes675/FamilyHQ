using FamilyHQ.Core.Models;

namespace FamilyHQ.WebUi.ViewModels;

public record CalendarEventViewModel(
    Guid Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    // The calendar this capsule represents on the grid
    Guid CalendarInfoId,
    string CalendarDisplayName,
    string? CalendarColor,
    // All calendars this event belongs to — for chip rendering in edit modal
    IReadOnlyList<CalendarSummaryViewModel> AllCalendars,
    // FHQ-18 recurrence projection: drives the edit modal's picker pre-population and the
    // scope-prompt decision. Defaulted so existing constructions stay non-recurring.
    bool IsRecurring = false,
    string? RecurrenceRule = null,
    // Google's reminders for this event, exactly as the API sent them. The reminder tab is built from
    // this when the modal opens, so the four states have to arrive intact: null is "never synced" and
    // not "no reminders", and an explicit empty list is the family asking for none.
    EventReminders? Reminders = null,
    // The calendar this event actually lives on, as the server stored it — mirrors
    // CalendarEventDto.OwningCalendarId. Null for a brand-new event, which has no owner yet.
    Guid? OwningCalendarId = null,
    // That calendar's own default reminders. The modal reads this back for an existing event instead
    // of re-deriving it from the selected member chips (EventModalLogic.OwningCalendarDefaults).
    EventReminders? OwningCalendarDefaultReminders = null);
