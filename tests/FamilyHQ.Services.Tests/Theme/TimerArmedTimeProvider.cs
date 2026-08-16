using Microsoft.Extensions.Time.Testing;

namespace FamilyHQ.Services.Tests.Theme;

/// <summary>
/// Delegates every clock operation to a <see cref="FakeTimeProvider"/> and signals the moment the code
/// under test arms a timer — i.e. the moment the scheduler enters its boundary delay. That lets a test
/// advance the fake clock at exactly the right point without polling or sleeping, so the
/// delay-elapses path is driven deterministically rather than raced.
/// </summary>
internal sealed class TimerArmedTimeProvider(FakeTimeProvider inner) : TimeProvider
{
    private readonly TaskCompletionSource _armed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes the first time the code under test creates a timer.</summary>
    public Task TimerArmed => _armed.Task;

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

    public override long GetTimestamp() => inner.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = inner.CreateTimer(callback, state, dueTime, period);
        _armed.TrySetResult();
        return timer;
    }
}
