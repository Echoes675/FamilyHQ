using System.Collections.Concurrent;
using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Services.Auth;

/// <summary>
/// In-memory, singleton implementation of <see cref="IAccessTokenCache"/> (FHQ-82). Holds a per-user
/// gate + cached token; the fast path is lock-free, refreshes run one-at-a-time per user.
/// </summary>
public sealed class AccessTokenCache(TimeProvider timeProvider) : IAccessTokenCache
{
    private const int SkewSeconds = 60;

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt);

    private sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public volatile CachedToken? Current;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<string> GetOrRefreshAsync(
        string userId, Func<CancellationToken, Task<(string Token, int ExpiresInSeconds)>> refresh, CancellationToken ct = default)
    {
        var entry = _entries.GetOrAdd(userId, static _ => new Entry());

        // Fast path: a reference read of the immutable snapshot, no lock.
        var cached = entry.Current;
        if (cached is not null && timeProvider.GetUtcNow() < cached.ExpiresAt)
            return cached.Token;

        await entry.Gate.WaitAsync(ct);
        try
        {
            cached = entry.Current; // double-checked: a concurrent caller may have just refreshed
            if (cached is not null && timeProvider.GetUtcNow() < cached.ExpiresAt)
                return cached.Token;

            var (token, expiresIn) = await refresh(ct);
            var expiresAt = timeProvider.GetUtcNow() + TimeSpan.FromSeconds(expiresIn) - TimeSpan.FromSeconds(SkewSeconds);
            entry.Current = new CachedToken(token, expiresAt);
            return token;
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public void Evict(string userId)
    {
        if (_entries.TryGetValue(userId, out var entry))
            entry.Current = null;
    }
}
