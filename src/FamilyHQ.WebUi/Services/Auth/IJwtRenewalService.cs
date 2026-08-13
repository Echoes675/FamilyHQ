namespace FamilyHQ.WebUi.Services.Auth;

/// <summary>
/// Keeps the stored FamilyHQ JWT fresh (FHQ-126): checks the token's remaining lifetime at app
/// startup and on a periodic tick, renewing via the authenticated renew-jwt endpoint while the
/// current token is still valid. Renewal failures never sign the kiosk out — the old token keeps
/// working and the next tick retries.
/// </summary>
public interface IJwtRenewalService : IAsyncDisposable
{
    /// <summary>Runs one immediate check, then starts the periodic background check loop.</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Renews the token if its remaining lifetime is below the configured threshold
    /// (a missing/unreadable exp claim counts as expiring). Returns true if a new token was stored.
    /// </summary>
    Task<bool> CheckAndRenewAsync(CancellationToken ct = default);

    /// <summary>
    /// Unconditionally attempts a renewal with the currently stored token (used by the 401
    /// retry path). Returns the new token on success, or null on failure — the old token is
    /// always kept on failure.
    /// </summary>
    Task<string?> RenewNowAsync(CancellationToken ct = default);
}
