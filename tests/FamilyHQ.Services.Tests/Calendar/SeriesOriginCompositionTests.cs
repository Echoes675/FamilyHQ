using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Exceptions;
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

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// FHQ-172, the scenario the ticket is named after, tested end to end across the two halves that
/// have to agree for it to be fixed.
/// <para>
/// Half one lives in <see cref="GoogleCalendarClient.GetSeriesMasterAsync"/>: an RDATE-only master
/// (the shape an ICS/CalDAV import produces) has a perfectly good DTSTART, and Change 1 stopped
/// discarding the whole record just because the <c>recurrence</c> array carries no <c>RRULE:</c>
/// line. Half two lives in <c>CalendarEventService.PatchSeriesMasterAsync</c>: a master that
/// resolves is patched at <c>masterStart + shift</c>, preserving the series' origin.
/// </para>
/// <para>
/// <b>Why this is not two mocked tests.</b> Each half already has its own; neither can fail when the
/// other regresses, because the service's view of the client is a mock. Composing them is the only
/// way to pin the behaviour the ticket describes — a real series whose master Google returns without
/// an RRULE gets its origin shifted, not relocated. Reverting Change 1 alone (restoring the
/// <c>if (rrule is null) return null;</c> bail in the client) turns this test red: the service sees
/// an unresolved anchor, classifies the requested one-hour move as a timing change, and refuses with
/// <see cref="SeriesOriginUnresolvedException"/> instead of writing.
/// </para>
/// <para>
/// It is still a pure unit test. The only seam that is real is the pair
/// <c>GoogleCalendarClient</c> ↔ <c>CalendarEventService</c>; the network is a mocked
/// <c>HttpMessageHandler</c> and the repository is a mock. Nothing touches a database, a clock or a
/// socket. That seam is deliberately the one chosen: it is where Google's write semantics live, and
/// the Simulator does not model them (see <c>.agent/docs/intermittent-issues.md</c>).
/// </para>
/// </summary>
public class SeriesOriginCompositionTests
{
    private const string SeriesId = "series-master-id";
    private const string GoogleCalId = "calendar-under-test@example.test";
    private const string UserId = "u-composition";

