namespace FamilyHQ.Core.DTOs;

public record CreateEventRequest(
    IReadOnlyList<Guid> CalendarInfoIds,   // min 1, no duplicates; CalendarInfoIds[0] is the organiser
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    string? RecurrenceRule = null);
