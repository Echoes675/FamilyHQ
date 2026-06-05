using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Options;
using FamilyHQ.Services.Theme;
using FamilyHQ.Services.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    private static TestableDayThemeSchedulerService CreateSut(
        IDayThemeService dayThemeService,
        IThemeBroadcaster themeBroadcaster,
        ILogger<DayThemeSchedulerService> logger)
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
        return new TestableDayThemeSchedulerService(rootProviderMock.Object, themeBroadcaster, logger, options);
    }
}
