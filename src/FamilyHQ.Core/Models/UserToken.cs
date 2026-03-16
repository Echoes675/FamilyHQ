namespace FamilyHQ.Core.Models;

/// <summary>
/// Stores OAuth tokens (refresh and access) per user with encryption support.
/// Used by DatabaseTokenStore to replace file-based token storage.
/// </summary>
public class UserToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// The user ID this token belongs to (from Google OAuth / Claims.NameIdentifier)
    /// </summary>
    public string UserId { get; set; } = null!;
    
    /// <summary>
    /// The OAuth provider (e.g., "Google")
    /// </summary>
    public string Provider { get; set; } = null!;
    
    /// <summary>
    /// Encrypted refresh token (encrypted using ASP.NET Core Data Protection)
    /// </summary>
    public string RefreshToken { get; set; } = null!;
    
    /// <summary>
    /// Encrypted access token (encrypted using ASP.NET Core Data Protection)
    /// </summary>
    public string? AccessToken { get; set; }
    
    /// <summary>
    /// When the access token expires (if stored)
    /// </summary>
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    
    /// <summary>
    /// When the token was first created
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
    
    /// <summary>
    /// When the token was last updated
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
