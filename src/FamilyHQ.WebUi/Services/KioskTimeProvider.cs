namespace FamilyHQ.WebUi.Services;

/// <summary>
/// Wraps a <see cref="TimeProvider"/> so the displayed "today" can be advanced by whole days
/// in lower environments — letting E2E exercise the kiosk day-rollover (FHQ-63) without
/// waiting for real midnight. In production <paramref name="overrideEnabled"/> is false, the
/// offset stays zero, and this behaves identically to the wrapped provider.
/// </summary>
public sealed class KioskTimeProvider : TimeProvider
{
    private readonly TimeProvider _inner;
    private readonly bool _overrideEnabled;
    private int _dayOffset;

    public KioskTimeProvider(TimeProvider inner, bool overrideEnabled)
    {
        _inner = inner;
        _overrideEnabled = overrideEnabled;
    }

    /// <summary>True when the day offset may be changed (lower environments only).</summary>
    public bool OverrideEnabled => _overrideEnabled;

    public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow().AddDays(_dayOffset);

    // Local-now and timezone derive from the wrapped provider; only the date is shifted.
    public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;
    public override long GetTimestamp() => _inner.GetTimestamp();
    public override long TimestampFrequency => _inner.TimestampFrequency;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => _inner.CreateTimer(callback, state, dueTime, period);

    /// <summary>Advances the displayed date by whole days. No-op unless the override is enabled.</summary>
    public void AdvanceDays(int days)
    {
        if (!_overrideEnabled) return;
        _dayOffset += days;
    }

    /// <summary>Clears the day offset.</summary>
    public void Reset() => _dayOffset = 0;
}
