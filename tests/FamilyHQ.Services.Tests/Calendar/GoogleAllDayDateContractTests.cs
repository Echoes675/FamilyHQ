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
/// FHQ-174 — an all-day <c>date</c> Google sends must come back out as the same <c>date</c>.
/// <para>
/// The defect these pin was a substitution: <c>DateTimeOffset.Parse("2026-06-15")</c> stamped the
/// HOST machine's offset, so on a host at UTC+1 the event became <c>2026-06-14T23:00:00Z</c>. The
/// <c>Start</c>/<c>End</c> EF converter (<c>v =&gt; v.ToUniversalTime()</c>) preserved that instant,
/// and <c>MapToGoogleEvent</c>'s all-day branch formatted <c>"yyyy-MM-dd"</c> from it — so a one-day
/// event synced on a UTC+1 host and later written back reached the phone starting a day early, with
/// the exclusive-end normalisation adding a further day on top.
/// </para>
///
/// <para><b>What these tests can and cannot do.</b> They DOCUMENT the contract; they are not a
/// regression net. CI runs at a zero host offset, where the buggy and the fixed code produce
/// byte-identical values — every assertion below passes on the defect. The mechanisms that actually
/// catch a reintroduction are the <c>BannedApiAnalyzers</c> rule in
/// <c>build/BannedSymbols.txt</c> (which fails the build on the parse overloads that take no
/// <c>DateTimeStyles</c>) and <c>DateOnlyParseGuardTests</c> (which fails on any
/// <c>DateTime</c>/<c>DateTimeOffset</c> parse that does not pass <c>AssumeUniversal</c>). Read a
/// green run here as "the
/// contract is written down and the code still satisfies it", not as "the host-offset bug cannot
/// come back".</para>
/// </summary>
public class GoogleAllDayDateContractTests
{
    private const string InboundStartDate = "2026-06-15";
    private const string InboundEndDate = "2026-06-16";

    // ── Inbound: a date resolves to midnight UTC, not to the host's midnight ──

