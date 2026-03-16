namespace FamilyHQ.WebUi.Services.Auth;

/// <summary>
/// Interface for managing authentication state in Blazor WASM.
/// Provides methods to check authentication status and get user information from JWT tokens.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Checks if the user is authenticated based on token presence and validity.
    /// </summary>
    Task<bool> IsAuthenticatedAsync();

    /// <summary>
    /// Gets the user ID from the JWT token's "sub" claim.
    /// </summary>
    Task<string?> GetUserIdAsync();

    /// <summary>
    /// Gets the username from the JWT token's "name" or "unique_name" claim.
    /// </summary>
    Task<string?> GetUsernameAsync();

    /// <summary>
    /// Signs out the user by clearing the token from storage.
    /// </summary>
    Task SignOutAsync();
}
