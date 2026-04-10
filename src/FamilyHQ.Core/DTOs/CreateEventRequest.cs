namespace FamilyHQ.Core.DTOs;

public record CreateEventRequest(
    IReadOnlyList<Guid> MemberCalendarInfoIds, // min 1, no duplicates; determines shared vs individual calendar
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description); // user-visible description; [members:...] tag is managed automatically
