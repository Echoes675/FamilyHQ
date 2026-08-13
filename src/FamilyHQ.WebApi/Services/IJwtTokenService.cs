namespace FamilyHQ.WebApi.Services;

/// <summary>
/// Mints the FamilyHQ API JWT. Single owner of the claims + lifetime policy so the login
/// callback and the renew-jwt endpoint (FHQ-126) cannot drift apart.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generates a signed JWT for the given user. The optional email lands in the "name" claim.
    /// </summary>
    string GenerateToken(string userId, string? email);
}
