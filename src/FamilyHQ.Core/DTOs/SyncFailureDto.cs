namespace FamilyHQ.Core.DTOs;

public record SyncFailureDto(
    Guid Id,
    Guid CalendarInfoId,
    string GoogleEventId,
    string? EventTitle,
    string FailureReason,
    string ExceptionType,
    DateTimeOffset FailedAt,
    bool Resolved);
