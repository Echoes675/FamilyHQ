using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// FHQ-127 review follow-up: the per-minute now-line refresh loop must survive a transient
// refresh failure (a dead loop silently reinstates the frozen-line bug) and must not swallow
// cancellation or faults silently (logging skill: no silent catch). Ticks are driven by
// FakeTimeProvider through PeriodicTimer's TimeProvider overload — no real timers; the
// WaitAsync ceilings never elapse on the pass path and only turn a would-be hang into a
// clean failure.
public class NowLineRefreshLoopTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    private static FakeTimeProvider CreateClock() =>
        new(new DateTimeOffset(2026, 8, 13, 6, 0, 0, TimeSpan.Zero));

    private static Mock<ILogger> CreateLogger() => new();

    [Fact]
    public async Task RunAsync_OnEachTick_InvokesRefresh()
    {
        var clock = CreateClock();
        using var cts = new CancellationTokenSource();
        using var ticked = new SemaphoreSlim(0);
        var refreshes = 0;
        var loop = NowLineRefreshLoop.RunAsync(clock, Period, () =>
        {
            Interlocked.Increment(ref refreshes);
            ticked.Release();
            return Task.CompletedTask;
        }, NullLogger.Instance, cts.Token);

        clock.Advance(Period);
        (await ticked.WaitAsync(Guard)).Should().BeTrue();
        clock.Advance(Period);
        (await ticked.WaitAsync(Guard)).Should().BeTrue();

        refreshes.Should().Be(2);
        cts.Cancel();
        await loop.WaitAsync(Guard);
    }

    [Fact]
    public async Task RunAsync_WhenRefreshThrows_ContinuesTickingOnTheNextTick()
    {
        var clock = CreateClock();
        using var cts = new CancellationTokenSource();
        using var ticked = new SemaphoreSlim(0);
        var refreshes = 0;
        var loop = NowLineRefreshLoop.RunAsync(clock, Period, () =>
        {
            var call = Interlocked.Increment(ref refreshes);
            ticked.Release();
            return call == 1
                ? Task.FromException(new InvalidOperationException("transient render fault"))
                : Task.CompletedTask;
        }, NullLogger.Instance, cts.Token);

        clock.Advance(Period);
        (await ticked.WaitAsync(Guard)).Should().BeTrue();
        clock.Advance(Period);
        (await ticked.WaitAsync(Guard)).Should().BeTrue();

        refreshes.Should().Be(2);
        cts.Cancel();
        await loop.WaitAsync(Guard);
        loop.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WhenRefreshThrows_LogsWarningWithTheException()
    {
        var clock = CreateClock();
        using var cts = new CancellationTokenSource();
        var logger = CreateLogger();
        var warningLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        logger.Setup(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(() => warningLogged.TrySetResult());
        var fault = new InvalidOperationException("transient render fault");
        var loop = NowLineRefreshLoop.RunAsync(clock, Period,
            () => Task.FromException(fault), logger.Object, cts.Token);

        clock.Advance(Period);
        await warningLogged.Task.WaitAsync(Guard);

        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            fault,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        cts.Cancel();
        await loop.WaitAsync(Guard);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_CompletesWithoutThrowing()
    {
        var clock = CreateClock();
        using var cts = new CancellationTokenSource();
        var loop = NowLineRefreshLoop.RunAsync(clock, Period,
            () => Task.CompletedTask, NullLogger.Instance, cts.Token);

        cts.Cancel();

        await loop.WaitAsync(Guard);
        loop.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_LogsDebugForTheShutdown()
    {
        var clock = CreateClock();
        using var cts = new CancellationTokenSource();
        var logger = CreateLogger();
        var loop = NowLineRefreshLoop.RunAsync(clock, Period,
            () => Task.CompletedTask, logger.Object, cts.Token);

        cts.Cancel();
        await loop.WaitAsync(Guard);

        logger.Verify(l => l.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
