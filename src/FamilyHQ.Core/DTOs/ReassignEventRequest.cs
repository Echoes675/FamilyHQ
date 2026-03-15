namespace FamilyHQ.Core.DTOs;

public record ReassignEventRequest(
    Guid ToCalendarId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description);
