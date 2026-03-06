namespace FamilyHQ.Core.DTOs;

public record CalendarEventDto(
    Guid Id,
    string GoogleEventId,
    Guid CalendarInfoId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    string? CalendarColor);
