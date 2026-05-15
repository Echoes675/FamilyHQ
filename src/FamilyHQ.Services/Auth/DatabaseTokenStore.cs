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
    private readonly IConnectionStatusBroadcaster _connectionStatusBroadcaster;
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
        IConnectionStatusBroadcaster connectionStatusBroadcaster,
        string provider = DefaultProvider)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _logger = logger;
        _connectionStatusBroadcaster = connectionStatusBroadcaster;
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

        await SaveRefreshTokenInternalAsync(refreshToken, userId, ct);
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

        await SaveRefreshTokenInternalAsync(refreshToken, userId, ct);
    }

    public async Task<IEnumerable<string>> GetAllUserIdsAsync(CancellationToken ct = default)
    {
        return await _dbContext.UserTokens
            .Select(t => t.UserId)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task MarkNeedsReauthAsync(string userId, string? errorDescription, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            throw new InvalidOperationException("Cannot mark token as needing re-auth: no user ID provided");
        }

        bool broadcast = false;
        await _lock.WaitAsync(ct);
        try
        {
            var existingToken = await _dbContext.UserTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == _provider, ct);

            if (existingToken == null)
            {
                _logger.LogWarning(
                    "MarkNeedsReauthAsync called for user {UserId} but no token exists",
                    userId);
                return;
            }

            existingToken.AuthStatus = TokenAuthStatus.NeedsReauth;
            existingToken.LastAuthErrorDescription = Truncate(errorDescription, 512);
            existingToken.AuthStatusChangedAt = DateTimeOffset.UtcNow;
            existingToken.UpdatedAt = DateTimeOffset.UtcNow;

            await _dbContext.SaveChangesAsync(ct);
            _logger.LogWarning(
                "Marked user {UserId} token as NeedsReauth ({ErrorDescription})",
                userId, existingToken.LastAuthErrorDescription);
            broadcast = true;
        }
        finally
        {
            _lock.Release();
        }

        if (broadcast)
        {
            // Fire the SignalR notification AFTER releasing the SemaphoreSlim so a slow
            // hub-context send cannot serialise across token-store callers. The DB
            // commit has already succeeded; the broadcast is fire-and-forget from the
            // store's perspective and the IHubContext implementation handles its own
            // queueing if any connected client is slow.
            await _connectionStatusBroadcaster.BroadcastConnectionStatusUpdatedAsync(ct);
        }
    }

    public async Task<AuthStatusResult> GetAuthStatusAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return new AuthStatusResult(TokenAuthStatus.Active, null, null);
        }

        var token = await _dbContext.UserTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == _provider, ct);

        if (token == null)
        {
            return new AuthStatusResult(TokenAuthStatus.Active, null, null);
        }

        return new AuthStatusResult(token.AuthStatus, token.LastAuthErrorDescription, token.AuthStatusChangedAt);
    }

    private async Task SaveRefreshTokenInternalAsync(string refreshToken, string userId, CancellationToken ct)
    {
        bool broadcast = false;
        await _lock.WaitAsync(ct);
        try
        {
            var encryptedToken = _dataProtector.Protect(refreshToken);

            var existingToken = await _dbContext.UserTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == _provider, ct);

            var now = DateTimeOffset.UtcNow;

            if (existingToken != null)
            {
                _logger.LogDebug("Updating existing refresh token for user {UserId}", userId);
                existingToken.RefreshToken = encryptedToken;
                existingToken.UpdatedAt = now;

                // Re-consent restores the token; clear any previous NeedsReauth flag.
                if (existingToken.AuthStatus != TokenAuthStatus.Active
                    || existingToken.LastAuthErrorDescription != null)
                {
                    existingToken.AuthStatus = TokenAuthStatus.Active;
                    existingToken.LastAuthErrorDescription = null;
                    existingToken.AuthStatusChangedAt = now;
                    broadcast = true;
                }
            }
            else
            {
                _logger.LogDebug("Creating new refresh token for user {UserId}", userId);
                var userToken = new UserToken
                {
                    UserId = userId,
                    Provider = _provider,
                    RefreshToken = encryptedToken,
                    CreatedAt = now,
                    UpdatedAt = now,
                    AuthStatus = TokenAuthStatus.Active,
                    AuthStatusChangedAt = now
                };
                _dbContext.UserTokens.Add(userToken);
                // First-time token creation isn't a transition from NeedsReauth — no broadcast.
            }

            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("Saved refresh token for user {UserId}", userId);
        }
        finally
        {
            _lock.Release();
        }

        if (broadcast)
        {
            // Fire the SignalR notification AFTER releasing the SemaphoreSlim so a slow
            // hub-context send cannot serialise across token-store callers.
            await _connectionStatusBroadcaster.BroadcastConnectionStatusUpdatedAsync(ct);
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value == null) return null;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