    private static readonly Guid CalendarId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    // The master predates the sync window — the whole reason the local-row proxy is dangerous.
    private static readonly DateTimeOffset MasterStart = new(2026, 1, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowStart = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    // The earliest row the sync window reaches, and the occurrence the user edits.
    private static readonly DateTimeOffset EarliestLocalRowStart = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EditedInstanceStart = new(2026, 3, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AllInSeriesTimeChange_OnAnRdateOnlyMaster_PatchesTheMastersOwnOriginShiftedByTheEdit()
    {
        // Arrange — the master Google returns has a DTSTART and RDATE/EXDATE lines but no RRULE.
        var (http, sut) = CreateComposedSut();
        var master = ArrangeRdateOnlyMaster(http);
        var patchBody = ArrangePatchCapture(http);
        ArrangeEmptyReconcileWindow(http);

        // Act — move the edited occurrence an hour earlier, for the whole series.
        var newStart = EditedInstanceStart.AddHours(-1);
        await sut.UpdateRecurringAsync(
            EventId,
            new UpdateEventRequest("Swimming", newStart, newStart.AddHours(1), false, "The pool", "Body"),
            RecurrenceScope.AllInSeries);

        // Assert — the master was fetched, and the write carries ITS origin shifted by the edit.
        master.Value.Should().Be(1, "the series master is what supplies the origin");

        patchBody.Value.Should().NotBeNull("a resolvable master takes the ordinary patch, which sends start and end");
        using var doc = JsonDocument.Parse(patchBody.Value!);
        var start = doc.RootElement.GetProperty("start").GetProperty("dateTime").GetString();
        var end = doc.RootElement.GetProperty("end").GetProperty("dateTime").GetString();

        start.Should().StartWith("2026-01-04T08:00:00", "the master's own DTSTART moved by the requested −1h");
        end.Should().StartWith("2026-01-04T09:00:00", "the master keeps its one-hour duration");

        // The failure this ticket is about, stated as its own assertion so a regression names itself.
        start.Should().NotStartWith("2026-03-01",
            "anchoring on the earliest locally-synced row relocates the series' origin into the sync window, " +
            "deleting every occurrence before it from Google and from every device");
    }

    // ── the composed system under test ────────────────────────────────────────

    /// <summary>
    /// A real <see cref="GoogleCalendarClient"/> over a mocked transport, wired into a real
    /// <see cref="CalendarEventService"/>. The repository, migration service and hash cache are
    /// mocks; the member-tag parser and the recurrence zone factory are the real ones, because both
    /// are pure computation and substituting them would assert against invented behaviour.
    /// </summary>
    private static (Mock<HttpMessageHandler> Http, CalendarEventService Sut) CreateComposedSut()
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

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns(UserId);

        // No family send-zone: the master carries no start.timeZone either, so the write takes the
        // UTC branch and the assertion is on the INSTANT, which is what this test is about.
        var timeZoneService = new Mock<ITimeZoneService>();
        timeZoneService.Setup(s => s.GetSendZoneAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var client = new GoogleCalendarClient(
            httpClient,
            new GoogleAuthService(httpClient, options, new Mock<ILogger<GoogleAuthService>>().Object,
                new Mock<IIdTokenValidator>().Object, new Mock<ITokenStore>().Object),
            tokenStore.Object,
            currentUser.Object,
            new AccessTokenCache(TimeProvider.System),
            options,
            new RecordingLogger<GoogleCalendarClient>(),
            timeZoneService.Object,
            TestPiiRedactor.Instance);

        var sut = new CalendarEventService(
            client,
            CreateRepository().Object,
            new Mock<ICalendarMigrationService>().Object,
            new MemberTagParser(),
            new Mock<IOutboundWriteHashCache>().Object,
            currentUser.Object,
            new NodaTimeRecurrenceTimeZoneFactory(),
            new RecordingLogger<CalendarEventService>());

        return (http, sut);
    }

    private static Mock<ICalendarRepository> CreateRepository()
    {
        var repo = new Mock<ICalendarRepository>();
        var calendar = new CalendarInfo { Id = CalendarId, GoogleCalendarId = GoogleCalId, DisplayName = "Alice" };

        var edited = RecurringRow(EventId, "inst-3", EditedInstanceStart);
        var earliest = RecurringRow(Guid.Parse("11111111-1111-1111-1111-111111111111"), "inst-1", EarliestLocalRowStart);

        repo.Setup(r => r.GetEventAsync(EventId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(edited);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calendar]);
        repo.Setup(r => r.GetCalendarByIdAsync(CalendarId, It.IsAny<CancellationToken>())).ReturnsAsync(calendar);
        repo.Setup(r => r.GetSyncStateAsync(CalendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncState { CalendarInfoId = CalendarId, SyncWindowStart = WindowStart, SyncWindowEnd = WindowEnd });
        repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([earliest, edited]);
        repo.Setup(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        return repo;
    }

    private static CalendarEvent RecurringRow(Guid id, string googleEventId, DateTimeOffset start) => new()
    {
        Id = id,
        GoogleEventId = googleEventId,
        Title = "Swimming",
        Start = start,
        End = start.AddHours(1),
        Description = "Body",
        OwnerCalendarInfoId = CalendarId,
        GoogleRecurringEventId = SeriesId,
        RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=SU"
    };

    // ── transport arrangements ────────────────────────────────────────────────

    /// <summary>
    /// The events.get for the series master: a real DTSTART, and a <c>recurrence</c> array carrying
    /// only RDATE/EXDATE lines. Returns a counter of how many times it was fetched.
    /// </summary>
    private static StrongBox<int> ArrangeRdateOnlyMaster(Mock<HttpMessageHandler> http)
    {
        var fetches = new StrongBox<int>(0);
        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains($"events/{SeriesId}")),
                ItExpr.IsAny<CancellationToken>())
            .Callback(() => fetches.Value++)
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id = SeriesId,
                    summary = "Swimming",
                    start = new { dateTime = MasterStart.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) },
                    end = new { dateTime = MasterStart.AddHours(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) },
                    recurrence = new[]
                    {
                        "RDATE;VALUE=DATE-TIME:20260111T090000Z",
                        "EXDATE;VALUE=DATE-TIME:20260118T090000Z"
                    }
                }))
            });
        return fetches;
    }

    /// <summary>The events.patch on the master, capturing the request body it was sent.</summary>
    private static StrongBox<string?> ArrangePatchCapture(Mock<HttpMessageHandler> http)
    {
        var captured = new StrongBox<string?>(null);
        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Patch && req.RequestUri!.ToString().Contains($"events/{SeriesId}")),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
                captured.Value = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { id = SeriesId }))
            });
        return captured;
    }

    /// <summary>
    /// The window re-fetch the update performs after the write. It has nothing to prove here, so it
    /// returns no items — the reconcile is exercised by <c>CalendarEventServiceRecurringTests</c>.
    /// </summary>
    private static void ArrangeEmptyReconcileWindow(Mock<HttpMessageHandler> http) =>
        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("singleEvents=true")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { items = Array.Empty<object>() }))
            });
}
