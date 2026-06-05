namespace FamilyHQ.Core.Models;

/// <summary>
/// A user that has a stored token, paired with that token's authentication status.
/// Lets callers decide sync eligibility without a per-user round trip.
/// </summary>
public record UserAuthState(string UserId, TokenAuthStatus AuthStatus);
