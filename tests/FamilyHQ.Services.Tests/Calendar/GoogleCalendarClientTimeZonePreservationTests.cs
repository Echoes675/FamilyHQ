using System.Net;
using System.Text.Json;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Options;
using FamilyHQ.Services.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// FHQ-170 — the outbound zone must be the EVENT'S own, not the family's.
/// <para>
/// The edited occurrence's instant survives either way (<c>ToZonedWallClock</c> converts rather than
/// reinterprets), so the immediate edit always looks correct. What the family's zone silently
/// rewrites is the series' ANCHOR — the zone Google expands future occurrences in — so a series
/// created on a phone in another zone moves by an hour at the next transition where the two zones
/// differ, on every device the calendar is shared with. These tests assert the one observable thing
/// that distinguishes the two: which zone id leaves the process.
/// </para>
/// <para>
/// FHQ-43 introduced sending an explicit zone at all and is preserved here: an event Google gave no
/// zone for still goes out anchored to the family's configured zone.
/// </para>
/// </summary>
public class GoogleCalendarClientTimeZonePreservationTests
{
    private const string FamilyZone = "Europe/Dublin";
    private const string EventZone = "America/New_York";

    private static readonly DateTimeOffset Start = new(2026, 10, 20, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 10, 20, 14, 0, 0, TimeSpan.Zero);

    // ── The write side ────────────────────────────────────────────────────────

    [Fact]
    public async Task PatchEventFieldsAsync_EventCarryingItsOwnZone_SendsThatZoneNotTheFamilys()
    {
        // The ticket's case: a series created on a phone in America/New_York, edited by a family
        // whose display setting is Europe/Dublin. Sending Europe/Dublin re-anchors the series.
        var (http, sut) = CreateSut(FamilyZone);
        var captured = ArrangeWriteCapture(http, "evt-ny");

        var evt = TimedEvent("evt-ny", EventZone);

        await sut.PatchEventFieldsAsync("cal-1", evt, "hash-1", CancellationToken.None);

        StartZoneOf(captured).Should().Be(EventZone);
        EndZoneOf(captured).Should().Be(EventZone);
    }

    [Fact]
    public async Task PatchEventFieldsAsync_EventCarryingItsOwnZone_ConvertsTheWallClockInThatZoneToo()
    {
        // Sending the right timeZone alongside a dateTime converted in the WRONG zone would move the
        // occurrence's instant — a worse failure than the one being fixed. The stubbed conversion
        // echoes the zone it was asked for, so the pair is asserted together.
        var (http, sut) = CreateSut(FamilyZone);
        var captured = ArrangeWriteCapture(http, "evt-ny");

        await sut.PatchEventFieldsAsync("cal-1", TimedEvent("evt-ny", EventZone), "hash-1", CancellationToken.None);

        Element(captured, "start").GetProperty("dateTime").GetString().Should().Contain($"[{EventZone}]");
    }

    [Fact]
    public async Task PatchEventFieldsAsync_EventWithNoZoneOfItsOwn_StillSendsTheFamilysZone()
    {
        // FHQ-43 preserved: the family's setting is the FALLBACK for data Google did not supply.
        var (http, sut) = CreateSut(FamilyZone);
        var captured = ArrangeWriteCapture(http, "evt-none");

        await sut.PatchEventFieldsAsync("cal-1", TimedEvent("evt-none", ianaTimeZone: null), "hash-1", CancellationToken.None);

        StartZoneOf(captured).Should().Be(FamilyZone);
    }

    [Fact]
    public async Task CreateEventAsync_NewEvent_SendsTheFamilysZone()
    {
        // A brand-new event has no prior zone to preserve, so the family's zone IS the right answer
        // on create. FHQ-170 changes the update path only.
        var (http, sut) = CreateSut(FamilyZone);
        var captured = ArrangeCreateCapture(http);

        await sut.CreateEventAsync("cal-1", TimedEvent(googleEventId: string.Empty, ianaTimeZone: null), "hash-1", CancellationToken.None);

        StartZoneOf(captured).Should().Be(FamilyZone);
    }

    [Fact]
    public async Task CreateRecurringEventAsync_SeriesCarryingItsOwnZone_SendsThatZone()
    {
        // The forward half of a "this and following" split is created through this path carrying the
        // original series' zone, so the continuation stays anchored where Google anchored it.
        var (http, sut) = CreateSut(FamilyZone);
        var captured = ArrangeCreateCapture(http);

        await sut.CreateRecurringEventAsync(
            "cal-1", TimedEvent(string.Empty, EventZone), "hash-1", "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=2", CancellationToken.None);

        StartZoneOf(captured).Should().Be(EventZone);
    }

