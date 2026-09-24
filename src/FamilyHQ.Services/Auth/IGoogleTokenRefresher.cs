namespace FamilyHQ.Services.Auth;

/// <summary>
/// The Google refresh-grant seam (FHQ-139), implemented by <see cref="GoogleAuthService"/>.
/// <para>
/// It exists so that callers which only need "turn this stored refresh token into an access token" can
/// depend on that one operation — and be unit-tested without an <c>HttpClient</c> — while there remains
/// exactly ONE refresh implementation. Anything needing a second one is a bug, not a new interface.
/// </para>
/// </summary>
public interface IGoogleTokenRefresher
{
    /// <summary>
    /// Exchanges a refresh token for a fresh access token, persisting a rotated refresh token if Google
    /// supplies one (FHQ-86 — that save uses the CURRENT-USER token store overload, so callers must
    /// ensure an ambient user is resolvable).
    /// </summary>
    /// <exception cref="GoogleReauthRequiredException">Google reports the grant revoked or invalid.</exception>
    Task<(string AccessToken, int ExpiresIn)> RefreshAccessTokenAsync(
        string refreshToken, CancellationToken ct = default);
}
