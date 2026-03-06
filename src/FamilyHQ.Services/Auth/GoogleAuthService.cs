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

    public async Task<(string AccessToken, string? RefreshToken)> ExchangeCodeForTokenAsync(string code, string redirectUri, CancellationToken ct = default)
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
        return (result!.AccessToken, result.RefreshToken);
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

    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }
}
