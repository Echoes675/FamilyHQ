namespace FamilyHQ.Core.Interfaces;

public interface ITokenStore
{
    Task<string?> GetRefreshTokenAsync(CancellationToken ct = default);
    Task SaveRefreshTokenAsync(string refreshToken, CancellationToken ct = default);
    
    // Overloads that accept explicit user ID (for use during OAuth callback before user is authenticated)
    Task<string?> GetRefreshTokenAsync(string userId, CancellationToken ct = default);
    Task SaveRefreshTokenAsync(string refreshToken, string userId, CancellationToken ct = default);
}
