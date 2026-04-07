namespace FamilyHQ.Core.DTOs;

public record EventCalendarDto(Guid Id, string DisplayName, string? Color, bool IsShared = false, bool IsVisible = true);
