using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

/// <summary>
/// The property this type exists for: a rapid tab switch away and back, while a fetch is in flight,
/// must never leave two loops running or orphan one that nothing can stop short of a page reload.
/// Follows <c>PeriodicUiRefreshLoopTests</c>'s shape — <see cref="FakeTimeProvider"/> driving
/// <c>PeriodicTimer</c>, no real timers, a <see cref="SemaphoreSlim"/> to wait for a tick
/// deterministically rather than a sleep.
/// </summary>
public class ReminderTickLoopTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    private static FakeTimeProvider CreateClock() =>
        new(new DateTimeOffset(2026, 8, 13, 6, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task EnsureRunningAsync_TicksOnTheConfiguredPeriod()
    {
        var clock = CreateClock();
        var sut = new ReminderTickLoop();
        using var ticked = new SemaphoreSlim(0);
        var ticks = 0;

        await sut.EnsureRunningAsync(clock, Period, () =>
        {
            Interlocked.Increment(ref ticks);
            ticked.Release();
            return Task.CompletedTask;
        }, NullLogger.Instance);

        clock.Advance(Period);
        (await ticked.WaitAsync(Guard)).Should().BeTrue();

        ticks.Should().Be(1);
        await sut.StopAsync();
    }

    [Fact]
    public async Task EnsureRunningAsync_ReportsRunningUntilStopped()
    {
        var clock = CreateClock();
        var sut = new ReminderTickLoop();

        sut.IsRunning.Should().BeFalse();

        await sut.EnsureRunningAsync(clock, Period, () => Task.CompletedTask, NullLogger.Instance);
        sut.IsRunning.Should().BeTrue();

        await sut.StopAsync();
        sut.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task EnsureRunningAsync_CalledAgainWhileRunning_TheEarlierLoopNeverTicksAgain()
    {
        // The exact orphan this type exists to prevent: a second start overwriting the first
        // loop's handles without cancelling it first would leave that first loop ticking forever,
        // unreachable by any later StopAsync call. Proven behaviourally — by continuing to advance
        // the SAME clock both loops would otherwise share — rather than by inspecting private state.
        var clock = CreateClock();
        var sut = new ReminderTickLoop();
        var firstTicks = 0;
        var secondTicks = 0;
        using var secondTicked = new SemaphoreSlim(0);

        await sut.EnsureRunningAsync(clock, Period, () =>
        {
            Interlocked.Increment(ref firstTicks);
            return Task.CompletedTask;
        }, NullLogger.Instance);

        // EnsureRunningAsync is itself cancel-first: by the time this second call returns, the first
        // loop has already been cancelled and awaited to completion, not merely overwritten.
        await sut.EnsureRunningAsync(clock, Period, () =>
        {
            Interlocked.Increment(ref secondTicks);
            secondTicked.Release();
            return Task.CompletedTask;
        }, NullLogger.Instance);

        clock.Advance(Period);
        (await secondTicked.WaitAsync(Guard)).Should().BeTrue();
        clock.Advance(Period);
        (await secondTicked.WaitAsync(Guard)).Should().BeTrue();

        secondTicks.Should().Be(2);
        firstTicks.Should().Be(0, "the first loop must be fully cancelled before the second starts, never merely overwritten");

        await sut.StopAsync();
    }

    [Fact]
    public async Task EnsureRunningAsync_RacingAgainstAStopStillAwaitingAnInFlightRefresh_NeverOrphansTheLateringCall()
    {
        // One layer deeper than the test above. There, the second EnsureRunningAsync call only ran
        // AFTER the first had fully returned. Here the first call's own stop step is itself still
        // suspended — awaiting a loop whose refreshAsync (standing in for a real month-change
        // refetch) has not returned yet — when the second call arrives. Without serialising
        // EnsureRunningAsync/StopAsync against each other, the second call's stop step would find the
        // fields already cleared by the first (so it no-ops), record its own loop, and then the
        // first call would resume and overwrite THOSE handles with a third loop of its own —
        // orphaning the second. The gate's job is to make that impossible: whichever call gets in
        // first must finish its entire stop-then-start before the other is let past its own
        // `_gate.WaitAsync()`.
        var clock = CreateClock();
        var sut = new ReminderTickLoop();

        // The already-running loop, standing in for the real tick mid an in-flight refetch: its
        // first tick blocks until the test releases it.
        var initialEntered = new SemaphoreSlim(0);
        var releaseInitial = new TaskCompletionSource();
        await sut.EnsureRunningAsync(clock, Period, async () =>
        {
            initialEntered.Release();
            await releaseInitial.Task;
        }, NullLogger.Instance);

        clock.Advance(Period);
        (await initialEntered.WaitAsync(Guard)).Should().BeTrue();

        // "Call 1": its gate acquisition is uncontended (nothing else is waiting on it yet) and
        // everything before `await loop` inside StopLockedAsync is synchronous, so by the time this
        // line returns control, call1 has already reached — and is genuinely suspended on — the
        // blocked initial loop. No arbitrary delay is needed for that ordering; it falls out of how
        // async/await resumes only at a real suspension point.
        var call1Ticks = 0;
        var call1 = sut.EnsureRunningAsync(clock, Period, () =>
        {
            Interlocked.Increment(ref call1Ticks);
            return Task.CompletedTask;
        }, NullLogger.Instance);

        // "Call 2": its own `_gate.WaitAsync()` cannot complete synchronously — call1 is still
        // holding the gate — so this is guaranteed to be queued behind call1, not racing it.
        var call2Ticks = 0;
        var call2 = sut.EnsureRunningAsync(clock, Period, () =>
        {
            Interlocked.Increment(ref call2Ticks);
            return Task.CompletedTask;
        }, NullLogger.Instance);

        // Unblock the initial loop. This lets its task complete, which lets call1's StopLockedAsync
        // return, which lets call1 finish starting its own loop and release the gate for call2.
        releaseInitial.SetResult();
        await Task.WhenAll(call1, call2).WaitAsync(Guard);

        // Whichever of call1/call2 ends up as the surviving loop, the OTHER must have been fully
        // stopped by the one that ran after it through the gate — not left running unseen. Advance
        // once: exactly one of the two callbacks may fire.
        clock.Advance(Period);
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        (call1Ticks + call2Ticks).Should().Be(
            1, "exactly one of the two later loops must survive the race — never both (an orphan), never neither");

        await sut.StopAsync();
    }

    [Fact]
    public async Task StopAsync_WhenNothingIsRunning_CompletesWithoutThrowing()
    {
        var sut = new ReminderTickLoop();

        var act = () => sut.StopAsync();

        await act.Should().NotThrowAsync();
        sut.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StopAsync_CalledTwiceInARow_CompletesBothTimesWithoutThrowing()
    {
        // The other half of the race this type closes: a stop that lands after a start has already
        // been stopped by something else (or never started at all) must still be a safe no-op.
        var clock = CreateClock();
        var sut = new ReminderTickLoop();
        await sut.EnsureRunningAsync(clock, Period, () => Task.CompletedTask, NullLogger.Instance);

        await sut.StopAsync();
        var act = () => sut.StopAsync();

        await act.Should().NotThrowAsync();
        sut.IsRunning.Should().BeFalse();
    }
}
