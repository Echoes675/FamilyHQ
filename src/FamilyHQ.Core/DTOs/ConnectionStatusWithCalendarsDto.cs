namespace FamilyHQ.Core.DTOs;

public record ConnectionStatusCalendarDto(Guid CalendarId, string DisplayName, DateTimeOffset? LastSyncedAt);

public record ConnectionStatusWithCalendarsDto(
    string Status,
    string? LastError,
    DateTimeOffset? Since,
    IReadOnlyList<ConnectionStatusCalendarDto> Calendars);
