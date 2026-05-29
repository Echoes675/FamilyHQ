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
    string? RecurrenceRule = null);
