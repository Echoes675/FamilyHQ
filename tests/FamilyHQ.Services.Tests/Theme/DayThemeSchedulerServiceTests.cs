using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Options;
using FamilyHQ.Services.Theme;
using FamilyHQ.Services.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FamilyHQ.Services.Tests.Theme;

public class DayThemeSchedulerServiceTests
{
    [Fact]
    public async Task ExecuteAsync_DoesNotCrashHost_WhenGetTodayReportsMissingRecord()
    {
        // Reproduces the FHQ-55 production crash: at a day boundary GetTodayAsync finds no record
        // and throws InvalidOperationException. The loop must absorb it (log + continue), not let it
        // escape ExecuteAsync — which, under BackgroundServiceExceptionBehavior.StopHost, kills the host.
        using var cts = new CancellationTokenSource();
        var dayThemeServiceMock = new Mock<IDayThemeService>();
        dayThemeServiceMock.Setup(x => x.EnsureTodayAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var getCalls = 0;
        dayThemeServiceMock.Setup(x => x.GetTodayAsync(It.IsAny<CancellationToken>()))
            // Call 1 is the startup read; the first loop read is call 2 — cancel there to bound the run.
            .Callback(() => { if (Interlocked.Increment(ref getCalls) >= 2) cts.Cancel(); })
            .ThrowsAsync(new InvalidOperationException("No DayTheme record found for today."));
        var logger = new RecordingLogger<DayThemeSchedulerService>();

        var sut = CreateSut(dayThemeServiceMock.Object, new Mock<IThemeBroadcaster>().Object, logger);

        await sut.Invoking(s => s.RunExecuteAsync(cts.Token))
            .Should().NotThrowAsync("a missing DayTheme record must not stop the host");

        logger.Records.Should().Contain(
            r => r.Level == LogLevel.Error && r.Message.Contains("loop iteration failed"),
            "the failed iteration must be logged before the loop continues");
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotCrashHost_WhenLoopIterationThrowsTransientError()
    {
        // The loop guard must contain ANY exception (not just the missing-record one) — e.g. a transient
        // database/location failure — so a single bad iteration never takes down the host.
        using var cts = new CancellationTokenSource();
        var dayThemeServiceMock = new Mock<IDayThemeService>();
        dayThemeServiceMock.Setup(x => x.EnsureTodayAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var getCalls = 0;
        dayThemeServiceMock.Setup(x => x.GetTodayAsync(It.IsAny<CancellationToken>()))
            .Callback(() => { if (Interlocked.Increment(ref getCalls) >= 2) cts.Cancel(); })
            .ThrowsAsync(new TimeoutException("transient database timeout"));
        var logger = new RecordingLogger<DayThemeSchedulerService>();

        var sut = CreateSut(dayThemeServiceMock.Object, new Mock<IThemeBroadcaster>().Object, logger);

        await sut.Invoking(s => s.RunExecuteAsync(cts.Token))
            .Should().NotThrowAsync("a transient loop failure must not stop the host");

        logger.Records.Should().Contain(r => r.Level == LogLevel.Error && r.Message.Contains("loop iteration failed"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExecuteAsync_WhenRecalculationTriggeredDuringBoundaryRead_ReReadsBoundariesWithoutError(int triggerCount)
    {
        // FHQ-108: a location change calls TriggerRecalculationAsync while the loop iteration that
        // will consume the signal is already in flight (between reading the boundaries and entering
        // the delay). The iteration must observe that trigger — if it picks up the freshly installed
        // CancellationTokenSource instead, it sleeps on the boundaries it read BEFORE the location
        // changed and the recalculation is silently lost, on exactly the operation the trigger exists
        // to serve. triggerCount 2 covers two location changes racing into the same window.
        //
        // Determinism: the trigger is raised synchronously from inside the GetTodayAsync mock, so the
        // interleaving is fixed, not raced. The next boundary is ~12.5 hours away on the fake clock,
        // so the delay can only end by cancellation — never by the wall clock.
        using var cts = new CancellationTokenSource();
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 21, 6, 30, 0, TimeSpan.Zero));
        var dto = new DayThemeDto(
            new DateOnly(2024, 6, 21),
            new TimeOnly(5, 30), new TimeOnly(6, 0), new TimeOnly(20, 0), new TimeOnly(21, 30),
            "Europe/Dublin",
            "Daytime");

        var getTodayCalls = 0;
        Action triggerRecalculation = () => { };
        var dayThemeServiceMock = new Mock<IDayThemeService>();
        dayThemeServiceMock.Setup(x => x.EnsureTodayAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dayThemeServiceMock.Setup(x => x.GetTodayAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var call = Interlocked.Increment(ref getTodayCalls);
                // Call 1 is the startup read; call 2 is the first loop iteration's boundary read.
                if (call == 2)
                    for (var i = 0; i < triggerCount; i++) triggerRecalculation();
                // Call 3 is the re-read the trigger must produce — stop the loop so the test terminates.
                if (call >= 3) cts.Cancel();
                return Task.FromResult(dto);
            });
        var logger = new RecordingLogger<DayThemeSchedulerService>();

        var sut = CreateSut(dayThemeServiceMock.Object, new Mock<IThemeBroadcaster>().Object, logger, fakeTime);
        // TriggerRecalculationAsync completes synchronously, so there is nothing to await here.
        triggerRecalculation = () => _ = sut.TriggerRecalculationAsync();

        // Tripwire only. On the correct path the loop runs to completion synchronously — every mock
        // returns a completed task and the delay is pre-cancelled, so no timer is ever armed and this
        // deadline is never approached. A regression that swallows the trigger instead waits 12.5 hours
        // on a fake clock the test never advances, which this turns into a prompt failure.
        await sut.RunExecuteAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(15));

        getTodayCalls.Should().Be(3, "the trigger must cause exactly one fresh boundary re-read");
        logger.Records.Should().NotContain(r => r.Level == LogLevel.Error,
            "the trigger must be absorbed as a clean cancellation, not surface as a failed iteration");
    }

    [Fact]
    public async Task ExecuteAsync_WhenBoundaryDelayElapses_BroadcastsThePeriodThatBecameActive()
    {
        // The scheduler's core contract: sleep to the next boundary, then re-read and broadcast the
        // period that has JUST become active — not the one that was active when the delay started.
        // Now unit-testable because the delay runs on the injected TimeProvider: TimerArmedTimeProvider
        // signals the instant the loop enters the delay and advancing the fake clock is what ends it,
        // so the whole test is driven by explicit steps with no polling and no wall-clock waiting.
        using var cts = new CancellationTokenSource();
        // 06:30 UTC = 07:30 Europe/Dublin (BST); the next boundary is EveningStart 20:00 local = 12.5h away.
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 21, 6, 30, 0, TimeSpan.Zero));
        var clock = new TimerArmedTimeProvider(fakeTime);
        DayThemeDto DtoFor(string period) => new(
            new DateOnly(2024, 6, 21),
            new TimeOnly(5, 30), new TimeOnly(6, 0), new TimeOnly(20, 0), new TimeOnly(21, 30),
            "Europe/Dublin",
            period);

        var getTodayCalls = 0;
        var dayThemeServiceMock = new Mock<IDayThemeService>();
        dayThemeServiceMock.Setup(x => x.EnsureTodayAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dayThemeServiceMock.Setup(x => x.GetTodayAsync(It.IsAny<CancellationToken>()))
            // Calls 1 (startup) and 2 (the pre-delay read) still see Daytime; the boundary is crossed
            // during the delay, so the post-delay read must observe Evening.
            .Returns(() => Task.FromResult(DtoFor(Interlocked.Increment(ref getTodayCalls) <= 2 ? "Daytime" : "Evening")));

        var broadcastPeriods = new List<string>();
        var broadcasterMock = new Mock<IThemeBroadcaster>();
        broadcasterMock.Setup(x => x.BroadcastThemeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string period, CancellationToken _) =>
            {
                broadcastPeriods.Add(period);
                // Startup broadcast, then the post-delay one — stop the loop so the test terminates.
                if (broadcastPeriods.Count >= 2) cts.Cancel();
                return Task.CompletedTask;
            });
        var logger = new RecordingLogger<DayThemeSchedulerService>();

        var sut = CreateSut(dayThemeServiceMock.Object, broadcasterMock.Object, logger, clock);

        var run = sut.RunExecuteAsync(cts.Token);
        await clock.WaitForNextTimerAsync();             // the loop is now waiting on the boundary
        fakeTime.Advance(TimeSpan.FromHours(13));        // cross the boundary — this is what ends the wait
        await run.WaitAsync(TimeSpan.FromSeconds(15));   // tripwire only; the loop completes inline

        broadcastPeriods.Should().Equal(["Daytime", "Evening"],
            "the boundary broadcast must carry the period read after the delay, not the one read before it");
        logger.Records.Should().NotContain(r => r.Level == LogLevel.Error);
    }

    [Fact]
    public void GetNextBoundaryDelay_WithNonUtcZone_UsesLocalTimeNotUtc()
    {
        // Clock fixed at 04:50 UTC = 05:50 Europe/Dublin (BST, UTC+1).
        // MorningStart = 05:30 local, DaytimeStart = 06:00 local.
        // Next boundary after 05:50 local is DaytimeStart at 06:00 local = 05:00 UTC.
        // Delay should be ~10 minutes (to local 06:00), not ~61 minutes (to UTC 06:00).
        var fixedUtc = new DateTimeOffset(2024, 6, 21, 4, 50, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(fixedUtc);

        var dto = new DayThemeDto(
            new DateOnly(2024, 6, 21),
            new TimeOnly(5, 30),   // MorningStart (local)
            new TimeOnly(6, 0),    // DaytimeStart (local)
            new TimeOnly(20, 0),   // EveningStart (local)
            new TimeOnly(21, 30),  // NightStart (local)
            "Europe/Dublin",
            "Morning");

        var sut = CreateSut(new Mock<IDayThemeService>().Object,
                            new Mock<IThemeBroadcaster>().Object,
                            new RecordingLogger<DayThemeSchedulerService>(),
                            fakeTime);

        var delay = sut.TestGetNextBoundaryDelay(dto);

        // 06:00 local BST = 05:00 UTC. Clock is at 04:50 UTC. Delay ≈ 10 minutes.
        delay.Should().BeCloseTo(TimeSpan.FromMinutes(10), precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetNextBoundaryDelay_AllBoundariesPassed_WaitsToLocalMidnight()
    {
        // Clock fixed at 22:30 UTC = 23:30 Europe/Dublin (BST, UTC+1).
        // All boundaries have passed in local time (NightStart was 21:30 local = 20:30 UTC).
        // Should wait until local midnight (2024-06-22 00:00 BST = 2024-06-21 23:00 UTC).
        var fixedUtc = new DateTimeOffset(2024, 6, 21, 22, 30, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(fixedUtc);

        var dto = new DayThemeDto(
            new DateOnly(2024, 6, 21),
            new TimeOnly(5, 30),
            new TimeOnly(6, 0),
            new TimeOnly(20, 0),
            new TimeOnly(21, 30),  // NightStart passed at 21:30 local = 20:30 UTC; now 23:30 local
            "Europe/Dublin",
            "Night");

        var sut = CreateSut(new Mock<IDayThemeService>().Object,
                            new Mock<IThemeBroadcaster>().Object,
                            new RecordingLogger<DayThemeSchedulerService>(),
                            fakeTime);

        var delay = sut.TestGetNextBoundaryDelay(dto);

        // Midnight Europe/Dublin on 2024-06-22 = 23:00 UTC on 2024-06-21.
        // Clock is at 22:30 UTC → delay ≈ 30 minutes.
        delay.Should().BeCloseTo(TimeSpan.FromMinutes(30), precision: TimeSpan.FromSeconds(5));
    }

    private static TestableDayThemeSchedulerService CreateSut(
        IDayThemeService dayThemeService,
        IThemeBroadcaster themeBroadcaster,
        ILogger<DayThemeSchedulerService> logger,
        TimeProvider? timeProvider = null)
    {
        // The scheduler resolves IDayThemeService from a per-iteration DI scope; mock the scope chain.
        var scopeProviderMock = new Mock<IServiceProvider>();
        scopeProviderMock.Setup(x => x.GetService(typeof(IDayThemeService))).Returns(dayThemeService);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(x => x.ServiceProvider).Returns(scopeProviderMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(x => x.CreateScope()).Returns(scopeMock.Object);

        var rootProviderMock = new Mock<IServiceProvider>();
        rootProviderMock.Setup(x => x.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactoryMock.Object);

        var options = Microsoft.Extensions.Options.Options.Create(
            new DayThemeOptions { LoopErrorBackoff = TimeSpan.FromMilliseconds(1) });

        return new TestableDayThemeSchedulerService(
            rootProviderMock.Object, themeBroadcaster, logger, options,
            timeProvider ?? TimeProvider.System);
    }
}
