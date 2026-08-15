using System.Net;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Options;
using FamilyHQ.Services.Tests.Helpers;
using FamilyHQ.Services.Weather;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Weather;

/// <summary>
/// FHQ-109: the poller must escalate a failing user's poll interval instead of re-hitting a
/// rate-limited Open-Meteo every MinPollIntervalMinutes. Every test runs on a FakeTimeProvider and
/// drives whole cycles directly, so nothing waits in real time.
/// </summary>
public class WeatherPollerServiceTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    private static readonly DateTimeOffset Start = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    private static WeatherSetting Setting(string userId, int pollIntervalMinutes = 1, bool enabled = true)
        => new() { UserId = userId, Enabled = enabled, PollIntervalMinutes = pollIntervalMinutes };

    private static HttpRequestException RateLimited()
        => new("Response status code does not indicate success: 429 (Too Many Requests).",
            inner: null, HttpStatusCode.TooManyRequests);

    private static (TestableWeatherPollerService Sut, Mock<IWeatherRefreshService> Refresh,
        Mock<IWeatherSettingRepository> Repo, FakeTimeProvider Time, RecordingLogger<WeatherPollerService> Logger)
        CreateSut(WeatherOptions? options = null)
    {
        var refresh = new Mock<IWeatherRefreshService>();
        refresh.Setup(r => r.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WeatherRefreshResult(WeatherRefreshOutcome.Succeeded, 1, 1));

        var repo = new Mock<IWeatherSettingRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WeatherSetting>());

        var scopeProvider = new Mock<IServiceProvider>();
        scopeProvider.Setup(p => p.GetService(typeof(IWeatherRefreshService))).Returns(refresh.Object);
        scopeProvider.Setup(p => p.GetService(typeof(IWeatherSettingRepository))).Returns(repo.Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(scopeProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var rootProvider = new Mock<IServiceProvider>();
        rootProvider.Setup(p => p.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);

        var time = new FakeTimeProvider(Start);
        var logger = new RecordingLogger<WeatherPollerService>();
        var sut = new TestableWeatherPollerService(
            rootProvider.Object,
            Microsoft.Extensions.Options.Options.Create(options ?? new WeatherOptions()),
            time,
            logger);

        return (sut, refresh, repo, time, logger);
    }

    /// <summary>Runs <paramref name="cycles"/> cycles, advancing the fake clock by each returned delay.</summary>
    private static async Task<List<TimeSpan>> RunCyclesAsync(
        TestableWeatherPollerService sut, FakeTimeProvider time, int cycles)
    {
        var delays = new List<TimeSpan>();
        for (var i = 0; i < cycles; i++)
        {
            var delay = await sut.RunCycleAsync(CancellationToken.None);
            delays.Add(delay);
            time.Advance(delay);
        }
        return delays;
    }

    [Fact]
    public async Task RunPollCycle_RepeatedRateLimitFailures_EscalatesThatUsersInterval()
    {
        // FHQ-109 AC3: repeated 429s must produce increasing intervals, not a fixed 60s spin.
        var (sut, refresh, repo, time, _) = CreateSut();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Setting(UserA)]);
        refresh.Setup(r => r.RefreshAsync(UserA, It.IsAny<CancellationToken>())).ThrowsAsync(RateLimited());

        var delays = await RunCyclesAsync(sut, time, cycles: 4);

        delays.Should().Equal(
            TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(16));
    }

    [Fact]
    public async Task RunPollCycle_SustainedFailures_StopEscalatingAtTheConfiguredCap()
    {
        var (sut, refresh, repo, time, _) = CreateSut(new WeatherOptions { MaxFailureBackoffMinutes = 10 });
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Setting(UserA)]);
        refresh.Setup(r => r.RefreshAsync(UserA, It.IsAny<CancellationToken>())).ThrowsAsync(RateLimited());

        var delays = await RunCyclesAsync(sut, time, cycles: 5);

        delays.Should().Equal(
            TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(8),
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task RunPollCycle_SuccessAfterFailures_ResetsToTheConfiguredInterval()
    {
        var (sut, refresh, repo, time, logger) = CreateSut();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Setting(UserA, pollIntervalMinutes: 5)]);
        refresh.SetupSequence(r => r.RefreshAsync(UserA, It.IsAny<CancellationToken>()))
            .ThrowsAsync(RateLimited())
            .ThrowsAsync(RateLimited())
            .ReturnsAsync(new WeatherRefreshResult(WeatherRefreshOutcome.Succeeded, 1, 1));

        var delays = await RunCyclesAsync(sut, time, cycles: 3);

        delays.Should().Equal(
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(5));
        logger.Records.Should().Contain(
            r => r.Level == LogLevel.Information && r.Message.Contains("recovered"),
            "recovery from a backed-off state is a state transition worth seeing in Seq");
    }

    [Fact]
    public async Task RunPollCycle_BackedOffUser_IsNotRefreshedBeforeItIsDue()
    {
        // A healthy user on a 1-minute interval must not drag a backed-off user along with it.
        var (sut, refresh, repo, time, _) = CreateSut();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Setting(UserA), Setting(UserB)]);
        refresh.Setup(r => r.RefreshAsync(UserB, It.IsAny<CancellationToken>())).ThrowsAsync(RateLimited());

        await RunCyclesAsync(sut, time, cycles: 2);

        refresh.Verify(r => r.RefreshAsync(UserA, It.IsAny<CancellationToken>()), Times.Exactly(2));
        refresh.Verify(r => r.RefreshAsync(UserB, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunPollCycle_HealthyUser_KeepsItsConfiguredIntervalWhileAnotherUserBacksOff()
    {
        var (sut, refresh, repo, time, _) = CreateSut();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Setting(UserA), Setting(UserB)]);
        refresh.Setup(r => r.RefreshAsync(UserB, It.IsAny<CancellationToken>())).ThrowsAsync(RateLimited());

        var delays = await RunCyclesAsync(sut, time, cycles: 3);

        delays.Should().AllBeEquivalentTo(TimeSpan.FromMinutes(1),
            "the loop must keep waking for the healthy user on its own interval");
    }

    [Fact]
    public async Task RunPollCycle_UserNoLongerEnabled_HasItsBackoffStateDiscarded()
    {
        // Memory safety: state is keyed only by users present in the current cycle, so a user who
        // disappears leaves nothing behind — and a returning user starts from a clean slate.
        var (sut, refresh, repo, _, _) = CreateSut();
        var enabled = new List<WeatherSetting> { Setting(UserA) };
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => enabled);
        refresh.SetupSequence(r => r.RefreshAsync(UserA, It.IsAny<CancellationToken>()))
            .ThrowsAsync(RateLimited())
            .ReturnsAsync(new WeatherRefreshResult(WeatherRefreshOutcome.Succeeded, 1, 1));

        await sut.RunCycleAsync(CancellationToken.None);   // fails -> backed off for 2 minutes
        enabled = [];
        await sut.RunCycleAsync(CancellationToken.None);   // user gone -> state pruned
        enabled = [Setting(UserA)];
        var delay = await sut.RunCycleAsync(CancellationToken.None);

        // The clock never moved, so a surviving backoff entry would have skipped this refresh.
        refresh.Verify(r => r.RefreshAsync(UserA, It.IsAny<CancellationToken>()), Times.Exactly(2));
        delay.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RunPollCycle_NoEnabledUsers_WaitsTheMinimumInterval()
    {
        var (sut, refresh, _, _, _) = CreateSut();

        var delay = await sut.RunCycleAsync(CancellationToken.None);

        delay.Should().Be(TimeSpan.FromMinutes(1));
        refresh.Verify(r => r.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunPollCycle_CappedFailures_StopLoggingAtErrorOncePlateaued()
    {
        // Logging skill: log the escalation transition, not every cycle — a permanently broken
        // provider must not flood Seq with one Error per poll.
        var (sut, refresh, repo, time, logger) = CreateSut(new WeatherOptions { MaxFailureBackoffMinutes = 4 });
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Setting(UserA)]);
        refresh.Setup(r => r.RefreshAsync(UserA, It.IsAny<CancellationToken>())).ThrowsAsync(RateLimited());

        await RunCyclesAsync(sut, time, cycles: 6);

        logger.Records.Count(r => r.Level == LogLevel.Error).Should().Be(2,
            "only the two escalating intervals (2 min, 4 min) are transitions; the rest sit at the cap");
    }

    [Fact]
    public async Task RunPollCycle_RefreshThrows_DoesNotPropagate()
    {
        var (sut, refresh, repo, _, _) = CreateSut();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Setting(UserA)]);
        refresh.Setup(r => r.RefreshAsync(UserA, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await sut.Invoking(s => s.RunCycleAsync(CancellationToken.None))
            .Should().NotThrowAsync("a single user's failure must never stop the poll loop");
    }
}
