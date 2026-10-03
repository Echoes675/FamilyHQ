namespace FamilyHQ.Core.DTOs;

/// <summary>
/// A person on the event, for the row's chips. Name and colour only — a shared event names the
/// people on it, never the shared calendar.
/// </summary>
public record ReminderMemberDto(string DisplayName, string? Color);
