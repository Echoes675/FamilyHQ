namespace FamilyHQ.Core.DTOs;

public record CalendarEventDto(
    Guid Id,
    string GoogleEventId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    IReadOnlyList<EventCalendarDto> Members,
    // FHQ-18: recurrence projection so the UI can pre-populate the picker on edit and decide
    // whether to show the scope prompt. IsRecurring mirrors CalendarEvent.IsRecurring; the
    // RRULE seeds RecurrencePicker. Defaulted so existing non-recurrence callers are unaffected.
    bool IsRecurring = false,
    string? RecurrenceRule = null);
