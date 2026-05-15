namespace FamilyHQ.Core.Models;

public record AuthStatusResult(TokenAuthStatus Status, string? LastError, DateTimeOffset? Since);
