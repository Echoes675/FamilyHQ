namespace FamilyHQ.WebUi.Services.Auth;

/// <summary>
/// Claims decoded client-side from the stored FamilyHQ JWT (FHQ-126).
/// A null <paramref name="ExpiresAtUtc"/> means the exp claim was missing or unreadable —
/// callers must treat that as "expiring" rather than "never expires".
/// </summary>
public sealed record JwtTokenClaims(string? UserId, string? Username, DateTimeOffset? ExpiresAtUtc);
