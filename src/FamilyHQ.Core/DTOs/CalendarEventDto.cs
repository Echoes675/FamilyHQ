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
    IReadOnlyList<EventCalendarDto> Members);
