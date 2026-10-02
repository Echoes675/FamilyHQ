namespace FamilyHQ.Core.DTOs;

/// <summary>
/// One row of the reminders timeline: a single notification a phone will make.
/// </summary>
/// <remarks>
/// Deliberately slim. The list is ~150 rows and is fetched whole, so every field here is paid for
/// 150 times — and the view opens the full event by id when a row is tapped rather than carrying
/// the whole event on every row.
/// </remarks>
public record UpcomingReminderDto(
    DateTimeOffset TriggerAt,
    string Method,
    int Minutes,
    bool IsDefault,
    Guid EventId,
    string EventTitle,
    DateTimeOffset EventStart,
    bool EventIsAllDay,
    IReadOnlyList<ReminderMemberDto> Members);

/// <summary>
/// A person on the event, for the row's chips. Name and colour only — a shared event names the
/// people on it, never the shared calendar.
/// </summary>
public record ReminderMemberDto(string DisplayName, string? Color);
