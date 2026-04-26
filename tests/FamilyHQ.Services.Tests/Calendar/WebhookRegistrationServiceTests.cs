using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FamilyHQ.Services.Tests.Calendar;

public class WebhookRegistrationServiceTests
{
    private static readonly Guid CalendarInfoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string GoogleCalendarId = "test@group.calendar.google.com";
    private const string WebhookBaseUrl = "https://familyhq.example.com";
    private const string ExpectedWebhookUrl = "https://familyhq.example.com/api/sync/webhook";

    [Fact]
    public async Task RegisterForCalendarAsync_CallsWatchAndUpserts()
    {
        // Arrange
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var channelId = "generated-channel-id";
        var resourceId = "resource-123";
        var expiration = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds();

        client.Setup(c => c.WatchEventsAsync(
                GoogleCalendarId,
                It.IsAny<string>(),
                ExpectedWebhookUrl,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse(channelId, resourceId, expiration));

        // Act
        await sut.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId);

        // Assert
        client.Verify(c => c.WatchEventsAsync(
            GoogleCalendarId,
            It.IsAny<string>(),
            ExpectedWebhookUrl,
            It.IsAny<CancellationToken>()), Times.Once);

        webhookRepo.Verify(r => r.UpsertAsync(
            It.Is<WebhookRegistration>(reg =>
                reg.CalendarInfoId == CalendarInfoId &&
                reg.ChannelId == channelId &&
                reg.ResourceId == resourceId &&
                reg.ExpiresAt == DateTimeOffset.FromUnixTimeMilliseconds(expiration)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterForCalendarAsync_SkipsWhenDisabled()
    {
        // Arrange
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut(webhookEnabled: false);

        // Act
        await sut.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId);

        // Assert
        client.Verify(c => c.WatchEventsAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);

        webhookRepo.Verify(r => r.UpsertAsync(
            It.IsAny<WebhookRegistration>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterForCalendarAsync_LogsErrorOnFailure()
    {
        // Arrange
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        client.Setup(c => c.WatchEventsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Google API error"));

        // Act
        var act = () => sut.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId);

        // Assert - should not throw
        await act.Should().NotThrowAsync();

        webhookRepo.Verify(r => r.UpsertAsync(
            It.IsAny<WebhookRegistration>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAllAsync_RegistersEachCalendar()
    {
        // Arrange
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var userId = "user-1";
        var calendar1 = new CalendarInfo
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            GoogleCalendarId = "cal1@google.com",
            UserId = userId,
            DisplayName = "Cal 1"
        };
        var calendar2 = new CalendarInfo
        {
            Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            GoogleCalendarId = "cal2@google.com",
            UserId = userId,
            DisplayName = "Cal 2"
        };

        calendarRepo.Setup(r => r.GetCalendarsByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calendar1, calendar2 });

        client.Setup(c => c.WatchEventsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                ExpectedWebhookUrl,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse("ch-1", "res-1", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()));

        // Act
        await sut.RegisterAllAsync(userId);

        // Assert
        client.Verify(c => c.WatchEventsAsync(
            "cal1@google.com",
            It.IsAny<string>(),
            ExpectedWebhookUrl,
            It.IsAny<CancellationToken>()), Times.Once);

        client.Verify(c => c.WatchEventsAsync(
            "cal2@google.com",
            It.IsAny<string>(),
            ExpectedWebhookUrl,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenewAllAsync_RegistersForAllUsersAndCalendars()
    {
        // Arrange
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var userIds = new[] { "user-1", "user-2" };
        tokenStore.Setup(t => t.GetAllUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userIds);

        var user1Calendar = new CalendarInfo
        {
            Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            GoogleCalendarId = "u1-cal@google.com",
            UserId = "user-1",
            DisplayName = "User 1 Cal"
        };
        var user2Calendar = new CalendarInfo
        {
            Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            GoogleCalendarId = "u2-cal@google.com",
            UserId = "user-2",
            DisplayName = "User 2 Cal"
        };

        calendarRepo.Setup(r => r.GetCalendarsByUserIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { user1Calendar });
        calendarRepo.Setup(r => r.GetCalendarsByUserIdAsync("user-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { user2Calendar });

        client.Setup(c => c.WatchEventsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                ExpectedWebhookUrl,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse("ch-x", "res-x", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()));

        // Act
        await sut.RenewAllAsync();

        // Assert
        tokenStore.Verify(t => t.GetAllUserIdsAsync(It.IsAny<CancellationToken>()), Times.Once);

        client.Verify(c => c.WatchEventsAsync(
            "u1-cal@google.com",
            It.IsAny<string>(),
            ExpectedWebhookUrl,
            It.IsAny<CancellationToken>()), Times.Once);

        client.Verify(c => c.WatchEventsAsync(
            "u2-cal@google.com",
            It.IsAny<string>(),
            ExpectedWebhookUrl,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (
        Mock<IGoogleCalendarClient> client,
        Mock<IWebhookRegistrationRepository> webhookRepo,
        Mock<ICalendarRepository> calendarRepo,
        Mock<ITokenStore> tokenStore,
        WebhookRegistrationService sut) CreateSut(bool webhookEnabled = true)
    {
        var clientMock = new Mock<IGoogleCalendarClient>();
        var webhookRepoMock = new Mock<IWebhookRegistrationRepository>();
        var calendarRepoMock = new Mock<ICalendarRepository>();
        var tokenStoreMock = new Mock<ITokenStore>();
        var loggerMock = new Mock<ILogger<WebhookRegistrationService>>();

        var syncOptions = new SyncOptions
        {
            WebhookRegistrationEnabled = webhookEnabled,
            WebhookBaseUrl = WebhookBaseUrl
        };
        var optionsMock = Microsoft.Extensions.Options.Options.Create(syncOptions);

        var sut = new WebhookRegistrationService(
            clientMock.Object,
            webhookRepoMock.Object,
            calendarRepoMock.Object,
            tokenStoreMock.Object,
            optionsMock,
            loggerMock.Object);

        return (clientMock, webhookRepoMock, calendarRepoMock, tokenStoreMock, sut);
    }
}
