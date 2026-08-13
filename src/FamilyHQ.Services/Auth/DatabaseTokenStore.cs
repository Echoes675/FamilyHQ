using System.Data.Common;
using System.Security.Cryptography;
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
    private readonly IAccessTokenCache _accessTokenCache;
    private readonly string _provider;

    /// <summary>
    /// Default OAuth provider
    /// </summary>
    private const string DefaultProvider = "Google";

    /// <summary>
    /// Reason persisted when a stored refresh token cannot be decrypted (FHQ-90) — e.g. after a
    /// Data Protection key-ring rotation/loss. Distinguishes an unreadable token from "never connected".
    /// </summary>
    internal const string DecryptionFailedReason =
        "DecryptionFailed: the stored Google connection could not be read — reconnect your Google account.";

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
        IAccessTokenCache accessTokenCache,
        string provider = DefaultProvider)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _logger = logger;
        _connectionStatusBroadcaster = connectionStatusBroadcaster;
        _accessTokenCache = accessTokenCache;
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

        return await GetRefreshTokenInternalAsync(userId, ct);
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

        return await GetRefreshTokenInternalAsync(userId, ct);
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

    public async Task<IReadOnlyList<UserAuthState>> GetAllUserAuthStatesAsync(CancellationToken ct = default)
    {
        return await _dbContext.UserTokens
            .Where(t => t.Provider == _provider)
            .Select(t => new UserAuthState(t.UserId, t.AuthStatus))
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
            await ExecuteWithConcurrencyRetryAsync(async token =>
            {
                broadcast = false; // recompute per attempt

                var existingToken = await _dbContext.UserTokens
                    .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == _provider, token);

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

                await _dbContext.SaveChangesAsync(token);
                _logger.LogWarning(
                    "Marked user {UserId} token as NeedsReauth ({ErrorDescription})",
                    userId, existingToken.LastAuthErrorDescription);
                broadcast = true;
            }, ct);
        }
        finally
        {
            _lock.Release();
        }

        // A revoked/expired refresh token means any cached access token is no longer trustworthy
        // (or is about to fail anyway) — evict so the next caller forces a fresh refresh attempt
        // instead of reusing a soon-to-be-rejected cached token.
        _accessTokenCache.Evict(userId);

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

    /// <summary>
    /// Reads and decrypts the stored refresh token (FHQ-90 failure semantics):
    /// <list type="bullet">
    /// <item>No row → null ("never connected").</item>
    /// <item>Unreadable ciphertext (key-ring rotation/loss, corrupt column) → persist NeedsReauth with
    /// <see cref="DecryptionFailedReason"/>, log Error, THEN return null — callers' null-handling routes
    /// the user to re-consent, which self-heals by replacing the unreadable token.</item>
    /// <item>DB failure → log and rethrow; a DB blip must never impersonate "not connected".</item>
    /// </list>
    /// </summary>
    private async Task<string?> GetRefreshTokenInternalAsync(string userId, CancellationToken ct)
    {
        UserToken? userToken = null;
        var decryptionFailed = false;

        await _lock.WaitAsync(ct);
        try
        {
            userToken = await _dbContext.UserTokens
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
        catch (DbException ex)
        {
            _logger.LogError(ex, "Database error retrieving refresh token for user {UserId}", userId);
            throw;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // CryptographicException: the Data Protection key ring no longer holds the key that
            // protected this payload (rotation/loss) or the payload is tampered. FormatException:
            // the column is not even valid base64url. Both mean the token is unreadable — a
            // fleet-wide key-ring incident must be operator-visible, not look like "never connected".
            _logger.LogError(ex,
                "Failed to decrypt stored refresh token for user {UserId} — Data Protection key ring may have been rotated or lost; marking NeedsReauth",
                userId);
            decryptionFailed = true;
        }
        finally
        {
            _lock.Release();
        }

        // Reached only on decryption failure. Mark AFTER releasing the semaphore
        // (MarkNeedsReauthAsync re-acquires it) and uncancellable — the persisted signal must
        // survive the caller's request being torn down. Skip when the row is already flagged so
        // repeated failed reads (every kiosk poll) don't re-save/re-broadcast; matches the
        // FHQ-85 keep-first-record semantics.
        if (decryptionFailed && userToken is { AuthStatus: not TokenAuthStatus.NeedsReauth })
        {
            await MarkNeedsReauthAsync(userId, DecryptionFailedReason, CancellationToken.None);
        }

        return null;
    }

    private async Task SaveRefreshTokenInternalAsync(string refreshToken, string userId, CancellationToken ct)
    {
        bool broadcast = false;
        await _lock.WaitAsync(ct);
        try
        {
            await ExecuteWithConcurrencyRetryAsync(async token =>
            {
                broadcast = false; // recompute per attempt
                var encryptedToken = _dataProtector.Protect(refreshToken);

                var existingToken = await _dbContext.UserTokens
                    .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == _provider, token);

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
                    _dbContext.UserTokens.Add(new UserToken
                    {
                        UserId = userId,
                        Provider = _provider,
                        RefreshToken = encryptedToken,
                        CreatedAt = now,
                        UpdatedAt = now,
                        AuthStatus = TokenAuthStatus.Active,
                        AuthStatusChangedAt = now
                    });
                    // First-time token creation isn't a transition from NeedsReauth — no broadcast.
                }

                await _dbContext.SaveChangesAsync(token);
            }, ct);

            _logger.LogInformation("Saved refresh token for user {UserId}", userId);
        }
        finally
        {
            _lock.Release();
        }

        // Re-consent (or any refresh-token save) installs a new refresh token, so any access
        // token cached under the old one is stale — evict it so the next caller refreshes.
        _accessTokenCache.Evict(userId);

        if (broadcast)
        {
            // Fire the SignalR notification AFTER releasing the SemaphoreSlim so a slow
            // hub-context send cannot serialise across token-store callers.
            await _connectionStatusBroadcaster.BroadcastConnectionStatusUpdatedAsync(ct);
        }
    }

    /// <summary>
    /// Runs a read-modify-save unit of work, retrying on optimistic-concurrency conflicts (FHQ-119).
    /// On <see cref="DbUpdateConcurrencyException"/> it detaches tracked entities and re-runs the action,
    /// so the retry reloads the winning row and re-applies this caller's mutation against fresh values —
    /// last writer wins, but never a silent lost update. After the cap the exception propagates.
    /// </summary>
    private async Task ExecuteWithConcurrencyRetryAsync(Func<CancellationToken, Task> readModifySave, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await readModifySave(ct);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex,
                    "Optimistic-concurrency conflict saving UserToken (attempt {Attempt} of {MaxAttempts}); reloading and retrying",
                    attempt, maxAttempts);
                _dbContext.ClearTrackedEntities();
            }
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value == null) return null;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
