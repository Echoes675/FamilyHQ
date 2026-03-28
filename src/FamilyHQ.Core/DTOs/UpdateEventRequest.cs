namespace FamilyHQ.Core.DTOs;

public record UpdateEventRequest(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    string? RecurrenceRule = null);
