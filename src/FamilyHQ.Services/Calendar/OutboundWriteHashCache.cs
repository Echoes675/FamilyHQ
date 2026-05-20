using System.Collections.Concurrent;

namespace FamilyHQ.Services.Calendar;

internal sealed class OutboundWriteHashCache : IOutboundWriteHashCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<(string Id, string Hash), DateTimeOffset> _entries = new();
    private readonly TimeProvider _clock;

    public OutboundWriteHashCache(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public void Record(string googleEventId, string contentHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(googleEventId);
        ArgumentException.ThrowIfNullOrEmpty(contentHash);

        _entries[(googleEventId, contentHash)] = _clock.GetUtcNow();
        EvictExpired();
    }

    // Eager eviction on every Record call: bounded N (handful per minute at family-app volume)
    // avoids needing a background timer or size cap.

    public bool WasRecentlyWritten(string googleEventId, string contentHash)
    {
        if (string.IsNullOrEmpty(googleEventId) || string.IsNullOrEmpty(contentHash))
            return false;

        if (!_entries.TryGetValue((googleEventId, contentHash), out var written))
            return false;

        if (_clock.GetUtcNow() - written > Ttl)
        {
            _entries.TryRemove((googleEventId, contentHash), out _);
            return false;
        }

        return true;
    }

    private void EvictExpired()
    {
        var cutoff = _clock.GetUtcNow() - Ttl;
        foreach (var kv in _entries)
        {
            if (kv.Value < cutoff)
                _entries.TryRemove(kv.Key, out _);
        }
    }
}
