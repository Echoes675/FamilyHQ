using System.Net;
using System.Text.Json;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using FamilyHQ.Core.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class GoogleCalendarClientMappingTests
{
    [Fact]
    public async Task GetEventAsync_MapsOrganizerToGoogleEventDetail()
    {
        // Arrange
        var (http, tokenStore, sut) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");
        SetupAuthResponse(http);

        var json = JsonSerializer.Serialize(new
        {
            id = "evt-123",
            status = "confirmed",
            summary = "Team Meeting",
            start = new { dateTime = "2026-03-01T10:00:00Z" },
            end   = new { dateTime = "2026-03-01T11:00:00Z" },
            organizer = new { email = "org@calendar.google.com", self = true },
            attendees = new[] {
                new { email = "att1@calendar.google.com", responseStatus = "accepted" },
                new { email = "att2@calendar.google.com", responseStatus = "accepted" }
            }
        });

        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("events/evt-123")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json)
            });

        // Act
        var result = await sut.GetEventAsync("org@calendar.google.com", "evt-123", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("evt-123");
        result.OrganizerEmail.Should().Be("org@calendar.google.com");
    }

    [Fact]
    public async Task GetEventAsync_ReturnsNullOn404()
    {
        // Arrange
        var (http, tokenStore, sut) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");
        SetupAuthResponse(http);

        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("events/no-such")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.NotFound });

        // Act
        var result = await sut.GetEventAsync("org@calendar.google.com", "no-such", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ── GetEventsAsync mapping ────────────────────────────────────────────────

    [Fact]
    public async Task GetEventsAsync_ReturnsEventWithCorrectTitle()
    {
        // Arrange
        var (http, tokenStore, sut) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");
        SetupAuthResponse(http);

        var json = JsonSerializer.Serialize(new
        {
            items = new[] {
                new {
                    id = "evt-1", status = "confirmed", summary = "External Meeting",
                    start = new { dateTime = "2026-03-01T10:00:00Z" },
                    end   = new { dateTime = "2026-03-01T11:00:00Z" },
                    organizer = new { email = "other@calendar.google.com", self = false }
                }
            },
            nextSyncToken = "token-1"
        });
        SetupEventsResponse(http, json);

        // Act
        var (events, _) = await sut.GetEventsAsync("cal@google.com",
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero),
            ct: CancellationToken.None);

        // Assert
        events.Should().ContainSingle(e => e.Title == "External Meeting");
    }

    [Fact]
    public async Task GetEventsAsync_WhenOrganizerSelfTrue_ReturnsSingleEvent()
    {
        // Arrange
        var (http, tokenStore, sut) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");
        SetupAuthResponse(http);

        var json = JsonSerializer.Serialize(new
        {
            items = new[] {
                new {
                    id = "evt-2", status = "confirmed", summary = "My Meeting",
                    start = new { dateTime = "2026-03-01T10:00:00Z" },
                    end   = new { dateTime = "2026-03-01T11:00:00Z" },
                    organizer = new { email = "me@calendar.google.com", self = true }
                }
            },
            nextSyncToken = "token-2"
        });
        SetupEventsResponse(http, json);

        // Act
        var (events, _) = await sut.GetEventsAsync("cal@google.com",
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero),
            ct: CancellationToken.None);

        // Assert
        events.Should().ContainSingle(e => e.Title == "My Meeting");
    }

    [Fact]
    public async Task GetEventsAsync_WhenOrganizerAbsent_ReturnsSingleEvent()
    {
        // Arrange
        var (http, tokenStore, sut) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");
        SetupAuthResponse(http);

        var json = JsonSerializer.Serialize(new
        {
            items = new[] {
                new {
                    id = "evt-3", status = "confirmed", summary = "Simple Meeting",
                    start = new { dateTime = "2026-03-01T10:00:00Z" },
                    end   = new { dateTime = "2026-03-01T11:00:00Z" }
                    // no organizer field
                }
            },
            nextSyncToken = "token-3"
        });
        SetupEventsResponse(http, json);

        // Act
        var (events, _) = await sut.GetEventsAsync("cal@google.com",
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero),
            ct: CancellationToken.None);

        // Assert
        events.Should().ContainSingle(e => e.Title == "Simple Meeting");
    }

    // ── FHQ-144: series-master writes must be events.patch, not events.update ──

    [Fact]
    public async Task PatchEventFieldsAsync_IssuesHttpPatch_AndOmitsRecurrenceKey()
    {
        // Arrange
        var (http, tokenStore, sut) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");
        SetupAuthResponse(http);

        string? capturedBody = null;
        HttpMethod? capturedMethod = null;

        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("events/series-master-1")),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedMethod = req.Method;
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { id = "series-master-1" }))
            });

        var master = new CalendarEvent
        {
            GoogleEventId = "series-master-1",
            Title = "Gymnastics",
            Start = new DateTimeOffset(2026, 6, 10, 9, 30, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 6, 10, 10, 30, 0, TimeSpan.Zero)
        };

        // Act
        await sut.PatchEventFieldsAsync("cal-1", master, "hash-1", CancellationToken.None);

        // Assert — events.patch merge semantics: PUT would full-replace and clear the RRULE.
        capturedMethod.Should().Be(HttpMethod.Patch);

        // The recurrence key must be absent entirely (not null, not []), so Google preserves the RRULE.
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.TryGetProperty("recurrence", out _).Should().BeFalse();

        // The edited fields are still sent.
        doc.RootElement.GetProperty("summary").GetString().Should().Be("Gymnastics");
        doc.RootElement.TryGetProperty("start", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("end", out _).Should().BeTrue();
    }

    [Fact]
    public async Task PatchEventFieldsAsync_NullLocation_SendsEmptyStringToClearIt()
    {
        // A null Location must be sent as "" (not omitted), so events.patch merge clears it
        // server-side. Omitting it (WhenWritingNull) would leave a stale location in Google.
        var (http, tokenStore, sut) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");
        SetupAuthResponse(http);

        string? capturedBody = null;
        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("events/evt-loc")),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { id = "evt-loc" }))
            });

        var evt = new CalendarEvent
        {
            GoogleEventId = "evt-loc",
            Title = "No location",
            Location = null,
            Start = new DateTimeOffset(2026, 6, 10, 9, 30, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 6, 10, 10, 30, 0, TimeSpan.Zero)
        };

        await sut.PatchEventFieldsAsync("cal-1", evt, "hash-1", CancellationToken.None);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.TryGetProperty("location", out var loc).Should().BeTrue("a null location must still be sent, as \"\"");
        loc.GetString().Should().Be("");
    }

    [Fact]
    public async Task PatchEventFieldsAsync_OmitsUnmappedGoogleFields()
    {
        // FHQ-145: the merge body must never carry fields FamilyHQ does not model, so Google's
        // existing attendees/colorId/reminders survive a kiosk edit. Guards against a future
        // MapToGoogleEvent change re-arming the full-replace data loss.
        var (http, tokenStore, sut) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");
        SetupAuthResponse(http);

        string? capturedBody = null;
        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("events/evt-unmapped")),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { id = "evt-unmapped" }))
            });

        var evt = new CalendarEvent
        {
            GoogleEventId = "evt-unmapped",
            Title = "Retimed by kiosk",
            Start = new DateTimeOffset(2026, 6, 10, 9, 30, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 6, 10, 10, 30, 0, TimeSpan.Zero)
        };

        await sut.PatchEventFieldsAsync("cal-1", evt, "hash-1", CancellationToken.None);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.TryGetProperty("attendees", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("colorId", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("reminders", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("visibility", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PatchEventFieldsAsync_AllDay_ClearsStaleDateTimeAndTimeZone()
    {
        // FHQ-151: converting a timed event to all-day via events.patch (merge) must send the
        // counterpart dateTime/timeZone as explicit JSON null, or Google merges the new date onto the
        // stale dateTime and rejects it 400 "Invalid start time."
        var (http, tokenStore, sut) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");
        SetupAuthResponse(http);

        string? capturedBody = null;
        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("events/evt-allday")),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { id = "evt-allday" }))
            });

        var evt = new CalendarEvent
        {
            GoogleEventId = "evt-allday",
            Title = "Now all day",
            Start = new DateTimeOffset(2026, 12, 9, 0, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 12, 10, 0, 0, 0, TimeSpan.Zero),
            IsAllDay = true
        };

        await sut.PatchEventFieldsAsync("cal-1", evt, "hash-1", CancellationToken.None);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var start = doc.RootElement.GetProperty("start");
        start.GetProperty("date").GetString().Should().Be("2026-12-09");
        start.TryGetProperty("dateTime", out var sdt).Should().BeTrue("all-day patch must send dateTime to clear a stale timed value");
        sdt.ValueKind.Should().Be(JsonValueKind.Null);
        start.TryGetProperty("timeZone", out var stz).Should().BeTrue();
        stz.ValueKind.Should().Be(JsonValueKind.Null);

        var end = doc.RootElement.GetProperty("end");
        end.GetProperty("date").GetString().Should().Be("2026-12-10");
        end.GetProperty("dateTime").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static void SetupEventsResponse(Mock<HttpMessageHandler> http, string json)
    {
        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("/events")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json)
            });
    }

    private static void SetupAuthResponse(Mock<HttpMessageHandler> http)
    {
        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("auth.test.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                    { access_token = "new-access", expires_in = 3600, token_type = "Bearer" }))
            });
    }

    private static (Mock<HttpMessageHandler> HttpMock, Mock<ITokenStore> TokenMock, GoogleCalendarClient Sut) CreateSut()
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
        var authService = new GoogleAuthService(httpClient, options, new Mock<ILogger<GoogleAuthService>>().Object, new Mock<IIdTokenValidator>().Object);
        var timeZoneServiceMock = new Mock<ITimeZoneService>();
        timeZoneServiceMock
            .Setup(s => s.GetSendZoneAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var sut = new GoogleCalendarClient(
            httpClient, authService,
            tokenStoreMock.Object,
            new Mock<IAccessTokenProvider>().Object,
            options,
            new Mock<ILogger<GoogleCalendarClient>>().Object,
            timeZoneServiceMock.Object);
        return (httpMessageHandlerMock, tokenStoreMock, sut);
    }
}
