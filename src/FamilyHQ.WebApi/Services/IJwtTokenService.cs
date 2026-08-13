namespace FamilyHQ.WebApi.Services;

/// <summary>
/// Mints the FamilyHQ API JWT. Single owner of the claims + lifetime policy so the login
/// callback and the renew-jwt endpoint (FHQ-126) cannot drift apart.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generates a signed JWT for the given user. The optional email lands in the "name" claim.
    /// The auth_time claim records the ORIGINAL authentication instant: omit
    /// <paramref name="authTime"/> on first mint (login) to stamp "now"; renewal passes the
    /// original value through so the absolute session-age cap cannot be reset by renewing.
    /// </summary>
    string GenerateToken(string userId, string? email, DateTimeOffset? authTime = null);
}
