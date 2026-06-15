using FamilyHQ.Services.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FamilyHQ.Services.Auth;

public sealed class JwksIdTokenValidator : IIdTokenValidator
{
    private readonly GoogleCalendarOptions _options;
    private readonly Lazy<Task<IReadOnlyList<SecurityKey>>> _signingKeys;

    public JwksIdTokenValidator(IHttpClientFactory httpClientFactory, IOptions<GoogleCalendarOptions> options)
    {
        _options = options.Value;
        _signingKeys = new Lazy<Task<IReadOnlyList<SecurityKey>>>(
            () => FetchSigningKeysAsync(httpClientFactory.CreateClient()));
    }

    public async Task<IdTokenClaims> ValidateAsync(string idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(idToken))
            throw new IdTokenValidationException("id_token is missing.");

        var keys = await _signingKeys.Value;
        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = _options.ValidIssuer,
            ValidAudience = _options.ClientId,
            IssuerSigningKeys = keys,
            ValidateLifetime = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.Zero,
        });

        if (!result.IsValid)
            throw new IdTokenValidationException(result.Exception?.Message ?? "Token validation failed.");

        var jwt = (JsonWebToken)result.SecurityToken;
        var email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
        return new IdTokenClaims(jwt.Subject, email);
    }

    private async Task<IReadOnlyList<SecurityKey>> FetchSigningKeysAsync(HttpClient httpClient)
    {
        var json = await httpClient.GetStringAsync(_options.JwksUri);
        return new JsonWebKeySet(json).GetSigningKeys().AsReadOnly();
    }
}
