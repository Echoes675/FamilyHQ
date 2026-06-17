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