    [Fact]
    public async Task GetEventsAsync_AllDayEvent_ResolvesStartAndEndToMidnightUtc()
    {
        var (http, sut, _) = CreateSut();
        ArrangeList(http, AllDayItem("evt-allday", InboundStartDate, InboundEndDate));

        var (events, _) = await sut.GetEventsAsync("cal-1", Window.Start, Window.End, ct: CancellationToken.None);

        var evt = events.Should().ContainSingle().Subject;
        evt.IsAllDay.Should().BeTrue();
        evt.Start.Offset.Should().Be(TimeSpan.Zero);
        evt.Start.UtcDateTime.Should().Be(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        evt.End.Offset.Should().Be(TimeSpan.Zero);
        evt.End.UtcDateTime.Should().Be(new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetEventsAsync_AllDayExceptionInstance_ResolvesOriginalStartTimeToMidnightUtc()
    {
        // originalStartTime identifies WHICH occurrence an exception replaces. A host-shifted value
        // here does not move an event, it fails to match the slot at all.
        var (http, sut, _) = CreateSut();
        var item = AllDayItem("evt-exception", InboundStartDate, InboundEndDate);
        item["recurringEventId"] = "series-1";
        item["originalStartTime"] = new { date = InboundStartDate };
        ArrangeList(http, item);

        var (events, _) = await sut.GetEventsAsync("cal-1", Window.Start, Window.End, ct: CancellationToken.None);

        var originalStart = events.Should().ContainSingle().Subject.OriginalStartTime;
        originalStart!.Value.Offset.Should().Be(TimeSpan.Zero);
        originalStart.Value.UtcDateTime.Should().Be(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetSeriesMasterAsync_AllDayMaster_ResolvesTheSeriesOriginToMidnightUtc()
    {
        // FHQ-172 made this start authoritative for strictly more masters, and the AllInSeries path
        // writes it back to Google as the series' origin — a host-dependent anchor is a
        // host-dependent write.
        var (http, sut, _) = CreateSut();
        var json = JsonSerializer.Serialize(new
        {
            id = "series-1",
            status = "confirmed",
            start = new { date = InboundStartDate },
            end = new { date = InboundEndDate },
            recurrence = new[] { "RRULE:FREQ=WEEKLY;BYDAY=MO" }
        });
        ArrangeGet(http, "events/series-1", json);

        var master = await sut.GetSeriesMasterAsync("cal-1", "series-1", CancellationToken.None);

        master!.Start.Offset.Should().Be(TimeSpan.Zero);
        master.Start.UtcDateTime.Should().Be(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    // ── A malformed `date`: skipped, not thrown ───────────────────────────────
    //
    // Deliberate, and the opposite of what the first cut did. Throwing out of GetEventsAsync takes
    // the whole PAGE — up to 250 events — and, because the retry re-fetches the same page, it takes
    // that calendar's sync every time thereafter: one unreadable item would stop a family's calendar
    // updating indefinitely. The loop's existing contract for an item it cannot use is `continue`
    // (see the start/end null guard), so this is the same answer for the same class of problem.
    // Fail-fast still applies where the blast radius is one user action: GoogleAllDayDate.Parse
    // throws, and the kiosk and the Simulator both use it.

    [Fact]
    public async Task GetEventsAsync_AllDayDateInAnUnexpectedShape_SkipsThatEventAndKeepsTheRest()
    {
        var (http, sut, _) = CreateSut();
        ArrangeList(
            http,
            AllDayItem("evt-bad", "15/06/2026", InboundEndDate),
            AllDayItem("evt-good", InboundStartDate, InboundEndDate));

        var (events, _) = await sut.GetEventsAsync("cal-1", Window.Start, Window.End, ct: CancellationToken.None);

        events.Should().ContainSingle().Which.GoogleEventId.Should().Be("evt-good");
    }

    [Fact]
    public async Task GetEventsAsync_AllDayDateInAnUnexpectedShape_WarnsNamingTheEventButNotTheValue()
    {
        // A skip that says nothing is indistinguishable from an event that was never there. The
        // Google event id is FamilyHQ's own correlation handle; the date VALUE is calendar content
        // and stays out of Seq (the logging standard's redaction rule).
        var (http, sut, logger) = CreateSut();
        ArrangeList(http, AllDayItem("evt-bad", "15/06/2026", InboundEndDate));

        await sut.GetEventsAsync("cal-1", Window.Start, Window.End, ct: CancellationToken.None);

        var warning = logger.Records.Should()
            .ContainSingle(r => r.Level == LogLevel.Warning).Subject.Message;
        warning.Should().Contain("evt-bad");
        warning.Should().NotContain("15/06/2026");
    }

    [Fact]
    public async Task GetEventsAsync_AllDayExceptionWithAnUnreadableOriginalStartTime_SkipsTheEvent()
    {
        // originalStartTime is what identifies the occurrence an exception replaces. Persisting the
        // exception without it would silently detach it from its slot, which is worse than not
        // having it.
        var (http, sut, _) = CreateSut();
        var item = AllDayItem("evt-exception", InboundStartDate, InboundEndDate);
        item["recurringEventId"] = "series-1";
        item["originalStartTime"] = new { date = "15/06/2026" };
        ArrangeList(http, item);

        var (events, _) = await sut.GetEventsAsync("cal-1", Window.Start, Window.End, ct: CancellationToken.None);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSeriesMasterAsync_MasterWithAnUnreadableAllDayDate_ReturnsNull()
    {
        // A master with no resolvable start is no anchor at all — the same answer FHQ-172 settled on
        // for a master that has none.
        var (http, sut, _) = CreateSut();
        var json = JsonSerializer.Serialize(new
        {
            id = "series-1",
            status = "confirmed",
            start = new { date = "15/06/2026" },
            end = new { date = InboundEndDate },
            recurrence = new[] { "RRULE:FREQ=WEEKLY;BYDAY=MO" }
        });
        ArrangeGet(http, "events/series-1", json);

        (await sut.GetSeriesMasterAsync("cal-1", "series-1", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetEventsAsync_TimedEvent_KeepsTheExactInstantGoogleSent()
    {
        // The counterpart pin for GoogleApiTypes.GoogleApiEventDateTime.DateTime, which is left as a
        // DateTimeOffset bound by System.Text.Json. STJ would stamp the host's offset on an
        // offset-LESS value; this asserts the offset-carrying value Google actually sends survives
        // untouched, so the binding stays justified rather than merely unexamined.
        var (http, sut, _) = CreateSut();
        ArrangeList(http, new Dictionary<string, object?>
        {
            ["id"] = "evt-timed",
            ["status"] = "confirmed",
            ["summary"] = "Football training",
            ["start"] = new { dateTime = "2026-06-15T09:30:00+01:00", timeZone = "Europe/Dublin" },
            ["end"] = new { dateTime = "2026-06-15T10:30:00+01:00", timeZone = "Europe/Dublin" }
        });

        var (events, _) = await sut.GetEventsAsync("cal-1", Window.Start, Window.End, ct: CancellationToken.None);

        var evt = events.Should().ContainSingle().Subject;
        evt.IsAllDay.Should().BeFalse();
        evt.Start.UtcDateTime.Should().Be(new DateTime(2026, 6, 15, 8, 30, 0, DateTimeKind.Utc));
    }

    // ── The round trip: in through the sync, through persistence, back out ────

    [Fact]
    public async Task AllDayEvent_FromGoogleThroughPersistenceAndBackToGoogle_SendsTheSameDates()
    {
        // THE assertion of this ticket. It is not enough that the inbound instant is defensible: it
        // has to be the one that survives the Start/End EF converter and formats back to the string
        // Google sent. Midnight UTC does; a zone-anchored value does not, because the converter
        // reduces it to an instant and the outbound "yyyy-MM-dd" then names the previous day for any
        // positive offset. Run at the HttpMessageHandler seam rather than in E2E because the
        // Simulator does not model Google's write semantics (see the project note on FHQ-93).
        var (http, sut, _) = CreateSut();
        ArrangeList(http, AllDayItem("evt-allday", InboundStartDate, InboundEndDate));

        var (events, _) = await sut.GetEventsAsync("cal-1", Window.Start, Window.End, ct: CancellationToken.None);
        var synced = events.Single();

        // Exactly what CalendarEventConfiguration applies on the way to the database:
        //     builder.Property(e => e.Start).HasConversion(v => v.ToUniversalTime(), v => v);
        var persisted = new CalendarEvent
        {
            GoogleEventId = synced.GoogleEventId,
            Title = synced.Title,
            Start = synced.Start.ToUniversalTime(),
            End = synced.End.ToUniversalTime(),
            IsAllDay = synced.IsAllDay,
            IanaTimeZone = synced.IanaTimeZone
        };

        var captured = ArrangeWriteCapture(http, "evt-allday");
        await sut.PatchEventFieldsAsync("cal-1", persisted, "hash-1", CancellationToken.None);

        using var doc = JsonDocument.Parse(captured.Value!);
        doc.RootElement.GetProperty("start").GetProperty("date").GetString().Should().Be(InboundStartDate);
        doc.RootElement.GetProperty("end").GetProperty("date").GetString().Should().Be(InboundEndDate);
    }

    [Fact]
    public async Task AllDayEvent_FromGoogleThroughPersistenceAndBackToGoogle_SendsNoDateTime()
    {
        // An all-day event that goes back out as a dateTime is a different kind of event on the
        // phone, so the round trip has to preserve the SHAPE as well as the values.
        var (http, sut, _) = CreateSut();
        ArrangeList(http, AllDayItem("evt-allday", InboundStartDate, InboundEndDate));

        var (events, _) = await sut.GetEventsAsync("cal-1", Window.Start, Window.End, ct: CancellationToken.None);
        var synced = events.Single();

        var persisted = new CalendarEvent
        {
            GoogleEventId = synced.GoogleEventId,
            Title = synced.Title,
            Start = synced.Start.ToUniversalTime(),
            End = synced.End.ToUniversalTime(),
            IsAllDay = true,
            IanaTimeZone = null
        };

        var captured = ArrangeWriteCapture(http, "evt-allday");
        await sut.PatchEventFieldsAsync("cal-1", persisted, "hash-1", CancellationToken.None);

        using var doc = JsonDocument.Parse(captured.Value!);
        doc.RootElement.GetProperty("start").TryGetProperty("dateTime", out var startDateTime).Should().BeTrue();
        startDateTime.ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (DateTimeOffset Start, DateTimeOffset End) Window =>
        (new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

    private static Dictionary<string, object?> AllDayItem(string id, string startDate, string endDate) => new()
    {
        ["id"] = id,
        ["status"] = "confirmed",
        ["summary"] = "School holiday",
        ["start"] = new { date = startDate },
        ["end"] = new { date = endDate }
    };

    private static void ArrangeList(Mock<HttpMessageHandler> http, params Dictionary<string, object?>[] items)
    {
        var json = JsonSerializer.Serialize(new { items, nextSyncToken = "token-1" });
        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains("/events?")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json)
            });
    }

    private static void ArrangeGet(Mock<HttpMessageHandler> http, string pathFragment, string json) =>
        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains(pathFragment)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json)
            });

    private sealed class CapturedBody
    {
        public string? Value { get; set; }
    }

    private static CapturedBody ArrangeWriteCapture(Mock<HttpMessageHandler> http, string googleEventId)
    {
        var captured = new CapturedBody();
        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method != HttpMethod.Get && req.RequestUri!.ToString().Contains($"events/{googleEventId}")),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
                captured.Value = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { id = googleEventId }))
            });
        return captured;
    }

    private static (
        Mock<HttpMessageHandler> Http,
        GoogleCalendarClient Sut,
        RecordingLogger<GoogleCalendarClient> Logger) CreateSut()
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

        var logger = new RecordingLogger<GoogleCalendarClient>();

        var tokenStore = new Mock<ITokenStore>();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");

        var authService = new GoogleAuthService(
            httpClient, options, new Mock<ILogger<GoogleAuthService>>().Object,
            new Mock<IIdTokenValidator>().Object, new Mock<ITokenStore>().Object);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns("test-user-id");

        // An all-day write carries no timeZone at all, so nothing on these paths consults the zone
        // service; it is stubbed only to satisfy the constructor.
        var timeZoneService = new Mock<ITimeZoneService>();
        timeZoneService.Setup(s => s.GetSendZoneAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var sut = new GoogleCalendarClient(
            httpClient,
            authService,
            tokenStore.Object,
            currentUser.Object,
            new AccessTokenCache(TimeProvider.System),
            options,
            logger,
            timeZoneService.Object,
            TestPiiRedactor.Instance);

        return (http, sut, logger);
    }
}
