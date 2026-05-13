namespace FamilyHQ.Core.DTOs;

public record ConnectionStatusDto(string Status, string? LastError, DateTimeOffset? Since);
