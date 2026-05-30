using System.Net;
using System.Reflection;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.WebApi.Controllers;
using FamilyHQ.WebApi.Hubs;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
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
        var (constructorSync, proxy, systemUnderTest) = CreateSut();

        // Act
        var result = await systemUnderTest.TriggerSync(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();

        constructorSync.Verify(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        proxy.Verify(c => c.SendCoreAsync("EventsUpdated", Array.Empty<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void TriggerSync_HasAuthorizeAttribute()
    {
        // FHQ-31 regression guard: without [Authorize], an unauthenticated POST
        // to /api/sync/trigger reaches SyncAllAsync, which silently no-ops on a
        // null UserId — returning 200 OK with no work done. That silent-success
        // path was the root cause of the Deploy-Staging #110 reauth flake.
        var method = typeof(SyncController).GetMethod(nameof(SyncController.TriggerSync))!;
        method.GetCustomAttribute<AuthorizeAttribute>(inherit: false)
            .Should().NotBeNull(
                "TriggerSync must be [Authorize]-gated so unauthenticated requests return 401, " +
                "not a silent 200 that masks reauth-marking failures (FHQ-31).");
    }

    [Fact]
    public async Task TriggerSync_WhenCurrentUserIdIsNull_ReturnsUnauthorized()
    {
        // FHQ-31: belt-and-braces guard. Even if the principal is authenticated,
        // a missing 'sub' claim would let an unidentifiable request enter
        // SyncAllAsync, which would log-and-return silently. Refuse explicitly.
        var (constructorSync, _, systemUnderTest) = CreateSut(currentUserId: null);

        var result = await systemUnderTest.TriggerSync(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
        constructorSync.Verify(
            s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "SyncAllAsync must not be invoked when the caller has no resolvable user id.");
    }

    // FHQ-37: the webhook now enqueues a durable job and acks immediately rather than
    // running the sync inline on the request thread (which, with the request
    // CancellationToken, caused FHQ-36). These tests assert the enqueue+wake+ack contract.

    [Fact]
    public async Task GooglePushWebhook_WithNoChannelId_EnqueuesPeriodicJobPerUserAndAcks()
    {
        // Arrange
        var harness = CreateSutInternal(userIds: ["user1", "user2"],
            webhookRegistration: null, calendarInfo: null, currentUserId: "__default__");
        harness.SystemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-resource-state", "sync");

        // Act
        var result = await harness.SystemUnderTest.GooglePushWebhook(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();

        harness.JobQueue.Verify(
            q => q.EnqueueAsync("user1", null, SyncJobSource.Periodic, null, It.IsAny<CancellationToken>()),
            Times.Once);
        harness.JobQueue.Verify(
            q => q.EnqueueAsync("user2", null, SyncJobSource.Periodic, null, It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Signal.Verify(s => s.Release(), Times.Once);

        // No inline sync work on the request thread.
        harness.ConstructorSync.Verify(
            s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.ConstructorSync.Verify(
            s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GooglePushWebhook_WhenNoRegisteredUsers_StillAcks()
    {
        // Arrange
        var harness = CreateSutInternal(userIds: [],
            webhookRegistration: null, calendarInfo: null, currentUserId: "__default__");
        harness.SystemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-resource-state", "sync");

        // Act
        var result = await harness.SystemUnderTest.GooglePushWebhook(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();
        harness.JobQueue.Verify(
            q => q.EnqueueAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<SyncJobSource>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.Signal.Verify(s => s.Release(), Times.Once);
    }

    [Fact]
    public async Task GooglePushWebhook_WithMatchingChannelId_EnqueuesTargetedWebhookJobAndAcks()
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

        var harness = CreateSutInternal(userIds: null,
            webhookRegistration: registration, calendarInfo: calendarInfo, currentUserId: "__default__");

        harness.SystemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-resource-state", "exists");
        harness.SystemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-channel-id", channelId);

        // Act
        var result = await harness.SystemUnderTest.GooglePushWebhook(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();

        harness.JobQueue.Verify(
            q => q.EnqueueAsync(userId, calendarInfoId, SyncJobSource.Webhook, channelId, It.IsAny<CancellationToken>()),
            Times.Once);
        harness.JobQueue.Verify(
            q => q.EnqueueAsync(It.IsAny<string>(), null, SyncJobSource.Periodic, null, It.IsAny<CancellationToken>()),
            Times.Never);
        harness.Signal.Verify(s => s.Release(), Times.Once);

        // No inline sync work on the request thread.
        harness.ConstructorSync.Verify(
            s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.ConstructorSync.Verify(
            s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GooglePushWebhook_WithUnknownChannelId_FallsBackToPeriodicEnqueue()
    {
        // Arrange
        var harness = CreateSutInternal(userIds: ["user1"],
            webhookRegistration: null, calendarInfo: null, currentUserId: "__default__");

        harness.SystemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-resource-state", "exists");
        harness.SystemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-channel-id", "unknown-channel");

        // Act
        var result = await harness.SystemUnderTest.GooglePushWebhook(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();

        harness.JobQueue.Verify(
            q => q.EnqueueAsync("user1", null, SyncJobSource.Periodic, null, It.IsAny<CancellationToken>()),
            Times.Once);
        harness.JobQueue.Verify(
            q => q.EnqueueAsync(It.IsAny<string>(), It.IsAny<Guid?>(), SyncJobSource.Webhook, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.Signal.Verify(s => s.Release(), Times.Once);
    }

    [Fact]
    public async Task TriggerSync_WhenSyncThrowsGoogleReauthRequired_Returns409Conflict()
    {
        // Arrange
        var (constructorSync, _, systemUnderTest) = CreateSut();
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
        var (constructorSync, _, systemUnderTest) = CreateSut();
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
    public async Task GooglePushWebhook_WhenEnqueueThrows_StillAcksWith200()
    {
        // FHQ-37: a 200 ack stops Google's retries; the periodic safety net reconciles.
        // The webhook must never surface an enqueue failure as a non-200.
        var harness = CreateSutInternal(userIds: ["user1"],
            webhookRegistration: null, calendarInfo: null, currentUserId: "__default__");
        harness.JobQueue
            .Setup(q => q.EnqueueAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<SyncJobSource>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        harness.SystemUnderTest.ControllerContext.HttpContext.Request.Headers.Append("x-goog-resource-state", "sync");

        // Act
        var result = await harness.SystemUnderTest.GooglePushWebhook(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkResult>();
    }

    // Sentinel that distinguishes "default: a valid test user" from "explicitly null".
    // Reference equality lets the helper detect whether the caller passed `null` or
    // accepted the default — Optional<T?> would be tidier but C# doesn't ship one.
    private static readonly string DefaultTestUserId = "test-user-id";

    private static (
        Mock<ICalendarSyncService> ConstructorSync,
        Mock<IClientProxy> Proxy,
        SyncController SystemUnderTest) CreateSut(
        IEnumerable<string>? userIds = null,
        WebhookRegistration? webhookRegistration = null,
        CalendarInfo? calendarInfo = null,
        string? currentUserId = "__default__")
    {
        var harness = CreateSutInternal(userIds, webhookRegistration, calendarInfo, currentUserId);
        return (harness.ConstructorSync, harness.Proxy, harness.SystemUnderTest);
    }

    private static (
        Mock<ICalendarSyncService> ConstructorSync,
        Mock<IClientProxy> Proxy,
        SyncController SystemUnderTest,
        Mock<ITokenStore> TokenStore) CreateSutExposingTokenStore(
        IEnumerable<string>? userIds = null,
        WebhookRegistration? webhookRegistration = null,
        CalendarInfo? calendarInfo = null,
        string? currentUserId = "__default__")
    {
        var harness = CreateSutInternal(userIds, webhookRegistration, calendarInfo, currentUserId);
        return (harness.ConstructorSync, harness.Proxy, harness.SystemUnderTest, harness.TokenStore);
    }

    private sealed record SutHarness(
        Mock<ICalendarSyncService> ConstructorSync,
        Mock<IClientProxy> Proxy,
        SyncController SystemUnderTest,
        Mock<ITokenStore> TokenStore,
        Mock<ICalendarSyncJobQueue> JobQueue,
        Mock<ISyncJobSignal> Signal);

    private static SutHarness CreateSutInternal(
        IEnumerable<string>? userIds,
        WebhookRegistration? webhookRegistration,
        CalendarInfo? calendarInfo,
        string? currentUserId)
    {
        var syncServiceMock = new Mock<ICalendarSyncService>();
        var hubContextMock = new Mock<IHubContext<CalendarHub>>();
        var clientProxyMock = new Mock<IClientProxy>();
        var loggerMock = new Mock<ILogger<SyncController>>();
        var tokenStoreMock = new Mock<ITokenStore>();
        var webhookRepoMock = new Mock<IWebhookRegistrationRepository>();
        var calendarRepoMock = new Mock<ICalendarRepository>();
        var jobQueueMock = new Mock<ICalendarSyncJobQueue>();
        var signalMock = new Mock<ISyncJobSignal>();

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

        var currentUserMock = new Mock<ICurrentUserService>();
        var resolvedUserId = ReferenceEquals(currentUserId, "__default__") ? DefaultTestUserId : currentUserId;
        currentUserMock.Setup(c => c.UserId).Returns(resolvedUserId);

        var systemUnderTest = new SyncController(
            syncServiceMock.Object,
            hubContextMock.Object,
            tokenStoreMock.Object,
            loggerMock.Object,
            webhookRepoMock.Object,
            calendarRepoMock.Object,
            currentUserMock.Object,
            new Mock<IWebhookRegistrationService>().Object,
            jobQueueMock.Object,
            signalMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return new SutHarness(
            syncServiceMock,
            clientProxyMock,
            systemUnderTest,
            tokenStoreMock,
            jobQueueMock,
            signalMock);
    }
}
