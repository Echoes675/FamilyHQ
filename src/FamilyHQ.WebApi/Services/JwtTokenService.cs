using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace FamilyHQ.WebApi.Services;

/// <summary>
/// Mints the FamilyHQ API JWT. Extracted from AuthController (FHQ-126) so the login callback
/// and the renew-jwt endpoint share one claims + lifetime policy.
/// </summary>
public class JwtTokenService(IConfiguration configuration) : IJwtTokenService
{
    private const string IssuerAndAudience = "FamilyHQ";
    private const int LifetimeDays = 365;

    public string GenerateToken(string userId, string? email)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.UniqueName, userId)
        };
        if (!string.IsNullOrEmpty(email))
            claims.Add(new Claim(JwtRegisteredClaimNames.Name, email));

        var jwtKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("JWT signing key is not configured.");

        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: IssuerAndAudience,
            audience: IssuerAndAudience,
            claims: claims.ToArray(),
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddDays(LifetimeDays),
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
