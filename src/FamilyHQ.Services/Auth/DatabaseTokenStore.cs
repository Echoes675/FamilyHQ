using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Auth;

/// <summary>
/// Database-backed implementation of ITokenStore that stores OAuth tokens per user.
/// Uses ASP.NET Core Data Protection for encryption at rest.
/// Uses ICurrentUserService to get the current user ID.
/// </summary>
public class DatabaseTokenStore : ITokenStore
{
    private readonly FamilyHqDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDataProtector _dataProtector;
    private readonly ILogger<DatabaseTokenStore> _logger;
    private readonly string _provider;
    
    /// <summary>
    /// Default OAuth provider
    /// </summary>
    private const string DefaultProvider = "Google";

    /// <summary>
    /// Use SemaphoreSlim for async-compatible locking
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DatabaseTokenStore(
        FamilyHqDbContext dbContext,
        ICurrentUserService currentUserService,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<DatabaseTokenStore> logger,
        string provider = DefaultProvider)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _logger = logger;
        _provider = provider;
        
        // Create a purpose-specific data protector for tokens
        _dataProtector = dataProtectionProvider.CreateProtector("FamilyHQ.Tokens");
    }

    public async Task<string?> GetRefreshTokenAsync(CancellationToken ct = default)
    {
        var userId = _currentUserService.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("GetRefreshTokenAsync called but no user ID available");
            return null;
        }

        await _lock.WaitAsync(ct);
        try
        {
            var userToken = await _dbContext.UserTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == _provider, ct);

            if (userToken == null)
            {
                _logger.LogDebug("No refresh token found for user {UserId}", userId);
                return null;
            }

            // Decrypt the stored token
            var decryptedToken = _dataProtector.Unprotect(userToken.RefreshToken);
            _logger.LogDebug("Retrieved refresh token for user {UserId}", userId);
            return decryptedToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving refresh token for user {UserId}", userId);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Get refresh token for a specific user (used during authenticated operations)
    /// </summary>
    public async Task<string?> GetRefreshTokenAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("GetRefreshTokenAsync called but no user ID provided");
            return null;
        }

        await _lock.WaitAsync(ct);
        try
        {
            var userToken = await _dbContext.UserTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == _provider, ct);

            if (userToken == null)
            {
                _logger.LogDebug("No refresh token found for user {UserId}", userId);
                return null;
            }

            // Decrypt the stored token
            var decryptedToken = _dataProtector.Unprotect(userToken.RefreshToken);
            _logger.LogDebug("Retrieved refresh token for user {UserId}", userId);
            return decryptedToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving refresh token for user {UserId}", userId);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveRefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);
        
        var userId = _currentUserService.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            throw new InvalidOperationException("Cannot save refresh token: no user ID available");
        }

        await _lock.WaitAsync(ct);
        try
        {
            // Encrypt the token before storing
            var encryptedToken = _dataProtector.Protect(refreshToken);

            var existingToken = await _dbContext.UserTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == _provider, ct);

            if (existingToken != null)
            {
                // Update existing token
                _logger.LogDebug("Updating existing refresh token for user {UserId}", userId);
                existingToken.RefreshToken = encryptedToken;
                existingToken.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                // Insert new token
                _logger.LogDebug("Creating new refresh token for user {UserId}", userId);
                var userToken = new UserToken
                {
                    UserId = userId,
                    Provider = _provider,
                    RefreshToken = encryptedToken,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _dbContext.UserTokens.Add(userToken);
            }

            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("Saved refresh token for user {UserId}", userId);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Save refresh token for a specific user (used during OAuth callback)
    /// </summary>
    public async Task SaveRefreshTokenAsync(string refreshToken, string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);
        
        if (string.IsNullOrEmpty(userId))
        {
            throw new InvalidOperationException("Cannot save refresh token: no user ID provided");
        }

        await _lock.WaitAsync(ct);
        try
        {
            // Encrypt the token before storing
            var encryptedToken = _dataProtector.Protect(refreshToken);

            var existingToken = await _dbContext.UserTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == _provider, ct);

            if (existingToken != null)
            {
                // Update existing token
                _logger.LogDebug("Updating existing refresh token for user {UserId}", userId);
                existingToken.RefreshToken = encryptedToken;
                existingToken.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                // Insert new token
                _logger.LogDebug("Creating new refresh token for user {UserId}", userId);
                var userToken = new UserToken
                {
                    UserId = userId,
                    Provider = _provider,
                    RefreshToken = encryptedToken,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _dbContext.UserTokens.Add(userToken);
            }

            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("Saved refresh token for user {UserId}", userId);
        }
        finally
        {
            _lock.Release();
        }
    }
}
