namespace FamilyHQ.Core.DTOs;

public record FailedSyncRunDto(
    Guid Id,
    Guid? CalendarInfoId,
    int AttemptCount,
    string? LastError,
    string Source,
    DateTimeOffset LastAttemptAt);
