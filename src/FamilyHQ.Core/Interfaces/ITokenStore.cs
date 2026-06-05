using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ITokenStore
{
    Task<string?> GetRefreshTokenAsync(CancellationToken ct = default);
    Task SaveRefreshTokenAsync(string refreshToken, CancellationToken ct = default);

    // Overloads that accept explicit user ID (for use during OAuth callback before user is authenticated)
    Task<string?> GetRefreshTokenAsync(string userId, CancellationToken ct = default);
    Task SaveRefreshTokenAsync(string refreshToken, string userId, CancellationToken ct = default);

    /// <summary>Returns the distinct user IDs of all users who have stored tokens.</summary>
    Task<IEnumerable<string>> GetAllUserIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns each user that has a stored token paired with that token's auth status, so callers
    /// can decide sync eligibility (e.g. skip accounts needing re-auth) without a per-user round trip.
    /// </summary>
    Task<IReadOnlyList<UserAuthState>> GetAllUserAuthStatesAsync(CancellationToken ct = default);

    /// <summary>
    /// Marks the user's token as needing re-consent and records the failure reason.
    /// </summary>
    Task MarkNeedsReauthAsync(string userId, string? errorDescription, CancellationToken ct = default);

    /// <summary>
    /// Returns the current authentication status for the user's stored token.
    /// </summary>
    Task<AuthStatusResult> GetAuthStatusAsync(string userId, CancellationToken ct = default);
}
