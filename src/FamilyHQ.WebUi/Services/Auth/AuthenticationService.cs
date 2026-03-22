using System.Text;
using System.Text.Json;
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
            var (userId, username) = DecodeJwtToken(token);
            
            if (!string.IsNullOrEmpty(userId))
            {
                _cachedUserId = userId;
                _cachedUsername = username;
                _isAuthenticated = true;
            }
        }

        _isInitialized = true;
    }

    /// <summary>
    /// Decodes a JWT token and extracts the user ID and username claims.
    /// </summary>
    private static (string? userId, string? username) DecodeJwtToken(string token)
    {
        try
        {
            // JWT format: header.payload.signature
            var parts = token.Split('.');
            if (parts.Length != 3)
                return (null, null);

            // Decode the payload (second part)
            var payload = parts[1];
            
            // Add padding if needed
            var padding = 4 - (payload.Length % 4);
            if (padding != 4)
                payload += new string('=', padding);

            var jsonBytes = Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/'));
            var jsonString = Encoding.UTF8.GetString(jsonBytes);

            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            // Extract "sub" claim (user ID)
            string? userId = null;
            if (root.TryGetProperty("sub", out var subElement))
            {
                userId = subElement.GetString();
            }

            // Extract username - try "name" first, then "unique_name"
            string? username = null;
            if (root.TryGetProperty("name", out var nameElement))
            {
                username = nameElement.GetString();
            }
            
            if (string.IsNullOrEmpty(username) && root.TryGetProperty("unique_name", out var uniqueNameElement))
            {
                username = uniqueNameElement.GetString();
            }

            return (userId, username);
        }
        catch
        {
            // If decoding fails, return null values
            return (null, null);
        }
    }
}
