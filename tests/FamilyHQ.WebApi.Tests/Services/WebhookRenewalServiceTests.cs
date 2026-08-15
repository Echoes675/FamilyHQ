using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using FamilyHQ.WebApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Services;

/// <summary>
/// Drives the internal per-user renewal loop directly (same pattern as CalendarSyncWorkerTests
/// driving DrainAsync) — the ExecuteAsync wrapper only adds scheduling around it.
/// </summary>
public class WebhookRenewalServiceTests
{
    private static (WebhookRenewalService Service, Mock<IWebhookRegistrationService> Registration, Mock<ILogger<WebhookRenewalService>> Logger)
        CreateSut(IEnumerable<string> userIds)
    {
        var tokenStore = new Mock<ITokenStore>();
        tokenStore.Setup(t => t.GetAllUserIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(userIds);

        var registration = new Mock<IWebhookRegistrationService>();

        var services = new ServiceCollection();
        services.AddScoped(_ => tokenStore.Object);
        services.AddScoped(_ => registration.Object);
        var provider = services.BuildServiceProvider();

        var logger = new Mock<ILogger<WebhookRenewalService>>();
        var options = Options.Create(new SyncOptions { WebhookRegistrationEnabled = true });

        var service = new WebhookRenewalService(provider, options, logger.Object);
        return (service, registration, logger);
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
}
