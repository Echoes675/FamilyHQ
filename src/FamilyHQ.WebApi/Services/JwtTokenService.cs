using FamilyHQ.WebApi.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace FamilyHQ.WebApi.Services;

/// <summary>
/// Mints the FamilyHQ API JWT. Extracted from AuthController (FHQ-126) so the login callback
/// and the renew-jwt endpoint share one claims + lifetime policy.
/// </summary>
public class JwtTokenService(
    IConfiguration configuration,
    JwtSessionOptions sessionOptions,
    TimeProvider timeProvider) : IJwtTokenService
{
    private const string IssuerAndAudience = "FamilyHQ";
    private const int LifetimeDays = 365;

    public string GenerateToken(string userId, string? email, DateTimeOffset? authTime = null)
    {
        var now = timeProvider.GetUtcNow();

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.UniqueName, userId)
        };
        if (!string.IsNullOrEmpty(email))
            claims.Add(new Claim(JwtRegisteredClaimNames.Name, email));

        // auth_time (unix seconds): the ORIGINAL authentication instant. First mint (login
        // callback) stamps "now"; renewal carries the caller-supplied original through, so the
        // absolute session-age cap (JwtSessionOptions) cannot be reset by renewing (FHQ-126).
        var authTimeSeconds = (authTime ?? now).ToUnixTimeSeconds();
        claims.Add(new Claim(JwtRegisteredClaimNames.AuthTime, authTimeSeconds.ToString(), ClaimValueTypes.Integer64));

        // Lifetime: 365 days, but a renewal (authTime supplied) is clamped to the absolute
        // session cap so a token renewed near the cap dies AT the cap — otherwise the effective
        // leaked-token bound would be cap + another full lifetime. Login mints are unclamped.
        var expires = now.AddDays(LifetimeDays);
        if (authTime is not null)
        {
            var sessionCapExpiry = authTime.Value.AddDays(sessionOptions.MaxSessionAgeDays);
            if (sessionCapExpiry < expires)
                expires = sessionCapExpiry;
        }

        var jwtKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("JWT signing key is not configured.");

        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: IssuerAndAudience,
            audience: IssuerAndAudience,
            claims: claims.ToArray(),
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
