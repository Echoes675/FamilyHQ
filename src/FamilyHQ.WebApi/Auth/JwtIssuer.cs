using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace FamilyHQ.WebApi.Auth;

/// <summary>
/// Default <see cref="IJwtIssuer"/> implementation that signs tokens with the symmetric key
/// configured under <c>Jwt:SigningKey</c>. Tokens are issued with a 24 hour lifetime and use
/// the FamilyHQ issuer/audience pair.
/// </summary>
public class JwtIssuer : IJwtIssuer
{
    private const string Issuer = "FamilyHQ";
    private const string Audience = "FamilyHQ";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(1);

    private readonly IConfiguration _configuration;

    public JwtIssuer(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    public (string Token, DateTimeOffset ExpiresAt) Issue(string userId, string? email = null)
    {
        var signingKey = _configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("JWT signing key is not configured.");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.UniqueName, userId),
        };
        if (!string.IsNullOrEmpty(email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Name, email));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var serialized = new JwtSecurityTokenHandler().WriteToken(token);
        return (serialized, expiresAt);
    }
}
