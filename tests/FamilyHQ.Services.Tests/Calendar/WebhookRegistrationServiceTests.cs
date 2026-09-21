using System.Net;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
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

    /// <summary>FHQ-196. SHA-256 of <see cref="ExpectedWebhookUrl"/>, as the column stores it.</summary>
    private const string CurrentAddressHash = "b6b02363fac419d5948c31b0c09b500cb6e260eaa39d0bb5b5c1fa569b54c426";

    /// <summary>FHQ-196. A RelayRobin-shaped address whose path segment after /h/ is the route key.</summary>
    private const string RelayBaseUrl = "https://relay.example.com/h/s3cr3t-route-key";

    [Fact]
    public async Task RegisterForCalendarAsync_CallsWatchAndUpserts_WhenNoExistingRegistration()
    {
        // Arrange
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var channelId = "generated-channel-id";
        var resourceId = "resource-123";
        var expiration = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds();

        webhookRepo.Setup(r => r.GetByCalendarIdAsync(CalendarInfoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookRegistration?)null);

        client.Setup(c => c.WatchEventsAsync(
                GoogleCalendarId,
                It.IsAny<string>(),
                ExpectedWebhookUrl,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse(channelId, resourceId, expiration));

        // Act
        await sut.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId);

        // Assert
        client.Verify(c => c.WatchEventsAsync(
            GoogleCalendarId,
            It.IsAny<string>(),
            ExpectedWebhookUrl,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        webhookRepo.Verify(r => r.UpsertAsync(
            It.Is<WebhookRegistration>(reg =>
                reg.CalendarInfoId == CalendarInfoId &&
                reg.ChannelId == channelId &&
                reg.ResourceId == resourceId &&
                reg.ExpiresAt == DateTimeOffset.FromUnixTimeMilliseconds(expiration) &&
                !string.IsNullOrEmpty(reg.ChannelToken)),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert — no prior registration existed, so nothing should be stopped
        client.Verify(c => c.StopChannelAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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

        webhookRepo.Setup(r => r.GetByCalendarIdAsync(CalendarInfoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookRegistration?)null);

        client.Setup(c => c.WatchEventsAsync(
                It.IsAny<string>(),
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
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse("ch-1", "res-1", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()));

        // Act
        await sut.RegisterAllAsync(userId);

        // Assert
        client.Verify(c => c.WatchEventsAsync(
            "cal1@google.com",
            It.IsAny<string>(),
            ExpectedWebhookUrl,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        client.Verify(c => c.WatchEventsAsync(
            "cal2@google.com",
            It.IsAny<string>(),
            ExpectedWebhookUrl,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAllAsync_SkipsUserNeedingReauth()
    {
        // Arrange
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        tokenStore.Setup(t => t.GetAuthStatusAsync("reauth-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthStatusResult(TokenAuthStatus.NeedsReauth, "invalid_grant", DateTimeOffset.UtcNow));

        // Act
        await sut.RegisterAllAsync("reauth-user");

        // Assert — no calendar enumeration, no Google call
        calendarRepo.Verify(r => r.GetCalendarsByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(c => c.WatchEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAllAsync_WhenWatchThrowsReauth_MarksNeedsReauthAndRethrows()
    {
        // Arrange — FHQ-85: if the FIRST detection of a revoked grant happens during webhook
        // registration, the reauth must be persisted here (not swallowed) or the renewal cycle
        // keeps retrying the dead token forever.
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var userId = "reauth-during-registration";
        tokenStore.Setup(t => t.GetAuthStatusAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthStatusResult(TokenAuthStatus.Active, null, null));

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
        webhookRepo.Setup(r => r.GetByCalendarIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookRegistration?)null);

        client.Setup(c => c.WatchEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleReauthRequiredException(
                GoogleAuthFailureSource.TokenRefresh, "Token has been expired or revoked.", userId: userId));

        // Act & Assert — the reauth propagates to the caller (foreground → 409 via DomainExceptionHandler)
        await sut.Invoking(s => s.RegisterAllAsync(userId))
            .Should().ThrowAsync<GoogleReauthRequiredException>();

        // Persisted exactly once, and the second calendar was not attempted (same dead token).
        tokenStore.Verify(
            t => t.MarkNeedsReauthAsync(userId, "Token has been expired or revoked.", It.IsAny<CancellationToken>()),
            Times.Once);
        client.Verify(c => c.WatchEventsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAllAsync_WhenWatchThrowsReauth_MarksWithUncancellableToken()
    {
        // FHQ-85 review: SyncController's request token flows into RegisterAllAsync — but once
        // reauth is detected, the mark must survive a client abort, so the store call must
        // receive CancellationToken.None rather than the caller's token.
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var userId = "reauth-uncancellable";
        tokenStore.Setup(t => t.GetAuthStatusAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthStatusResult(TokenAuthStatus.Active, null, null));
        calendarRepo.Setup(r => r.GetCalendarsByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo>
            {
                new()
                {
                    Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                    GoogleCalendarId = "cal@google.com",
                    UserId = userId,
                    DisplayName = "Cal"
                }
            });
        webhookRepo.Setup(r => r.GetByCalendarIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookRegistration?)null);
        client.Setup(c => c.WatchEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleReauthRequiredException(
                GoogleAuthFailureSource.CalendarApi, "Unauthorized", userId: userId));

        using var requestCts = new CancellationTokenSource();

        await sut.Invoking(s => s.RegisterAllAsync(userId, ct: requestCts.Token))
            .Should().ThrowAsync<GoogleReauthRequiredException>();

        tokenStore.Verify(
            t => t.MarkNeedsReauthAsync(userId, "Unauthorized", CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task RegisterAllAsync_WhenPersistingMarkFails_StillThrowsOriginalReauthException()
    {
        // FHQ-85 review: a transient DB failure while marking must not replace the reauth
        // exception — the caller (foreground → 409 with reconnect payload) needs the original.
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var userId = "reauth-mark-fails";
        tokenStore.Setup(t => t.GetAuthStatusAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthStatusResult(TokenAuthStatus.Active, null, null));
        tokenStore.Setup(t => t.MarkNeedsReauthAsync(userId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));
        calendarRepo.Setup(r => r.GetCalendarsByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo>
            {
                new()
                {
                    Id = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa"),
                    GoogleCalendarId = "cal@google.com",
                    UserId = userId,
                    DisplayName = "Cal"
                }
            });
        webhookRepo.Setup(r => r.GetByCalendarIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookRegistration?)null);
        client.Setup(c => c.WatchEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleReauthRequiredException(
                GoogleAuthFailureSource.TokenRefresh, "Token has been expired or revoked.", userId: userId));

        await sut.Invoking(s => s.RegisterAllAsync(userId))
            .Should().ThrowAsync<GoogleReauthRequiredException>();
    }

    [Fact]
    public async Task RenewAllAsync_WhenOneUserThrowsReauth_MarksThatUserAndContinuesWithRemainingUsers()
    {
        // Arrange — user-1's token dies mid-renewal; user-2 must still get its channels renewed.
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var states = new List<UserAuthState>
        {
            new("user-1", TokenAuthStatus.Active),
            new("user-2", TokenAuthStatus.Active)
        };
        tokenStore.Setup(t => t.GetAllUserAuthStatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(states);
        tokenStore.Setup(t => t.GetAuthStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthStatusResult(TokenAuthStatus.Active, null, null));

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
        webhookRepo.Setup(r => r.GetByCalendarIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookRegistration?)null);

        client.Setup(c => c.WatchEventsAsync("u1-cal@google.com", It.IsAny<string>(), ExpectedWebhookUrl, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleReauthRequiredException(
                GoogleAuthFailureSource.CalendarApi, "Unauthorized", userId: "user-1"));
        client.Setup(c => c.WatchEventsAsync("u2-cal@google.com", It.IsAny<string>(), ExpectedWebhookUrl, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse("ch-2", "res-2", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()));

        // Act — must not throw; one user's dead token cannot abort the renewal cycle.
        await sut.Invoking(s => s.RenewAllAsync()).Should().NotThrowAsync();

        // Assert
        tokenStore.Verify(
            t => t.MarkNeedsReauthAsync("user-1", "Unauthorized", It.IsAny<CancellationToken>()),
            Times.Once);
        client.Verify(c => c.WatchEventsAsync(
            "u2-cal@google.com", It.IsAny<string>(), ExpectedWebhookUrl, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenewAllAsync_RegistersForAllUsersAndCalendars()
    {
        // Arrange
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var states = new List<UserAuthState>
        {
            new("user-1", TokenAuthStatus.Active),
            new("user-2", TokenAuthStatus.Active)
        };
        tokenStore.Setup(t => t.GetAllUserAuthStatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(states);
        tokenStore.Setup(t => t.GetAuthStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthStatusResult(TokenAuthStatus.Active, null, null));

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
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse("ch-x", "res-x", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()));

        // Act
        await sut.RenewAllAsync();

        // Assert
        tokenStore.Verify(t => t.GetAllUserAuthStatesAsync(It.IsAny<CancellationToken>()), Times.Once);

        client.Verify(c => c.WatchEventsAsync(
            "u1-cal@google.com",
            It.IsAny<string>(),
            ExpectedWebhookUrl,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        client.Verify(c => c.WatchEventsAsync(
            "u2-cal@google.com",
            It.IsAny<string>(),
            ExpectedWebhookUrl,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenewAllAsync_SkipsUsersNeedingReauth()
    {
        // Arrange
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var states = new List<UserAuthState>
        {
            new("active-user", TokenAuthStatus.Active),
            new("reauth-user", TokenAuthStatus.NeedsReauth)
        };
        tokenStore.Setup(t => t.GetAllUserAuthStatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(states);
        tokenStore.Setup(t => t.GetAuthStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthStatusResult(TokenAuthStatus.Active, null, null));

        var activeCalendar = new CalendarInfo
        {
            Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            GoogleCalendarId = "active@google.com",
            UserId = "active-user",
            DisplayName = "Active Cal"
        };
        calendarRepo.Setup(r => r.GetCalendarsByUserIdAsync("active-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { activeCalendar });
        client.Setup(c => c.WatchEventsAsync(It.IsAny<string>(), It.IsAny<string>(), ExpectedWebhookUrl, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse("ch", "res", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()));

        // Act
        await sut.RenewAllAsync();

        // Assert
        client.Verify(c => c.WatchEventsAsync("active@google.com", It.IsAny<string>(), ExpectedWebhookUrl, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        calendarRepo.Verify(r => r.GetCalendarsByUserIdAsync("reauth-user", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterForCalendarAsync_SkipsWhenRegistrationNotExpiredAndAddressUnchanged()
    {
        // Arrange
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var existing = new WebhookRegistration
        {
            CalendarInfoId = CalendarInfoId,
            ChannelId = "existing-channel",
            ResourceId = "existing-resource",
            // FHQ-196: skipping is now conditional on the channel being registered for the address
            // currently configured, so the stored hash has to match for this to be a skip at all.
            RegisteredAddressHash = CurrentAddressHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(3),
            RegisteredAt = DateTimeOffset.UtcNow.AddDays(-4)
        };

        webhookRepo.Setup(r => r.GetByCalendarIdAsync(CalendarInfoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Act
        await sut.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId);

        // Assert — should not call Google API
        client.Verify(c => c.WatchEventsAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterForCalendarAsync_RegistersWhenRegistrationExpiringSoon()
    {
        // Arrange — expires in 12 hours (< 24h threshold)
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var existing = new WebhookRegistration
        {
            CalendarInfoId = CalendarInfoId,
            ChannelId = "existing-channel",
            ResourceId = "existing-resource",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(12),
            RegisteredAt = DateTimeOffset.UtcNow.AddDays(-6)
        };

        webhookRepo.Setup(r => r.GetByCalendarIdAsync(CalendarInfoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        client.Setup(c => c.WatchEventsAsync(
                GoogleCalendarId,
                It.IsAny<string>(),
                ExpectedWebhookUrl,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse("new-ch", "new-res", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()));

        // Act
        await sut.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId);

        // Assert — should call Google API since expiry is within 24h
        client.Verify(c => c.WatchEventsAsync(
            GoogleCalendarId,
            It.IsAny<string>(),
            ExpectedWebhookUrl,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert — the old channel is stopped after the new one is registered
        client.Verify(c => c.StopChannelAsync(
            "existing-channel", "existing-resource", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterForCalendarAsync_ForceOverridesExpiryCheck()
    {
        // Arrange — registration is still valid (3 days left)
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var existing = new WebhookRegistration
        {
            CalendarInfoId = CalendarInfoId,
            ChannelId = "existing-channel",
            ResourceId = "existing-resource",
            // FHQ-196: the address matches, so force is the only thing that can be driving the
            // re-registration below — without this the test would pass on the mismatch path.
            RegisteredAddressHash = CurrentAddressHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(3),
            RegisteredAt = DateTimeOffset.UtcNow.AddDays(-4)
        };

        webhookRepo.Setup(r => r.GetByCalendarIdAsync(CalendarInfoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        client.Setup(c => c.WatchEventsAsync(
                GoogleCalendarId,
                It.IsAny<string>(),
                ExpectedWebhookUrl,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse("forced-ch", "forced-res", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()));

        // Act — force: true should bypass expiry check
        await sut.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId, force: true);

        // Assert — should call Google API despite valid registration
        client.Verify(c => c.WatchEventsAsync(
            GoogleCalendarId,
            It.IsAny<string>(),
            ExpectedWebhookUrl,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert — the force path also fetches and stops the old channel
        client.Verify(c => c.StopChannelAsync(
            "existing-channel", "existing-resource", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterForCalendarAsync_WhenWebhookNotSupported_SkipsAndMarksUnsupported()
    {
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();
        webhookRepo.Setup(r => r.GetByCalendarIdAsync(CalendarInfoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookRegistration?)null);
        client.Setup(c => c.WatchEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebhookNotSupportedException("WatchEvents", "pushNotSupportedForRequestedResource"));

        await sut.Invoking(s => s.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId)).Should().NotThrowAsync();

        calendarRepo.Verify(r => r.MarkWebhooksUnsupportedAsync(CalendarInfoId, It.IsAny<CancellationToken>()), Times.Once);
        webhookRepo.Verify(r => r.UpsertAsync(It.IsAny<WebhookRegistration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterForCalendarAsync_OnGenericFailure_DoesNotMarkUnsupported()
    {
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();
        webhookRepo.Setup(r => r.GetByCalendarIdAsync(CalendarInfoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookRegistration?)null);
        client.Setup(c => c.WatchEventsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient"));

        await sut.Invoking(s => s.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId)).Should().NotThrowAsync();

        calendarRepo.Verify(r => r.MarkWebhooksUnsupportedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAllAsync_SkipsCalendarsThatDoNotSupportWebhooks()
    {
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();
        tokenStore.Setup(t => t.GetAuthStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthStatusResult(TokenAuthStatus.Active, null, null));

        var supported = new CalendarInfo { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), GoogleCalendarId = "ok@google.com", UserId = "u1", DisplayName = "OK", WebhooksSupported = true };
        var unsupported = new CalendarInfo { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), GoogleCalendarId = "holidays@google.com", UserId = "u1", DisplayName = "Holidays", WebhooksSupported = false };
        calendarRepo.Setup(r => r.GetCalendarsByUserIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { supported, unsupported });
        webhookRepo.Setup(r => r.GetByCalendarIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((WebhookRegistration?)null);
        client.Setup(c => c.WatchEventsAsync(It.IsAny<string>(), It.IsAny<string>(), ExpectedWebhookUrl, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse("ch", "res", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()));

        await sut.RegisterAllAsync("u1");

        client.Verify(c => c.WatchEventsAsync("ok@google.com", It.IsAny<string>(), ExpectedWebhookUrl, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.WatchEventsAsync("holidays@google.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterForCalendarAsync_StoresChannelTokenInRegistration()
    {
        // Verify that the token generated locally is both sent to Google and persisted on the registration.
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        webhookRepo.Setup(r => r.GetByCalendarIdAsync(CalendarInfoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookRegistration?)null);

        var expiration = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds();
        string? capturedToken = null;

        client.Setup(c => c.WatchEventsAsync(
                GoogleCalendarId,
                It.IsAny<string>(),
                ExpectedWebhookUrl,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, CancellationToken>((_, _, _, token, _) => capturedToken = token)
            .ReturnsAsync(new WatchChannelResponse("ch-x", "res-x", expiration));

        await sut.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId);

        capturedToken.Should().NotBeNullOrEmpty();
        webhookRepo.Verify(r => r.UpsertAsync(
            It.Is<WebhookRegistration>(reg =>
                reg.CalendarInfoId == CalendarInfoId &&
                reg.ChannelToken == capturedToken),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterForCalendarAsync_WhenStopChannelReturns404_StillReportsSuccess()
    {
        // Arrange — old channel already expired on Google's side by the time we try to stop it
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var existing = new WebhookRegistration
        {
            CalendarInfoId = CalendarInfoId,
            ChannelId = "existing-channel",
            ResourceId = "existing-resource",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(12),
            RegisteredAt = DateTimeOffset.UtcNow.AddDays(-6)
        };

        webhookRepo.Setup(r => r.GetByCalendarIdAsync(CalendarInfoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        client.Setup(c => c.WatchEventsAsync(
                GoogleCalendarId,
                It.IsAny<string>(),
                ExpectedWebhookUrl,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse("new-ch", "new-res", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()));

        client.Setup(c => c.StopChannelAsync("existing-channel", "existing-resource", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleApiException(HttpStatusCode.NotFound, "StopChannel"));

        // Act
        var act = () => sut.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId);

        // Assert — does not throw, and the new registration still went through
        await act.Should().NotThrowAsync();

        webhookRepo.Verify(r => r.UpsertAsync(
            It.Is<WebhookRegistration>(reg => reg.ChannelId == "new-ch"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterForCalendarAsync_WhenStopChannelFailsWithOtherError_StillReportsSuccess()
    {
        // Arrange — an unexpected error stopping the old channel must not undo the new registration
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var existing = new WebhookRegistration
        {
            CalendarInfoId = CalendarInfoId,
            ChannelId = "existing-channel",
            ResourceId = "existing-resource",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(12),
            RegisteredAt = DateTimeOffset.UtcNow.AddDays(-6)
        };

        webhookRepo.Setup(r => r.GetByCalendarIdAsync(CalendarInfoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        client.Setup(c => c.WatchEventsAsync(
                GoogleCalendarId,
                It.IsAny<string>(),
                ExpectedWebhookUrl,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse("new-ch", "new-res", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()));

        client.Setup(c => c.StopChannelAsync("existing-channel", "existing-resource", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient"));

        // Act
        var act = () => sut.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId);

        // Assert — does not throw, and the new registration still went through
        await act.Should().NotThrowAsync();

        webhookRepo.Verify(r => r.UpsertAsync(
            It.Is<WebhookRegistration>(reg => reg.ChannelId == "new-ch"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── FHQ-196: a changed registration address takes effect on the next registration pass ───────

    [Fact]
    public async Task RegisterForCalendarAsync_StoresTheHashOfTheAddressItRegisteredWith()
    {
        // Without this the next pass cannot tell whether the channel belongs to the configured
        // address, and a changed address stays ignored for the life of the channel.
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        webhookRepo.Setup(r => r.GetByCalendarIdAsync(CalendarInfoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookRegistration?)null);
        client.Setup(c => c.WatchEventsAsync(
                GoogleCalendarId, It.IsAny<string>(), ExpectedWebhookUrl, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse("ch", "res", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()));

        await sut.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId);

        webhookRepo.Verify(r => r.UpsertAsync(
            It.Is<WebhookRegistration>(reg => reg.RegisteredAddressHash == CurrentAddressHash),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterForCalendarAsync_StillValidButRegisteredForAnotherAddress_ReRegistersAndStopsTheOldChannel()
    {
        // The bug FHQ-196 fixes: the channel has 3 days left, so the expiry check alone would skip
        // it and Google would keep posting to the previous address for up to ~6 days.
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var existing = new WebhookRegistration
        {
            CalendarInfoId = CalendarInfoId,
            ChannelId = "existing-channel",
            ResourceId = "existing-resource",
            RegisteredAddressHash = WebhookAddress.Hash("https://previous.example.com/api/sync/webhook"),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(3),
            RegisteredAt = DateTimeOffset.UtcNow.AddDays(-4)
        };

        webhookRepo.Setup(r => r.GetByCalendarIdAsync(CalendarInfoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        client.Setup(c => c.WatchEventsAsync(
                GoogleCalendarId, It.IsAny<string>(), ExpectedWebhookUrl, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse("new-ch", "new-res", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()));

        await sut.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId);

        client.Verify(c => c.WatchEventsAsync(
            GoogleCalendarId, It.IsAny<string>(), ExpectedWebhookUrl, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        webhookRepo.Verify(r => r.UpsertAsync(
            It.Is<WebhookRegistration>(reg =>
                reg.ChannelId == "new-ch" && reg.RegisteredAddressHash == CurrentAddressHash),
            It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.StopChannelAsync(
            "existing-channel", "existing-resource", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterForCalendarAsync_StillValidWithNoStoredAddressHash_ReRegisters()
    {
        // Every row that predates FHQ-196 is in this state. A missing hash counts as a mismatch,
        // so the first pass after the deploy re-registers the channel and fills the hash in — the
        // normal new-then-stop sequence, so notifications are not interrupted.
        var (client, webhookRepo, calendarRepo, tokenStore, sut) = CreateSut();

        var existing = new WebhookRegistration
        {
            CalendarInfoId = CalendarInfoId,
            ChannelId = "legacy-channel",
            ResourceId = "legacy-resource",
            RegisteredAddressHash = null,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(3),
            RegisteredAt = DateTimeOffset.UtcNow.AddDays(-4)
        };

        webhookRepo.Setup(r => r.GetByCalendarIdAsync(CalendarInfoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        client.Setup(c => c.WatchEventsAsync(
                GoogleCalendarId, It.IsAny<string>(), ExpectedWebhookUrl, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse("new-ch", "new-res", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()));

        await sut.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId);

        webhookRepo.Verify(r => r.UpsertAsync(
            It.Is<WebhookRegistration>(reg => reg.RegisteredAddressHash == CurrentAddressHash),
            It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.StopChannelAsync(
            "legacy-channel", "legacy-resource", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterForCalendarAsync_WhenTheAddressChanged_LogsItWithTheRouteKeyMasked()
    {
        // A RelayRobin address carries its route key in the path, and that key is the credential
        // that authorises posting notifications to FamilyHQ. It must never reach a log sink.
        var (client, webhookRepo, calendarRepo, tokenStore, sut, logger) = CreateSutWithLogger(webhookBaseUrl: RelayBaseUrl);

        var existing = new WebhookRegistration
        {
            CalendarInfoId = CalendarInfoId,
            ChannelId = "existing-channel",
            ResourceId = "existing-resource",
            RegisteredAddressHash = WebhookAddress.Hash("https://previous.example.com/api/sync/webhook"),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(3),
            RegisteredAt = DateTimeOffset.UtcNow.AddDays(-4)
        };

        webhookRepo.Setup(r => r.GetByCalendarIdAsync(CalendarInfoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        client.Setup(c => c.WatchEventsAsync(
                GoogleCalendarId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WatchChannelResponse("new-ch", "new-res", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds()));

        await sut.RegisterForCalendarAsync(CalendarInfoId, GoogleCalendarId);

        VerifyNothingLoggedContaining(logger, "s3cr3t-route-key");
        VerifySomethingLoggedContaining(logger, "https://relay.example.com/h/***/api/sync/webhook");
    }

    private static void VerifyNothingLoggedContaining(Mock<ILogger<WebhookRegistrationService>> logger, string fragment) =>
        logger.Verify(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => Carries(v, fragment)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            $"'{fragment}' is the route key that authorises posting to FamilyHQ and must never reach a log sink");

    private static void VerifySomethingLoggedContaining(Mock<ILogger<WebhookRegistrationService>> logger, string fragment) =>
        logger.Verify(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => Carries(v, fragment)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            $"masking must leave '{fragment}' behind, or the line cannot say which address was used");

    private static bool Carries(object? state, string fragment)
    {
        if (state?.ToString()?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return state is IReadOnlyList<KeyValuePair<string, object?>> values
            && values.Any(kv => kv.Value?.ToString()?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static (
        Mock<IGoogleCalendarClient> client,
        Mock<IWebhookRegistrationRepository> webhookRepo,
        Mock<ICalendarRepository> calendarRepo,
        Mock<ITokenStore> tokenStore,
        WebhookRegistrationService sut) CreateSut(bool webhookEnabled = true)
    {
        var (client, webhookRepo, calendarRepo, tokenStore, sut, _) = CreateSutWithLogger(webhookEnabled);

        return (client, webhookRepo, calendarRepo, tokenStore, sut);
    }

    private static (
        Mock<IGoogleCalendarClient> client,
        Mock<IWebhookRegistrationRepository> webhookRepo,
        Mock<ICalendarRepository> calendarRepo,
        Mock<ITokenStore> tokenStore,
        WebhookRegistrationService sut,
        Mock<ILogger<WebhookRegistrationService>> logger) CreateSutWithLogger(
            bool webhookEnabled = true, string webhookBaseUrl = WebhookBaseUrl)
    {
        var clientMock = new Mock<IGoogleCalendarClient>();
        var webhookRepoMock = new Mock<IWebhookRegistrationRepository>();
        var calendarRepoMock = new Mock<ICalendarRepository>();
        var tokenStoreMock = new Mock<ITokenStore>();
        var loggerMock = new Mock<ILogger<WebhookRegistrationService>>();

        var syncOptions = new SyncOptions
        {
            WebhookRegistrationEnabled = webhookEnabled,
            WebhookBaseUrl = webhookBaseUrl
        };
        var optionsMock = Microsoft.Extensions.Options.Options.Create(syncOptions);

        var sut = new WebhookRegistrationService(
            clientMock.Object,
            webhookRepoMock.Object,
            calendarRepoMock.Object,
            tokenStoreMock.Object,
            optionsMock,
            loggerMock.Object);

        return (clientMock, webhookRepoMock, calendarRepoMock, tokenStoreMock, sut, loggerMock);
    }
}
