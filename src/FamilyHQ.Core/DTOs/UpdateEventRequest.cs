namespace FamilyHQ.Core.DTOs;

public record UpdateEventRequest(
    Guid CalendarInfoId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description);
