namespace FamilyHQ.Core.Interfaces;

/// <summary>
/// Per-user cache of Google OAuth access tokens with single-flight refresh (FHQ-82). Concurrent
/// callers for the same user share one in-flight refresh; a valid cached token is reused until near expiry.
/// </summary>
public interface IAccessTokenCache
{
    /// <summary>
    /// Returns a valid cached access token for the user, or runs <paramref name="refresh"/> once
    /// (serialised per user) and caches its result with the returned expires_in (minus a skew margin).
    /// </summary>
    Task<string> GetOrRefreshAsync(
        string userId,
        Func<CancellationToken, Task<(string Token, int ExpiresInSeconds)>> refresh,
        CancellationToken ct = default);

    /// <summary>Drops any cached token for the user, forcing the next call to refresh.</summary>
    void Evict(string userId);
}
