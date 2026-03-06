namespace FamilyHQ.Core.Interfaces;

public interface ITokenStore
{
    Task<string?> GetRefreshTokenAsync(CancellationToken ct = default);
    Task SaveRefreshTokenAsync(string refreshToken, CancellationToken ct = default);
}
