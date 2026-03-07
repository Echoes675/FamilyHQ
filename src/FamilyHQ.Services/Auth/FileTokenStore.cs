using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Services.Auth;

/// <summary>
/// A simple implementation of ITokenStore that persists the Google OAuth2 refresh token.
/// In a production environment, this might store to the database or a key vault.
/// For MVF1, we store it to the local file system (or it could be in-memory/DB).
/// </summary>
public class FileTokenStore : ITokenStore
{
    private readonly string _tokenFilePath;

    public FileTokenStore(string? tokenFilePath = null)
    {
        // For development, store token alongside the application relative path
        _tokenFilePath = tokenFilePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FamilyHQ", "google_refresh_token.txt");
        
        var dir = Path.GetDirectoryName(_tokenFilePath);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir!);
        }
    }

    public async Task<string?> GetRefreshTokenAsync(CancellationToken ct = default)
    {
        if (File.Exists(_tokenFilePath))
        {
            return await File.ReadAllTextAsync(_tokenFilePath, ct);
        }
        return null;
    }

    public async Task SaveRefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(_tokenFilePath, refreshToken, ct);
    }
}
