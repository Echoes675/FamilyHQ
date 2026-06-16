using System.Net.Http.Json;
using System.Text.Json;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Auth;

public class GoogleAuthService
{
    private readonly HttpClient _httpClient;
    private readonly GoogleCalendarOptions _options;
    private readonly ILogger<GoogleAuthService> _logger;
    private readonly IIdTokenValidator _idTokenValidator;

    public GoogleAuthService(
        HttpClient httpClient,
        IOptions<GoogleCalendarOptions> options,
        ILogger<GoogleAuthService> logger,
        IIdTokenValidator idTokenValidator)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _idTokenValidator = idTokenValidator;
    }

    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        var query = "?client_id=" + Uri.EscapeDataString(_options.ClientId)
            + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
            + "&response_type=code"
            + "&scope=" + Uri.EscapeDataString("openid email " + GoogleScopes.Calendar)
            + "&access_type=offline"
            + "&prompt=consent"
            + "&state=" + Uri.EscapeDataString(state);
        return _options.AuthPromptUrl + query;
    }

    public async Task<(string AccessToken, string? RefreshToken, string? UserId, string? Email, string? GrantedScope)> ExchangeCodeForTokenAsync(string code, string redirectUri, CancellationToken ct = default)
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
            var body = await response.Content.ReadAsStringAsync(ct);
            var (error, description) = ParseOAuthError(body);

            // Raw OAuth response body is intentionally never logged — only the parsed Google error codes.
            _logger.LogError(
                "Failed to exchange code for token. Status: {Status} Error: {Error} Description: {Description}",
                response.StatusCode, error, description);

            throw new InvalidOperationException(
                $"Failed to exchange code. Status: {response.StatusCode}. Error: {error ?? "<none>"}.");
        }

        var result = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        var claims = await _idTokenValidator.ValidateAsync(result!.IdToken ?? string.Empty, ct);
        _logger.LogInformation("Google granted scopes on code exchange: {GrantedScope}", result.Scope ?? "(none)");
        return (result.AccessToken, result.RefreshToken, claims.Sub, claims.Email, result.Scope);
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
            var body = await response.Content.ReadAsStringAsync(ct);
            var (error, description) = ParseOAuthError(body);

            // Refresh-token value is intentionally never logged — only the parsed Google error.
            _logger.LogError(
                "Failed to refresh token. Status: {Status} Error: {Error} Description: {Description}",
                response.StatusCode, error, description);

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest
                && error is "invalid_grant" or "unauthorized_client" or "invalid_token")
            {
                throw new GoogleReauthRequiredException(
                    GoogleAuthFailureSource.TokenRefresh,
                    description,
                    body);
            }

            throw new InvalidOperationException(
                $"Failed to refresh token. Status: {response.StatusCode}. Error: {error ?? "<none>"}. Description: {description ?? "<none>"}.");
        }

        var result = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        _logger.LogInformation("Google granted scopes on token refresh: {GrantedScope}", result!.Scope ?? "(none)");
        return result.AccessToken;
    }

    private static (string? Error, string? Description) ParseOAuthError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            var description = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            return (error, description);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