    [Fact]
    public async Task PatchEventFieldsAsync_AllDayEventWithAStoredZone_SendsNoTimeZoneAtAll()
    {
        // An all-day event is date-anchored and carries no start.timeZone by design, so the all-day
        // branch has nothing to preserve and is untouched by this change.
        var (http, sut) = CreateSut(FamilyZone);
        var captured = ArrangeWriteCapture(http, "evt-allday");

        var evt = new CalendarEvent
        {
            GoogleEventId = "evt-allday",
            Title = "Bin day",
            Start = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero),
            IsAllDay = true,
            IanaTimeZone = EventZone
        };

        await sut.PatchEventFieldsAsync("cal-1", evt, "hash-1", CancellationToken.None);

        var start = Element(captured, "start");
        start.GetProperty("date").GetString().Should().Be("2026-10-20");
        start.GetProperty("timeZone").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task PatchEventFieldsAsync_UnrecognisedStoredZone_FallsBackToTheFamilysZoneAndWarns()
    {
        // A stored id the tz database does not know cannot be converted and would be rejected by
        // Google. Degrade rather than fail the user's write — and say so, because an unrecognised id
        // means the stored value is stale or corrupt.
        var (http, sut, logger) = CreateSutWithLogger(FamilyZone);
        var captured = ArrangeWriteCapture(http, "evt-bad");

        await sut.PatchEventFieldsAsync("cal-1", TimedEvent("evt-bad", "Not/AZone"), "hash-1", CancellationToken.None);

        StartZoneOf(captured).Should().Be(FamilyZone);
        logger.Records.Should().Contain(r =>
            r.Level == LogLevel.Warning && r.Message.Contains("unrecognised IANA time zone"));
    }

    [Fact]
    public async Task PatchEventFieldsAsync_EventZoneWithNoFamilyZoneConfigured_StillSendsTheEventsZone()
    {
        // The event's own zone does not depend on the family having one. With no display setting at
        // all the pre-fix code fell all the way through to timeZone=UTC, re-anchoring the series.
        var (http, sut) = CreateSut(familyZone: null);
        var captured = ArrangeWriteCapture(http, "evt-ny");

        await sut.PatchEventFieldsAsync("cal-1", TimedEvent("evt-ny", EventZone), "hash-1", CancellationToken.None);

        StartZoneOf(captured).Should().Be(EventZone);
    }

    // ── The read side: the zone has to arrive before it can be sent back ──────

