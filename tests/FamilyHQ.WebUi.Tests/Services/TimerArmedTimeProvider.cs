using Microsoft.Extensions.Time.Testing;

namespace FamilyHQ.WebUi.Tests.Services;

/// <summary>
/// Delegates every clock operation to a <see cref="FakeTimeProvider"/> and records the moment the
/// code under test arms a timer, so a test can hold each advance until the system under test has
/// provably entered its next delay (FHQ-158).
/// <para>
/// Advancing a fake clock that has no timer armed is a silent no-op: the advance is lost, the system
/// under test then arms its delay against the already-advanced clock, and the following wait fails at
/// its deadline for reasons that look nothing like the cause. That is issue 10 in
/// <c>.agent/docs/intermittent-issues.md</c> — it turned master build #59 red on byte-identical
/// source. Settling on a yield budget only narrows the window; observing the registration closes it.
/// </para>
/// <para>
/// Duplicated (rather than shared) with the copy in <c>FamilyHQ.Services.Tests</c>: there is no
/// shared test-infra assembly, and the CI unit-test stage runs <c>tests/*/*.csproj</c>, so adding one
/// would change the build shape for ~40 lines.
/// </para>
/// </summary>
internal sealed class TimerArmedTimeProvider(FakeTimeProvider inner) : TimeProvider
{
    /// <summary>Real-clock tripwire, reached only when the expected timer is never armed at all.</summary>
    private static readonly TimeSpan ArmedDeadline = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private TaskCompletionSource _armed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _created;
    private int _observed;
    private TimeSpan _lastDueTime = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// How long the most recently armed timer was set for — i.e. what the code under test asked to
    /// sleep. Lets a test assert the requested duration outright instead of inferring it from how
    /// far the clock can be advanced without the operation completing, which is only observable
    /// through a queued continuation.
    /// </summary>
    public TimeSpan LastTimerDueTime
    {
        get
        {
            lock (_gate)
            {
                return _lastDueTime;
            }
        }
    }

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

    public override long GetTimestamp() => inner.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = inner.CreateTimer(callback, state, dueTime, period);

        // Counted only AFTER the inner call returns, so the timer is registered with the fake clock
        // before any observer can act on the count — an advance released by this signal can never
        // outrun the registration it was waiting for.
        lock (_gate)
        {
            _created++;
            _lastDueTime = dueTime;
            _armed.TrySetResult();
        }

        return timer;
    }

    /// <summary>
    /// Advances the fake clock without waiting. Use only where the timer this advance acts on is
    /// already accounted for — e.g. the second half of a "nothing fires just before the delay, it
    /// fires at the delay" pair, which splits ONE armed timer across TWO advances.
    /// </summary>
    public void Advance(TimeSpan delta) => inner.Advance(delta);

    /// <summary>
    /// Advances the fake clock once the system under test has armed a timer that no earlier advance
    /// has consumed. Wall-clock cost is nil on the success path (the timer is normally already
    /// armed); the deadline only bounds the failure path.
    /// </summary>
    public async Task AdvanceOnNextTimerAsync(TimeSpan delta)
    {
        await WaitForNextTimerAsync();
        Advance(delta);
    }

    /// <summary>
    /// Waits until a timer has been armed that no earlier wait or advance has consumed, then
    /// consumes it. Useful on its own as a barrier: the system under test can only re-arm after it
    /// has finished handling the previous timer, so this also proves that handling completed.
    /// </summary>
    public async Task WaitForNextTimerAsync()
    {
        while (true)
        {
            Task armed;
            lock (_gate)
            {
                if (_created > _observed)
                {
                    _observed = _created;
                    return;
                }

                if (_armed.Task.IsCompleted)
                {
                    _armed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                armed = _armed.Task;
            }

            try
            {
                await armed.WaitAsync(ArmedDeadline);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException(
                    $"No timer was armed on the fake clock within {ArmedDeadline.TotalSeconds:0}s; " +
                    "the system under test never entered its next delay.");
            }
        }
    }
}
