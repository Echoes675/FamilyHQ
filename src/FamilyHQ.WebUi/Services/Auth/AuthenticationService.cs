using FamilyHQ.WebUi.Services.Correlation;

namespace FamilyHQ.WebUi.Services.Auth;

/// <summary>
/// Service for managing authentication state in Blazor WASM.
/// Decodes JWT tokens and provides user information.
/// </summary>
public class AuthenticationService : IAuthenticationService
{
    private readonly IAuthTokenStore _tokenStore;
    private readonly ICorrelationIdTokenStore _correlationStore;
    
    private string? _cachedUserId;
    private string? _cachedUsername;
    private bool _isAuthenticated;
    private bool _isInitialized;

    public AuthenticationService(IAuthTokenStore tokenStore, ICorrelationIdTokenStore correlationStore)
    {
        _tokenStore = tokenStore;
        _correlationStore = correlationStore;
    }

    /// <summary>
    /// Checks if the user is authenticated based on token presence and validity.
    /// </summary>
    public async Task<bool> IsAuthenticatedAsync()
    {
        await EnsureInitializedAsync();
        return _isAuthenticated;
    }

    /// <summary>
    /// Gets the user ID from the JWT token's "sub" claim.
    /// </summary>
    public async Task<string?> GetUserIdAsync()
    {
        await EnsureInitializedAsync();
        return _cachedUserId;
    }

    /// <summary>
    /// Gets the username from the JWT token's "name" or "unique_name" claim.
    /// </summary>
    public async Task<string?> GetUsernameAsync()
    {
        await EnsureInitializedAsync();
        return _cachedUsername;
    }

    /// <summary>
    /// Signs out the user by clearing the token from localStorage.
    /// </summary>
    public async Task SignOutAsync()
    {
        await _tokenStore.ClearTokenAsync();
        await _correlationStore.ClearSessionCorrelationIdAsync();
        _cachedUserId = null;
        _cachedUsername = null;
        _isAuthenticated = false;
    }

    /// <summary>
    /// Initializes the authentication state by checking and decoding the token.
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized)
            return;

        var token = await _tokenStore.GetTokenAsync();

        if (!string.IsNullOrEmpty(token))
        {
            // Shared decoder (FHQ-126) — also used by JwtRenewalService for the exp claim.
            var claims = JwtTokenDecoder.Decode(token);

            if (!string.IsNullOrEmpty(claims.UserId))
            {
                _cachedUserId = claims.UserId;
                _cachedUsername = claims.Username;
                _isAuthenticated = true;
            }
        }

        _isInitialized = true;
    }
}
