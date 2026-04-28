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
    public async Task GetEventsAsync_WithSyncWindow_TimeParametersAreRfc3339WithZSuffix()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
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
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { nextSyncToken = "tok", items = Array.Empty<object>() }))
            });

        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

        // Act
        await systemUnderTest.GetEventsAsync("cal1", start, end);

        // Assert — timeMin and timeMax must not contain unencoded '+' and must use 'Z' suffix
        var uri = capturedRequest!.RequestUri!.ToString();
        uri.Should().Contain("timeMin=2026-01-01T00%3A00%3A00Z");
        uri.Should().Contain("timeMax=2026-04-01T00%3A00%3A00Z");
        uri.Should().NotContain("+"); // unencoded '+' would be misinterpreted as space
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
        var result = await systemUnderTest.CreateEventAsync("cal1", newEvent, "testhash");

        // Assert
        result.GoogleEventId.Should().Be("new-google-event-id");
        result.Title.Should().Be("Test Event");
    }

    [Theory]
    // Inclusive end-of-day (legacy EventModal representation): 23:59:59.9999999 → next day exclusive
    [InlineData("2026-04-28T00:00:00Z", "2026-04-28T23:59:59.9999999Z", "2026-04-28", "2026-04-29")]
    // Already-exclusive next-day midnight: pass through unchanged
    [InlineData("2026-04-28T00:00:00Z", "2026-04-29T00:00:00Z",         "2026-04-28", "2026-04-29")]
    // Multi-day inclusive end: roll up to next-day exclusive
    [InlineData("2026-04-28T00:00:00Z", "2026-04-30T23:59:59.9999999Z", "2026-04-28", "2026-05-01")]
    // Multi-day already-exclusive: pass through unchanged
    [InlineData("2026-04-28T00:00:00Z", "2026-05-01T00:00:00Z",         "2026-04-28", "2026-05-01")]
    // Pathological End == Start (the post-sync corruption case): force a one-day exclusive end
    [InlineData("2026-04-28T00:00:00Z", "2026-04-28T00:00:00Z",         "2026-04-28", "2026-04-29")]
    // Mid-day End (e.g. user toggled IsAllDay without resetting times): treat the day as inclusive
    [InlineData("2026-04-28T00:00:00Z", "2026-04-28T10:00:00Z",         "2026-04-28", "2026-04-29")]
    public async Task CreateEventAsync_AllDayEvent_SendsExclusiveEndDateToGoogle(
        string startIso, string endIso, string expectedStartDate, string expectedEndDate)
    {
        // Arrange — Google Calendar API requires `end.date` to be the day AFTER the last day of an
        // all-day event (exclusive). FamilyHQ's local model has historically stored End as either an
        // inclusive end-of-day tick or an exclusive next-day midnight; the boundary mapping must
        // normalise both into Google's exclusive format.
        string? capturedBody = null;
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
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { id = "new-id" }))
            });

        var newEvent = new CalendarEvent
        {
            Title = "All Day",
            Start = DateTimeOffset.Parse(startIso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            End = DateTimeOffset.Parse(endIso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
            IsAllDay = true
        };

        // Act
        await systemUnderTest.CreateEventAsync("cal1", newEvent, "testhash");

        // Assert
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var root = doc.RootElement;
        root.GetProperty("start").GetProperty("date").GetString().Should().Be(expectedStartDate);
        root.GetProperty("end").GetProperty("date").GetString().Should().Be(expectedEndDate);
        root.GetProperty("start").TryGetProperty("dateTime", out _).Should().BeFalse("all-day events use date, not dateTime");
        root.GetProperty("end").TryGetProperty("dateTime", out _).Should().BeFalse("all-day events use date, not dateTime");
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
        var result = await systemUnderTest.UpdateEventAsync("cal1", updatedEvent, "testhash");

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