    [Fact]
    public async Task GetEventsAsync_MapsStartTimeZoneOntoEveryEvent()
    {
        // The list response is the lazy backfill's main feeder (FHQ-164 Decision 4): Google reports
        // start.timeZone on expanded instances too, so an ordinary window sync populates the column
        // for free — no extra call, no bulk migration job.
        var (http, sut) = CreateSut(FamilyZone);
        var json = JsonSerializer.Serialize(new
        {
            items = new object[]
            {
                new
                {
                    id = "evt-1", status = "confirmed", summary = "Football training",
                    start = new { dateTime = "2026-10-20T13:00:00Z", timeZone = EventZone },
                    end = new { dateTime = "2026-10-20T14:00:00Z", timeZone = EventZone },
                    recurringEventId = "series-1"
                },
                new
                {
                    id = "evt-2", status = "confirmed", summary = "Bin day",
                    start = new { date = "2026-10-21" },
                    end = new { date = "2026-10-22" }
                }
            },
            nextSyncToken = "token-1"
        });

        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains("/events?")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent(json) });

        var (events, _) = await sut.GetEventsAsync("cal-1", Start.AddDays(-1), Start.AddDays(1), ct: CancellationToken.None);

        var byId = events.ToDictionary(e => e.GoogleEventId);
        byId["evt-1"].IanaTimeZone.Should().Be(EventZone);
        byId["evt-2"].IanaTimeZone.Should().BeNull("an all-day event carries no zone by design");
    }

    [Fact]
    public async Task GetEventAsync_CarriesStartTimeZone()
    {
        // Ladder rung 3: a recurring instance carries the zone its series is anchored to, so any
        // surviving instance answers the question when the master itself cannot be fetched.
        var (http, sut) = CreateSut(FamilyZone);
        var json = JsonSerializer.Serialize(new
        {
            id = "inst-1",
            status = "confirmed",
            start = new { dateTime = "2026-10-20T13:00:00Z", timeZone = EventZone },
            end = new { dateTime = "2026-10-20T14:00:00Z", timeZone = EventZone },
            extendedProperties = new { @private = new Dictionary<string, string> { ["content-hash"] = "hash-1" } }
        });

        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("events/inst-1")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent(json) });

        var detail = await sut.GetEventAsync("cal-1", "inst-1", CancellationToken.None);

        detail!.IanaTimeZone.Should().Be(EventZone);
    }

    [Fact]
    public async Task GetCalendarsAsync_CarriesTheCalendarsDefaultTimeZone()
    {
        // Ladder rung 4: the zone Google itself applies to an event on this calendar with none of its
        // own. Persisting it with the calendar keeps that rung free of an extra API call at edit time.
        var (http, sut) = CreateSut(FamilyZone);
        var json = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new { id = "cal-1", summary = "Family", backgroundColor = "#fff", timeZone = EventZone }
            }
        });

        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("calendarList")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent(json) });

        var calendars = await sut.GetCalendarsAsync(CancellationToken.None);

        calendars.Should().ContainSingle().Which.IanaTimeZone.Should().Be(EventZone);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CalendarEvent TimedEvent(string googleEventId, string? ianaTimeZone) => new()
    {
        GoogleEventId = googleEventId,
        Title = "Football training",
        Start = Start,
        End = End,
        IsAllDay = false,
        IanaTimeZone = ianaTimeZone
    };

    private static JsonDocument Parse(StrongBoxString captured)
    {
        captured.Value.Should().NotBeNull();
        return JsonDocument.Parse(captured.Value!);
    }

    private static JsonElement Element(StrongBoxString captured, string name)
    {
        using var doc = Parse(captured);
        return doc.RootElement.GetProperty(name).Clone();
    }

    private static string? StartZoneOf(StrongBoxString captured) =>
        Element(captured, "start").GetProperty("timeZone").GetString();

    private static string? EndZoneOf(StrongBoxString captured) =>
        Element(captured, "end").GetProperty("timeZone").GetString();

    private sealed class StrongBoxString
    {
        public string? Value { get; set; }
    }

    private static StrongBoxString ArrangeWriteCapture(Mock<HttpMessageHandler> http, string googleEventId) =>
        ArrangeCapture(http, req => req.RequestUri!.ToString().Contains($"events/{googleEventId}"), googleEventId);

    private static StrongBoxString ArrangeCreateCapture(Mock<HttpMessageHandler> http) =>
        ArrangeCapture(http, req => req.Method == HttpMethod.Post && req.RequestUri!.ToString().EndsWith("/events"), "created-1");

    private static StrongBoxString ArrangeCapture(
        Mock<HttpMessageHandler> http, Func<HttpRequestMessage, bool> match, string responseId)
    {
        var captured = new StrongBoxString();
        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => match(req)),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
                captured.Value = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { id = responseId }))
            });
        return captured;
    }

    private static (Mock<HttpMessageHandler> Http, GoogleCalendarClient Sut) CreateSut(string? familyZone)
    {
        var (http, sut, _) = CreateSutWithLogger(familyZone);
        return (http, sut);
    }

    private static (Mock<HttpMessageHandler> Http, GoogleCalendarClient Sut, RecordingLogger<GoogleCalendarClient> Logger) CreateSutWithLogger(
        string? familyZone)
    {
        var http = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(http.Object);
        var options = Microsoft.Extensions.Options.Options.Create(new GoogleCalendarOptions
        {
            CalendarApiBaseUrl = "https://calendar.test.com",
            ClientId = "test-client",
            ClientSecret = "test-secret",
            AuthBaseUrl = "https://auth.test.com"
        });

        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("auth.test.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                    { access_token = "new-access", expires_in = 3600, token_type = "Bearer" }))
            });

        var tokenStore = new Mock<ITokenStore>();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");

        var authService = new GoogleAuthService(
            httpClient, options, new Mock<ILogger<GoogleAuthService>>().Object,
            new Mock<IIdTokenValidator>().Object, new Mock<ITokenStore>().Object);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns("test-user-id");

        var timeZoneService = new Mock<ITimeZoneService>();
        timeZoneService.Setup(s => s.GetSendZoneAsync(It.IsAny<CancellationToken>())).ReturnsAsync(familyZone);
        // The tz database itself is not the subject here — TimeZoneServiceTests owns the real
        // NodaTime conversion. What matters is WHICH zone the client asks it about, so the stub
        // recognises the two real ids in play and echoes the id it was handed into the wall clock.
        timeZoneService.Setup(s => s.IsValidZone(It.IsAny<string>()))
            .Returns((string zone) => zone is FamilyZone or EventZone);
        timeZoneService.Setup(s => s.ToZonedWallClock(It.IsAny<DateTimeOffset>(), It.IsAny<string>()))
            .Returns((DateTimeOffset instant, string zone) => $"{instant.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss}[{zone}]");

        var logger = new RecordingLogger<GoogleCalendarClient>();
        var sut = new GoogleCalendarClient(
            httpClient, authService, tokenStore.Object, currentUser.Object,
            new AccessTokenCache(TimeProvider.System), options, logger,
            timeZoneService.Object, TestPiiRedactor.Instance);

        return (http, sut, logger);
    }
}
