using System.Net;
using System.Text.Json;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class GoogleCalendarClientTests
{
    [Fact]
    public async Task GetCalendarsAsync_WhenNoRefreshToken_ThrowsInvalidOperationException()
    {
        // Arrange
        var (http, tokenStore, systemUnderTest) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        // Act & Assert
        await systemUnderTest.Invoking(s => s.GetCalendarsAsync())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No refresh token available*");
    }

    [Fact]
    public async Task GetCalendarsAsync_WhenAuthorized_ReturnsCalendars()
    {
        // Arrange
        var (http, tokenStore, systemUnderTest) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");

        // We need to mock BOTH the Auth Service refresh token call AND the Calendar API call
        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("auth.test.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { access_token = "new-access", expires_in = 3600, token_type = "Bearer" }))
            });

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("users/me/calendarList")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    items = new[]
                    {
                        new { id = "cal1", summary = "My Cal", backgroundColor = "#ff0000" }
                    }
                }))
            });

        // Act
        var result = await systemUnderTest.GetCalendarsAsync();

        // Assert
        result.Should().HaveCount(1);
        result.First().GoogleCalendarId.Should().Be("cal1");
        result.First().DisplayName.Should().Be("My Cal");
        result.First().Color.Should().Be("#ff0000");
    }

    [Fact]
    public async Task GetEventsAsync_WithSyncTokenExpired_ThrowsInvalidOperationException()
    {
        // Arrange
        var (http, tokenStore, systemUnderTest) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("auth.test.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { access_token = "new-access", expires_in = 3600, token_type = "Bearer" }))
            });

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("events")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Gone // 410 Gone triggers the exception
            });

        // Act & Assert
        await systemUnderTest.Invoking(s => s.GetEventsAsync("cal1", null, null, "expired-token"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Full sync required*");
    }

    [Fact]
    public async Task GetEventsAsync_WhenAuthorized_ReturnsEventsAndNextSyncToken()
    {
        // Arrange
        var (http, tokenStore, systemUnderTest) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("auth.test.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { access_token = "new-access", expires_in = 3600, token_type = "Bearer" }))
            });

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("events")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    nextSyncToken = "new-sync-token",
                    items = new object[]
                    {
                        new { id = "firstEvent", status = "confirmed", summary = "Event 1", start = new { dateTime = "2026-03-01T10:00:00Z" }, end = new { dateTime = "2026-03-01T11:00:00Z" } },
                        new { id = "secondEvent", status = "cancelled" } // Tombstone
                    }
                }))
            });

        // Act
        var now = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var result = await systemUnderTest.GetEventsAsync("cal1", now.AddDays(-1), now.AddDays(1));

        // Assert
        result.NextSyncToken.Should().Be("new-sync-token");
        result.Events.Should().HaveCount(2);
        
        var firstEvent = result.Events.First(e => e.GoogleEventId == "firstEvent");
        firstEvent.Title.Should().Be("Event 1");
        firstEvent.IsAllDay.Should().BeFalse();
        
        var tombstone = result.Events.First(e => e.GoogleEventId == "secondEvent");
        tombstone.Title.Should().Be("CANCELLED_TOMBSTONE");
    }
    
    [Fact]
    public async Task CreateEventAsync_ReturnsEventWithNewId()
    {
        // Arrange
        var (http, tokenStore, systemUnderTest) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("auth.test.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { access_token = "new-access", expires_in = 3600, token_type = "Bearer" }))
            });

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains("events")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { id = "new-google-event-id" }))
            });

        var newEvent = new CalendarEvent
        {
            Title = "Test Event",
            Start = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 3, 1, 1, 0, 0, TimeSpan.Zero),
            IsAllDay = false
        };

        // Act
        var result = await systemUnderTest.CreateEventAsync("cal1", newEvent);

        // Assert
        result.GoogleEventId.Should().Be("new-google-event-id");
        result.Title.Should().Be("Test Event");
    }

    [Fact]
    public async Task UpdateEventAsync_ReturnsUpdatedEvent()
    {
        // Arrange
        var (http, tokenStore, systemUnderTest) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("auth.test.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { access_token = "new-access", expires_in = 3600, token_type = "Bearer" }))
            });

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Put && req.RequestUri!.ToString().Contains("events/existing-id")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { id = "existing-id" }))
            });

        var updatedEvent = new CalendarEvent
        {
            GoogleEventId = "existing-id",
            Title = "Updated Event",
            Start = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 3, 1, 1, 0, 0, TimeSpan.Zero)
        };

        // Act
        var result = await systemUnderTest.UpdateEventAsync("cal1", updatedEvent);

        // Assert
        result.GoogleEventId.Should().Be("existing-id");
        result.Title.Should().Be("Updated Event");
    }

    [Fact]
    public async Task DeleteEventAsync_CompletesSuccessfully()
    {
        // Arrange
        var (http, tokenStore, systemUnderTest) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("auth.test.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { access_token = "new-access", expires_in = 3600, token_type = "Bearer" }))
            });

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Delete && req.RequestUri!.ToString().Contains("events/to-delete-id")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NoContent
            });

        // Act & Assert
        await systemUnderTest.Invoking(s => s.DeleteEventAsync("cal1", "to-delete-id"))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task MoveEventAsync_ReturnsPreservedEventId()
    {
        // Arrange — Google's move endpoint preserves the original event ID
        var (http, tokenStore, systemUnderTest) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("auth.test.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { access_token = "new-access", expires_in = 3600, token_type = "Bearer" }))
            });

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains("events/evt-123/move") &&
                    req.RequestUri.Query.Contains("destination=to-cal")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { id = "evt-123" }))
            });

        // Act
        var returnedId = await systemUnderTest.MoveEventAsync("from-cal", "evt-123", "to-cal");

        // Assert — same event ID returned (Google preserves it on move)
        returnedId.Should().Be("evt-123");
    }

    [Fact]
    public async Task DeleteEventAsync_WhenEventNotFound_TreatsAsSuccess()
    {
        // Arrange — 404 means the event is already gone; we should not throw
        var (http, tokenStore, systemUnderTest) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("auth.test.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { access_token = "new-access", expires_in = 3600, token_type = "Bearer" }))
            });

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Delete && req.RequestUri!.ToString().Contains("events/already-gone-id")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound
            });

        // Act & Assert — idempotent delete: 404 is treated as success
        await systemUnderTest.Invoking(s => s.DeleteEventAsync("cal1", "already-gone-id"))
            .Should().NotThrowAsync();
    }

    private static (Mock<HttpMessageHandler> HttpMock, Mock<ITokenStore> TokenMock, GoogleCalendarClient systemUnderTest) CreateSut()
    {
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(httpMessageHandlerMock.Object);

        var tokenStoreMock = new Mock<ITokenStore>();

        var options = Microsoft.Extensions.Options.Options.Create(new GoogleCalendarOptions
        {
            CalendarApiBaseUrl = "https://calendar.test.com",
            ClientId = "test-client",
            ClientSecret = "test-secret",
            AuthBaseUrl = "https://auth.test.com"
        });

        var authLoggerMock = new Mock<ILogger<GoogleAuthService>>();
        var authService = new GoogleAuthService(httpClient, options, authLoggerMock.Object);

        var loggerMock = new Mock<ILogger<GoogleCalendarClient>>();
        var accessTokenProviderMock = new Mock<IAccessTokenProvider>();

        var systemUnderTest = new GoogleCalendarClient(
            httpClient,
            authService,
            tokenStoreMock.Object,
            accessTokenProviderMock.Object,
            options,
            loggerMock.Object);

        return (httpMessageHandlerMock, tokenStoreMock, systemUnderTest);
    }
}
