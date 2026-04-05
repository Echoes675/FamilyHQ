using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Auth;

public class GoogleAuthService
{
    private readonly HttpClient _httpClient;
    private readonly GoogleCalendarOptions _options;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(
        HttpClient httpClient, 
        IOptions<GoogleCalendarOptions> options,
        ILogger<GoogleAuthService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string GetAuthorizationUrl(string redirectUri)
    {
        var query = "?client_id=" + Uri.EscapeDataString(_options.ClientId)
            + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
            + "&response_type=code"
            + "&scope=" + Uri.EscapeDataString("openid email https://www.googleapis.com/auth/calendar")
            + "&access_type=offline"
            + "&prompt=consent";
        return _options.AuthPromptUrl + query;
    }

    public async Task<(string AccessToken, string? RefreshToken, string? UserId, string? Email)> ExchangeCodeForTokenAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        });

        var endpoint = $"{_options.AuthBaseUrl}/token";
        var response = await _httpClient.PostAsync(endpoint, request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Failed to exchange code for token: {Error}", error);
            throw new InvalidOperationException($"Failed to exchange code. Status: {response.StatusCode}");
        }

        var result = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        var (userId, email) = ExtractClaimsFromIdToken(result!.IdToken);
        return (result.AccessToken, result.RefreshToken, userId, email);
    }

    private static (string? sub, string? email) ExtractClaimsFromIdToken(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken)) return (null, null);
        var parts = idToken.Split('.');
        if (parts.Length < 2) return (null, null);
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        var sub = root.TryGetProperty("sub", out var subEl) ? subEl.GetString() : null;
        var email = root.TryGetProperty("email", out var emailEl) ? emailEl.GetString() : null;
        return (sub, email);
    }

    public async Task<string> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        });

        var endpoint = $"{_options.AuthBaseUrl}/token";
        var response = await _httpClient.PostAsync(endpoint, request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Failed to refresh token: {Error}", error);
            throw new InvalidOperationException($"Failed to refresh token. Status: {response.StatusCode}");
        }

        var result = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        return result!.AccessToken;
    }

}
