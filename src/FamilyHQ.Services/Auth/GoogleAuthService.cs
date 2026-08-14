using System.Net.Http.Json;
using System.Text.Json;
using FamilyHQ.Core.Interfaces;
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
    private readonly ITokenStore _tokenStore;

    public GoogleAuthService(
        HttpClient httpClient,
        IOptions<GoogleCalendarOptions> options,
        ILogger<GoogleAuthService> logger,
        IIdTokenValidator idTokenValidator,
        ITokenStore tokenStore)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _idTokenValidator = idTokenValidator;
        _tokenStore = tokenStore;
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

    public async Task<(string AccessToken, int ExpiresIn)> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default)
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
                // FHQ-88: only the parsed error_description travels on the exception — the raw
                // token-endpoint body is discarded here and must never be retained or logged.
                throw new GoogleReauthRequiredException(
                    GoogleAuthFailureSource.TokenRefresh,
                    description);
            }

            throw new InvalidOperationException(
                $"Failed to refresh token. Status: {response.StatusCode}. Error: {error ?? "<none>"}. Description: {description ?? "<none>"}.");
        }

        var result = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        _logger.LogInformation("Google granted scopes on token refresh: {GrantedScope}", result!.Scope ?? "(none)");
        await PersistRotatedRefreshTokenAsync(result.RefreshToken);
        return (result.AccessToken, result.ExpiresIn);
    }

    /// <summary>
    /// FHQ-86: Google may rotate the refresh token in a refresh-grant response. A rotated token must
    /// be persisted before the access token is returned — discarding it leaves the stored token stale,
    /// so the next refresh fails with invalid_grant and forces a needless re-consent. The save uses the
    /// current-user <see cref="ITokenStore"/> overload: refresh always runs in a context where
    /// ICurrentUserService resolves the user (request JWT sub, or BackgroundUserContext during sync) —
    /// the same ambient identity the token store used to read the refresh token being replaced.
    /// </summary>
    private async Task PersistRotatedRefreshTokenAsync(string? rotatedRefreshToken)
    {
        if (string.IsNullOrEmpty(rotatedRefreshToken)) return;

        try
        {
            // CancellationToken.None is deliberate: once the rotated token is in hand, a cancelled
            // request must not abort the save — losing the replacement token orphans the session.
            await _tokenStore.SaveRefreshTokenAsync(rotatedRefreshToken, CancellationToken.None);

            // Token values are never logged — length only.
            _logger.LogInformation(
                "Google rotated the refresh token on refresh; replacement persisted (length {TokenLength}).",
                rotatedRefreshToken.Length);
        }
        catch (Exception ex)
        {
            // Deliberate log-and-continue: the access token just issued is valid regardless, so the
            // in-flight sync should proceed. If Google's rotation already invalidated the old stored
            // token, the next refresh fails with invalid_grant and the FHQ-85 reauth machinery
            // surfaces the reconnect banner; rethrowing here would only fail work we can complete.
            _logger.LogError(ex,
                "Failed to persist rotated refresh token (length {TokenLength}); the stored refresh token may now be stale and the next refresh may require re-consent.",
                rotatedRefreshToken.Length);
        }
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
