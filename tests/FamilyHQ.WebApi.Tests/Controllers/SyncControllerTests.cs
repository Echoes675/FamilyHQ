using System.Net;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.WebApi.Controllers;
using FamilyHQ.WebApi.Hubs;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class SyncControllerTests
{
    [Fact]
    public async Task TriggerSync_ShouldCallSyncAndNotifyClients()
    {
        // Arrange
        var (constructorSync, _, proxy, systemUnderTest) = CreateSut();

        // Act
        var result = await systemUnderTest.TriggerSync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();

        constructorSync.Verify(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        proxy.Verify(c => c.SendCoreAsync("EventsUpdated", Array.Empty<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GooglePushWebhook_ShouldSyncEachRegisteredUserAndNotifyClients()
    {
        // Arrange
        var (_, scopedSync, proxy, systemUnderTest) = CreateSut(userIds: ["user1"]);
        systemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-resource-state", "sync");

        // Act
        var result = await systemUnderTest.GooglePushWebhook(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();

        scopedSync.Verify(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        proxy.Verify(c => c.SendCoreAsync("EventsUpdated", Array.Empty<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GooglePushWebhook_WhenNoRegisteredUsers_ShouldStillNotifyClients()
    {
        // Arrange
        var (_, _, proxy, systemUnderTest) = CreateSut(userIds: []);
        systemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-resource-state", "sync");

        // Act
        var result = await systemUnderTest.GooglePushWebhook(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();
        proxy.Verify(c => c.SendCoreAsync("EventsUpdated", Array.Empty<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GooglePushWebhook_WithMatchingChannelId_SyncsOnlyThatCalendar()
    {
        // Arrange
        var calendarInfoId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var userId = "user1";
        var channelId = "test-channel-123";

        var calendarInfo = new CalendarInfo
        {
            Id = calendarInfoId,
            UserId = userId,
            GoogleCalendarId = "google-cal-1",
            DisplayName = "Test Calendar"
        };

        var registration = new WebhookRegistration
        {
            ChannelId = channelId,
            CalendarInfoId = calendarInfoId,
            CalendarInfo = calendarInfo
        };

        var (_, scopedSync, proxy, systemUnderTest) = CreateSut(
            webhookRegistration: registration,
            calendarInfo: calendarInfo);

        systemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-resource-state", "exists");
        systemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-channel-id", channelId);

        // Act
        var result = await systemUnderTest.GooglePushWebhook(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();

        scopedSync.Verify(
            s => s.SyncAsync(calendarInfoId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);

        scopedSync.Verify(
            s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);

        proxy.Verify(c => c.SendCoreAsync("EventsUpdated", Array.Empty<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GooglePushWebhook_WithUnknownChannelId_FallsBackToSyncAll()
    {
        // Arrange
        var (_, scopedSync, proxy, systemUnderTest) = CreateSut(userIds: ["user1"]);

        systemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-resource-state", "exists");
        systemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-channel-id", "unknown-channel");

        // Act
        var result = await systemUnderTest.GooglePushWebhook(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();

        scopedSync.Verify(
            s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);

        scopedSync.Verify(
            s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);

        proxy.Verify(c => c.SendCoreAsync("EventsUpdated", Array.Empty<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TriggerSync_WhenSyncThrowsGoogleReauthRequired_Returns409Conflict()
    {
        // Arrange
        var (constructorSync, _, _, systemUnderTest) = CreateSut();
        constructorSync
            .Setup(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleReauthRequiredException(
                GoogleAuthFailureSource.TokenRefresh, "Token has been expired or revoked."));

        // Act
        var result = await systemUnderTest.TriggerSync(CancellationToken.None);

        // Assert
        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value.Should().NotBeNull();
        var payload = conflict.Value!.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(conflict.Value));
        payload["status"].Should().Be("needs_reauth");
        payload["source"].Should().Be("token_refresh");
        payload["reconnectUrl"].Should().Be("/api/auth/login");
    }

    [Fact]
    public async Task TriggerSync_WhenSyncThrowsGoogleApiException_Returns502BadGateway()
    {
        // Arrange
        var (constructorSync, _, _, systemUnderTest) = CreateSut();
        constructorSync
            .Setup(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleApiException(HttpStatusCode.InternalServerError, "GetCalendars", "server boom"));

        // Act
        var result = await systemUnderTest.TriggerSync(CancellationToken.None);

        // Assert
        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
        var payload = status.Value!.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(status.Value));
        payload["status"].Should().Be("upstream_error");
    }

    [Fact]
    public async Task GooglePushWebhook_WhenUserSyncThrowsReauthRequired_MarksUserAndReturnsOk()
    {
        // Arrange
        var (_, scopedSync, _, systemUnderTest, tokenStore) = CreateSutExposingTokenStore(userIds: ["user-fail"]);
        scopedSync
            .Setup(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleReauthRequiredException(
                GoogleAuthFailureSource.CalendarApi, "Insufficient Permission"));

        systemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-resource-state", "sync");

        // Act
        var result = await systemUnderTest.GooglePushWebhook(CancellationToken.None);

        // Assert — endpoint still returns 200 to Google, but the user is flagged.
        result.Should().BeOfType<OkResult>();
        tokenStore.Verify(
            t => t.MarkNeedsReauthAsync("user-fail", "Insufficient Permission", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GooglePushWebhook_WithoutChannelId_FallsBackToSyncAll()
    {
        // Arrange
        var (_, scopedSync, proxy, systemUnderTest) = CreateSut(userIds: ["user1"]);

        systemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-resource-state", "exists");
        // No x-goog-channel-id header

        // Act
        var result = await systemUnderTest.GooglePushWebhook(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();

        scopedSync.Verify(
            s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);

        scopedSync.Verify(
            s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);

        proxy.Verify(c => c.SendCoreAsync("EventsUpdated", Array.Empty<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (
        Mock<ICalendarSyncService> ConstructorSync,
        Mock<ICalendarSyncService> ScopedSync,
        Mock<IClientProxy> Proxy,
        SyncController SystemUnderTest) CreateSut(
        IEnumerable<string>? userIds = null,
        WebhookRegistration? webhookRegistration = null,
        CalendarInfo? calendarInfo = null)
    {
        var (constructorSync, scopedSync, proxy, sut, _) = CreateSutInternal(userIds, webhookRegistration, calendarInfo);
        return (constructorSync, scopedSync, proxy, sut);
    }

    private static (
        Mock<ICalendarSyncService> ConstructorSync,
        Mock<ICalendarSyncService> ScopedSync,
        Mock<IClientProxy> Proxy,
        SyncController SystemUnderTest,
        Mock<ITokenStore> TokenStore) CreateSutExposingTokenStore(
        IEnumerable<string>? userIds = null,
        WebhookRegistration? webhookRegistration = null,
        CalendarInfo? calendarInfo = null)
    {
        return CreateSutInternal(userIds, webhookRegistration, calendarInfo);
    }

    private static (
        Mock<ICalendarSyncService> ConstructorSync,
        Mock<ICalendarSyncService> ScopedSync,
        Mock<IClientProxy> Proxy,
        SyncController SystemUnderTest,
        Mock<ITokenStore> TokenStore) CreateSutInternal(
        IEnumerable<string>? userIds,
        WebhookRegistration? webhookRegistration,
        CalendarInfo? calendarInfo)
    {
        var syncServiceMock = new Mock<ICalendarSyncService>();
        var scopedSyncServiceMock = new Mock<ICalendarSyncService>();
        var hubContextMock = new Mock<IHubContext<CalendarHub>>();
        var clientProxyMock = new Mock<IClientProxy>();
        var loggerMock = new Mock<ILogger<SyncController>>();
        var tokenStoreMock = new Mock<ITokenStore>();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var webhookRepoMock = new Mock<IWebhookRegistrationRepository>();
        var calendarRepoMock = new Mock<ICalendarRepository>();

        var clientsMock = new Mock<IHubClients>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        tokenStoreMock
            .Setup(ts => ts.GetAllUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userIds ?? ["user1"]);

        if (webhookRegistration is not null)
        {
            webhookRepoMock
                .Setup(r => r.GetByChannelIdAsync(webhookRegistration.ChannelId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(webhookRegistration);
        }

        if (calendarInfo is not null)
        {
            calendarRepoMock
                .Setup(r => r.GetCalendarByIdAsync(calendarInfo.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(calendarInfo);
        }

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(ICalendarSyncService)))
            .Returns(scopedSyncServiceMock.Object);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var systemUnderTest = new SyncController(
            syncServiceMock.Object,
            hubContextMock.Object,
            tokenStoreMock.Object,
            scopeFactoryMock.Object,
            loggerMock.Object,
            webhookRepoMock.Object,
            calendarRepoMock.Object,
            new Mock<ICurrentUserService>().Object,
            new Mock<IWebhookRegistrationService>().Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return (syncServiceMock, scopedSyncServiceMock, clientProxyMock, systemUnderTest, tokenStoreMock);
    }
}
