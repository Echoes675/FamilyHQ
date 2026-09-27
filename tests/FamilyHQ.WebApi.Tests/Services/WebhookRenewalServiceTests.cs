using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Options;
using FamilyHQ.WebApi.Services;
using FamilyHQ.WebApi.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Services;

/// <summary>
/// Drives the internal per-user renewal loop directly (same pattern as CalendarSyncWorkerTests
/// driving DrainAsync), and drives <c>ExecuteAsync</c> itself through a fake clock for the FHQ-213
/// scheduling behaviour — which is the whole defect, so it cannot be left to the wrapper.
/// </summary>
public class WebhookRenewalServiceTests
{
    /// <summary>FHQ-213: the restart in production that exposed the defect, from the ticket's evidence.</summary>
    private static readonly DateTimeOffset ProcessStart = new(2026, 9, 25, 19, 27, 0, TimeSpan.Zero);

    /// <summary>FHQ-213: what an operator's stale <c>Sync__WebhookRenewalInterval</c> still says.</summary>
    private static readonly TimeSpan LegacySixDayInterval = TimeSpan.FromDays(6);

    private static (WebhookRenewalService Service, Mock<IWebhookRegistrationService> Registration, Mock<ILogger<WebhookRenewalService>> Logger)
        CreateSut(IEnumerable<string> userIds, TimeProvider? clock = null, TimeSpan? legacyRenewalInterval = null)
    {
        var tokenStore = new Mock<ITokenStore>();
        tokenStore.Setup(t => t.GetAllUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(userIds);

        var registration = new Mock<IWebhookRegistrationService>();

        var services = new ServiceCollection();
        services.AddScoped(_ => tokenStore.Object);
        services.AddScoped(_ => registration.Object);
        var provider = services.BuildServiceProvider();

        var logger = new Mock<ILogger<WebhookRenewalService>>();
        var options = Options.Create(new SyncOptions
        {
            WebhookRegistrationEnabled = true,
            WebhookRenewalInterval = legacyRenewalInterval
        });

        var service = new WebhookRenewalService(provider, options, clock ?? TimeProvider.System, logger.Object);
        return (service, registration, logger);
    }

    [Fact]
    public async Task ExecuteAsync_SchedulesEachPassOnePollIntervalApart_NotOncePerChannelLifetime()
    {
        // Arrange — FHQ-213: the loop used to sleep WebhookRenewalInterval (6 days) between passes,
        // anchored to process start, while Google's ~7-day channel clock runs from registration.
        // Production restarted 2026-09-25 19:27, so the next pass was due 10-01 — three days after the
        // channels expired on 09-28. The cadence has to be a poll, short enough that some pass always
        // lands inside the 24-hour re-registration window.
        var clock = new TimerArmedTimeProvider(new FakeTimeProvider(ProcessStart));
        var (service, registration, _) = CreateSut(["user-1"], clock);

        // Act / Assert
        await service.StartAsync(CancellationToken.None);

        // The startup delay, advanced by exactly what the service asked for rather than a duplicated constant.
        await clock.WaitForNextTimerAsync();
        clock.Advance(clock.LastTimerDueTime);

        // Returning here proves the first pass completed and the loop has armed its next delay.
        await clock.WaitForNextTimerAsync();
        registration.Verify(r => r.RegisterAllAsync("user-1", false, It.IsAny<CancellationToken>()), Times.Once);
        clock.LastTimerDueTime.Should().Be(TimeSpan.FromHours(1),
            "SyncOptions.WebhookRenewalPollInterval is the gap between passes now");
        clock.LastTimerDueTime.Should().BeLessThan(WebhookRegistrationService.RenewalWindow,
            "a pass has to land inside the window RegisterForCalendarAsync re-registers in, whenever the process started");

        clock.Advance(clock.LastTimerDueTime);
        await clock.WaitForNextTimerAsync();

        registration.Verify(r => r.RegisterAllAsync("user-1", false, It.IsAny<CancellationToken>()), Times.Exactly(2));

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOnlyTheLegacyRenewalIntervalIsConfigured_StillPollsOnTheNewCadence()
    {
        // Arrange — the deployed env credentials set Sync__WebhookRenewalInterval=6.00:00:00. Honouring
        // it would reinstate the defect, so the renamed key's default must win outright.
        var clock = new TimerArmedTimeProvider(new FakeTimeProvider(ProcessStart));
        var (service, _, _) = CreateSut(["user-1"], clock, legacyRenewalInterval: LegacySixDayInterval);

        // Act
        await service.StartAsync(CancellationToken.None);
        await clock.WaitForNextTimerAsync();
        clock.Advance(clock.LastTimerDueTime);
        await clock.WaitForNextTimerAsync();

        // Assert
        clock.LastTimerDueTime.Should().Be(TimeSpan.FromHours(1),
            "a stale 6-day legacy value must not be able to slow the poll back down");

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheLegacyRenewalIntervalIsConfigured_WarnsThatItIsIgnored()
    {
        // Arrange — silently ignoring a key an operator believes is in effect is how the next one of
        // these hides. The environment still boots; it just says so.
        var clock = new TimerArmedTimeProvider(new FakeTimeProvider(ProcessStart));
        var (service, _, logger) = CreateSut(["user-1"], clock, legacyRenewalInterval: LegacySixDayInterval);

        // Act
        await service.StartAsync(CancellationToken.None);
        await clock.WaitForNextTimerAsync();

        // Assert
        VerifyWarningLoggedContaining(logger, "Sync:WebhookRenewalInterval");
        VerifyWarningLoggedContaining(logger, "no longer used");

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheLegacyRenewalIntervalIsNotConfigured_WarnsAboutNothing()
    {
        // Arrange
        var clock = new TimerArmedTimeProvider(new FakeTimeProvider(ProcessStart));
        var (service, _, logger) = CreateSut(["user-1"], clock);

        // Act
        await service.StartAsync(CancellationToken.None);
        await clock.WaitForNextTimerAsync();

        // Assert
        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((_, _) => true),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RegisterAllWebhooksAsync_WhenNothingIsDue_StillLogsOneInformationSummary()
    {
        // Arrange — FHQ-213: the case that used to produce silence. The per-calendar "still valid,
        // skipping" lines are Debug now, and no FamilyHQ environment emits Debug (issue 14 in
        // .agent/docs/intermittent-issues.md), so without this line a renewal loop that had silently
        // stopped would be indistinguishable from one working perfectly.
        var (service, registration, logger) = CreateSut(["user-1"]);
        registration.Setup(r => r.RegisterAllAsync("user-1", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookRegistrationTally(CalendarsChecked: 7, ChannelsRegistered: 0, ChannelsFoundExpired: 0));

        // Act
        await service.RegisterAllWebhooksAsync(CancellationToken.None);

        // Assert
        VerifyInformationLoggedContaining(logger, "7 calendar(s) checked");
        VerifyInformationLoggedContaining(logger, "0 channel(s) re-registered");
        VerifyInformationLoggedContaining(logger, "Next pass in 01:00:00");
        VerifyInformationLogCount(logger, Times.Once(),
            "one summary line per pass is the whole point — a second routine line reinstates the flood");
    }

    [Fact]
    public async Task RegisterAllWebhooksAsync_WhenChannelsWereRenewed_SaysHowManyInTheSummary()
    {
        // Arrange — "alive, renewed 7" has to be distinguishable from "alive, nothing due" at a glance.
        var (service, registration, logger) = CreateSut(["user-1"]);
        registration.Setup(r => r.RegisterAllAsync("user-1", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookRegistrationTally(CalendarsChecked: 7, ChannelsRegistered: 7, ChannelsFoundExpired: 2));

        // Act
        await service.RegisterAllWebhooksAsync(CancellationToken.None);

        // Assert
        VerifyInformationLoggedContaining(logger, "7 channel(s) re-registered");
        VerifyInformationLoggedContaining(logger, "2 found already expired");
    }

    [Fact]
    public async Task RegisterAllWebhooksAsync_SumsTheTalliesOfEveryUserIntoOneSummary()
    {
        // Arrange — the counts have to be the pass's, not the last user's.
        var (service, registration, logger) = CreateSut(["user-1", "user-2"]);
        registration.Setup(r => r.RegisterAllAsync("user-1", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookRegistrationTally(CalendarsChecked: 3, ChannelsRegistered: 1, ChannelsFoundExpired: 0));
        registration.Setup(r => r.RegisterAllAsync("user-2", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookRegistrationTally(CalendarsChecked: 4, ChannelsRegistered: 2, ChannelsFoundExpired: 1));

        // Act
        await service.RegisterAllWebhooksAsync(CancellationToken.None);

        // Assert
        VerifyInformationLoggedContaining(logger, "2 user(s)");
        VerifyInformationLoggedContaining(logger, "7 calendar(s) checked");
        VerifyInformationLoggedContaining(logger, "3 channel(s) re-registered");
        VerifyInformationLoggedContaining(logger, "1 found already expired");
    }

    [Fact]
    public async Task RegisterAllWebhooksAsync_WhenOneUserThrowsReauth_ContinuesWithRemainingUsers()
    {
        // FHQ-85: one user's dead grant must not abort webhook renewal for everyone else.
        var (service, registration, _) = CreateSut(["user-1", "user-2"]);
        registration.Setup(r => r.RegisterAllAsync("user-1", false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleReauthRequiredException(
                GoogleAuthFailureSource.TokenRefresh, "Token has been expired or revoked.", userId: "user-1"));

        await service.RegisterAllWebhooksAsync(CancellationToken.None);

        registration.Verify(r => r.RegisterAllAsync("user-2", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAllWebhooksAsync_WhenUserThrowsReauth_DoesNotLogError()
    {
        // FHQ-85 review: by the time the reauth reaches this loop it is already persisted and
        // Warning-logged by WebhookRegistrationService — a handled account-state condition must
        // not produce an Error-level entry with a stack trace every renewal cycle.
        var (service, registration, logger) = CreateSut(["user-1"]);
        registration.Setup(r => r.RegisterAllAsync("user-1", false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleReauthRequiredException(
                GoogleAuthFailureSource.TokenRefresh, "Token has been expired or revoked.", userId: "user-1"));

        await service.RegisterAllWebhooksAsync(CancellationToken.None);

        logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((_, _) => true),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAllWebhooksAsync_WhenUserThrowsNonReauthException_StillLogsErrorAndContinues()
    {
        // A genuinely unexpected per-user failure keeps its Error log and the loop still continues.
        var (service, registration, logger) = CreateSut(["user-1", "user-2"]);
        registration.Setup(r => r.RegisterAllAsync("user-1", false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await service.RegisterAllWebhooksAsync(CancellationToken.None);

        registration.Verify(r => r.RegisterAllAsync("user-2", false, It.IsAny<CancellationToken>()), Times.Once);
        logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((_, _) => true),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private static void VerifyInformationLoggedContaining(Mock<ILogger<WebhookRenewalService>> logger, string fragment) =>
        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v != null && v.ToString()!.Contains(fragment, StringComparison.Ordinal)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            $"the renewal pass summary must carry '{fragment}'");

    private static void VerifyInformationLogCount(Mock<ILogger<WebhookRenewalService>> logger, Times times, string because) =>
        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((_, _) => true),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times,
            because);

    private static void VerifyWarningLoggedContaining(Mock<ILogger<WebhookRenewalService>> logger, string fragment) =>
        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v != null && v.ToString()!.Contains(fragment, StringComparison.Ordinal)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
}
