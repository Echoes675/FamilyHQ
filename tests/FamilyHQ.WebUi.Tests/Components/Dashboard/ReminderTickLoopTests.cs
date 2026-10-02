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
