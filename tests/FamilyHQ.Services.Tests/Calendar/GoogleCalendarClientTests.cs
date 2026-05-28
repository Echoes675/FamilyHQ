using System.Net;
using System.Reflection;
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
    public async Task GetEventsAsync_RecurringInstance_MapsRecurringEventIdAndOriginalStartTime()
    {
        // Arrange — a recurring-series instance carries recurringEventId and (when it is a
        // moved/modified exception) originalStartTime. Both must flow onto CalendarEvent so the
        // sync service can link the instance to its series and detect exceptions. RecurrenceRule
        // is filled later by the two-pass master fetch, so it is null here.
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
                    nextSyncToken = "tok",
                    items = new object[]
                    {
                        new
                        {
                            id = "series_inst_20260302T100000Z",
                            status = "confirmed",
                            summary = "Weekly Standup",
                            start = new { dateTime = "2026-03-02T11:00:00Z" },
                            end = new { dateTime = "2026-03-02T11:30:00Z" },
                            recurringEventId = "series-master-id",
                            originalStartTime = new { dateTime = "2026-03-02T10:00:00Z" }
                        }
                    }
                }))
            });

        var now = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        // Act
        var result = await systemUnderTest.GetEventsAsync("cal1", now.AddDays(-1), now.AddDays(7));

        // Assert
        var instance = result.Events.Single();
        instance.GoogleRecurringEventId.Should().Be("series-master-id");
        instance.OriginalStartTime.Should().Be(new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero));
        instance.RecurrenceRule.Should().BeNull("the RRULE is resolved later via the two-pass master fetch");
    }

    [Fact]
    public async Task GetSeriesMasterAsync_WhenMasterHasRecurrence_ReturnsRRuleLine()
    {
        // Arrange — the series master event carries a recurrence array; GetSeriesMasterAsync
        // returns the RRULE: line so the sync service can stamp it on every instance.
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
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains("events/series-master-id")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id = "series-master-id",
                    recurrence = new[] { "RRULE:FREQ=WEEKLY;BYDAY=MO" }
                }))
            });

        // Act
        var rrule = await systemUnderTest.GetSeriesMasterAsync("cal1", "series-master-id", CancellationToken.None);

        // Assert
        rrule.Should().Be("RRULE:FREQ=WEEKLY;BYDAY=MO");
    }

    [Fact]
    public async Task GetSeriesMasterAsync_WhenMasterHasMultipleRecurrenceLines_ReturnsRRuleLineOnly()
    {
        // Arrange — Google may return EXDATE/RDATE lines alongside RRULE; only the RRULE line is wanted.
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
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("events/series-master-id")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id = "series-master-id",
                    recurrence = new[] { "EXDATE;TZID=UTC:20260309T100000", "RRULE:FREQ=WEEKLY;BYDAY=MO" }
                }))
            });

        // Act
        var rrule = await systemUnderTest.GetSeriesMasterAsync("cal1", "series-master-id", CancellationToken.None);

        // Assert
        rrule.Should().Be("RRULE:FREQ=WEEKLY;BYDAY=MO");
    }

    [Fact]
    public async Task GetSeriesMasterAsync_WhenNoRecurrenceArray_ReturnsNull()
    {
        // Arrange — a master with no recurrence array (e.g. a single event mistakenly queried)
        // yields null rather than throwing, so the sync service degrades gracefully.
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
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("events/series-master-id")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { id = "series-master-id" }))
            });

        // Act
        var rrule = await systemUnderTest.GetSeriesMasterAsync("cal1", "series-master-id", CancellationToken.None);

        // Assert
        rrule.Should().BeNull();
    }

    [Fact]
    public async Task GetSeriesMasterAsync_WhenMasterNotFound_ReturnsNull()
    {
        // Arrange — a 404 (series deleted between pass 1 and pass 2) must not throw.
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
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("events/series-master-id")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.NotFound });

        // Act
        var rrule = await systemUnderTest.GetSeriesMasterAsync("cal1", "series-master-id", CancellationToken.None);

        // Assert
        rrule.Should().BeNull();
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
    public async Task GetCalendarsAsync_WhenForbidden_ThrowsGoogleReauthRequiredException()
    {
        // Arrange — Google 403 (e.g. scope missing post-consent, API disabled) must surface as
        // a needs-reauth signal so the user is prompted to reconnect rather than swallowing a 500.
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

        var forbiddenBody = JsonSerializer.Serialize(new { error = new { code = 403, message = "Insufficient Permission" } });
        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("users/me/calendarList")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Forbidden,
                Content = new StringContent(forbiddenBody)
            });

        // Act & Assert
        var ex = await systemUnderTest.Invoking(s => s.GetCalendarsAsync())
            .Should().ThrowAsync<GoogleReauthRequiredException>();
        ex.Which.FailureSource.Should().Be(GoogleAuthFailureSource.CalendarApi);
        ex.Which.ResponseBody.Should().Contain("Insufficient Permission");
    }

    [Fact]
    public async Task GetEventsAsync_WhenUnauthorized_ThrowsGoogleReauthRequiredException()
    {
        // Arrange — 401 from the events endpoint is also a reauth signal.
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
                StatusCode = HttpStatusCode.Unauthorized,
                Content = new StringContent("{\"error\":\"invalid_credentials\"}")
            });

        // Act & Assert
        var ex = await systemUnderTest.Invoking(s => s.GetEventsAsync("cal1", null, null))
            .Should().ThrowAsync<GoogleReauthRequiredException>();
        ex.Which.FailureSource.Should().Be(GoogleAuthFailureSource.CalendarApi);
    }

    [Fact]
    public async Task GetCalendarsAsync_When500_ThrowsGoogleApiExceptionWithBody()
    {
        // Arrange — non-auth upstream errors surface as GoogleApiException with body captured.
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
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("users/me/calendarList")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("backend boom")
            });

        // Act & Assert
        var ex = await systemUnderTest.Invoking(s => s.GetCalendarsAsync())
            .Should().ThrowAsync<GoogleApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        ex.Which.ResponseBody.Should().Contain("backend boom");
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

    [Fact]
    public async Task GetCalendarsAsync_AttachesBearerToken_PerRequest_WithoutMutatingDefaultHeaders()
    {
        // FHQ-27: mutating HttpClient.DefaultRequestHeaders.Authorization made the
        // Authorization header process-shared state on the typed client, opening a
        // cross-request leak window between concurrent users. Per-request headers
        // on HttpRequestMessage keep auth scoped to a single call.
        HttpRequestMessage? capturedCalendarRequest = null;
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
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("users/me/calendarList")),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedCalendarRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { items = Array.Empty<object>() }))
            });

        // Act
        await systemUnderTest.GetCalendarsAsync();

        // Assert — the calendar request must carry Authorization on its own headers.
        capturedCalendarRequest.Should().NotBeNull();
        capturedCalendarRequest!.Headers.Authorization.Should().NotBeNull(
            "FHQ-27: Authorization header must be attached per-request on HttpRequestMessage");
        capturedCalendarRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedCalendarRequest.Headers.Authorization.Parameter.Should().Be("new-access");

        // The shared HttpClient instance must not carry the Authorization header
        // between requests.
        var sharedHttpClient = systemUnderTest.GetType()
            .GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(systemUnderTest);
        sharedHttpClient.Should().BeOfType<HttpClient>()
            .Which.DefaultRequestHeaders.Authorization.Should().BeNull(
                "FHQ-27: Authorization header must be attached per-request on HttpRequestMessage, not mutated on the shared HttpClient");
    }

    [Fact]
    public async Task GetEventsAsync_WhenCalled_IncludesExtendedPropertiesInFieldsAllowlist()
    {
        // Arrange — capture the outgoing request URL to verify the fields= projection is sent.
        string? capturedUrl = null;
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
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUrl = req.RequestUri!.ToString())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { nextSyncToken = "tok", items = Array.Empty<object>() }))
            });

        // Act
        await systemUnderTest.GetEventsAsync(
            googleCalendarId: "primary",
            syncWindowStart: DateTimeOffset.UtcNow.AddDays(-1),
            syncWindowEnd: DateTimeOffset.UtcNow.AddDays(30),
            syncToken: null);

        // Assert
        capturedUrl.Should().NotBeNull();
        var unescapedUrl = Uri.UnescapeDataString(capturedUrl!);
        unescapedUrl.Should().Contain("nextPageToken,nextSyncToken,items(id,iCalUID,summary,description,location,start,end,attendees,organizer,extendedProperties,recurringEventId,originalStartTime,status)");
        capturedUrl.Should().Contain("singleEvents=true");
        // Verify critical fields that would silently break sync if missing:
        capturedUrl.Should().Contain("id");
        capturedUrl.Should().Contain("start");
        capturedUrl.Should().Contain("end");
        capturedUrl.Should().Contain("iCalUID");
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
