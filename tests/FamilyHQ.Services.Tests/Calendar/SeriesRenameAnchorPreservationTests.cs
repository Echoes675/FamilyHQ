using System.Globalization;
using System.Net;
using System.Text.Json;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Options;
using FamilyHQ.Services.Tests.Helpers;
using FluentAssertions;
using Moq;
using Moq.Protected;
using NodaTime;
using NodaTime.Text;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// An all-in-series edit that changes no timing must leave the series' anchor INSTANT exactly where
/// Google holds it — proved against a stand-in that models the one Google write semantic that makes
/// the difference observable.
/// <para>
/// <b>Why an instant and not a wall clock.</b> Google stores the master's DTSTART as an instant.
/// What FamilyHQ can express on a write is an offset-less <c>dateTime</c> plus a <c>timeZone</c>,
/// which Google RE-RESOLVES against that zone. Those two are not interchangeable: a wall clock in a
/// DST gap names no instant, and one in the repeated hour names TWO. So rendering Google's own
/// anchor back to it is a lossy round trip, and on a series anchored inside the repeated hour it can
/// hand back the other instant — moving DTSTART, and every occurrence of the series with it, in
/// response to a rename. The family sees that in the Google Calendar app, weeks later, with nothing
/// connecting it to the edit.
/// </para>
/// <para>
/// <b>Why the transport is real.</b> The defect lives in the bytes: whether the request body carries
/// a <c>start</c> key at all. A mocked <c>IGoogleCalendarClient</c> cannot see that, and the
/// Simulator does not model Google's re-resolution, so the honest seam is
/// <c>CalendarEventService</c> ↔ <c>GoogleCalendarClient</c> over a mocked
/// <see cref="HttpMessageHandler"/>. It is still a pure unit test: no database, no clock, no socket.
/// </para>
/// <para>
/// <b>Why two zones.</b> Every instant, wall clock and offset here is computed from NodaTime's
/// bundled tz database against a NAMED zone, never from the host's. The two cases fall back on
/// different dates (Britain on 25 October 2026, the US east coast a week later) to different
/// offsets, so no single host offset can satisfy both — a one-zone version of this test would pass
/// on a British machine in BST and fail in CI.
/// </para>
/// </summary>
public class SeriesRenameAnchorPreservationTests
{
    private const string SeriesId = "series-master-id";
    private const string GoogleCalId = "calendar-under-test@example.test";
    private const string UserId = "u-rename";

    private static readonly Guid CalendarId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    /// <summary>Google's own rendering of an offset-less local date-time, and FamilyHQ's.</summary>
    private static readonly LocalDateTimePattern OffsetlessPattern =
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu-MM-dd'T'HH:mm:ss");

    /// <summary>
    /// A zone and a wall clock that occurs TWICE in it: the hour the clocks repeat when that zone
    /// falls back. 01:30 is inside the repeated hour for both — Britain's clocks go back at 02:00 on
    /// 25 October 2026, the US east coast's a week later on 1 November — so the two cases resolve to
    /// different instants at different offsets on different days.
    /// </summary>
    public static TheoryData<string, string> RepeatedHourAnchors => new()
    {
        { "Europe/London", "2026-10-25T01:30:00" },
        { "America/New_York", "2026-11-01T01:30:00" }
    };

    [Theory]
    [MemberData(nameof(RepeatedHourAnchors))]
    public async Task UpdateRecurringAsync_AllInSeriesRenameOfASeriesAnchoredInTheRepeatedHour_LeavesGooglesAnchorInstantUnmoved(
        string zoneId, string ambiguousWallClock)
    {
        // Arrange — the series is anchored to the SECOND pass of the repeated hour. That instant is
        // real, and it is the one an offset-less wall clock cannot name.
        var zone = DateTimeZoneProviders.Tzdb[zoneId];
        var local = OffsetlessPattern.Parse(ambiguousWallClock).Value;
        var mapping = zone.MapLocal(local);

        mapping.Count.Should().Be(2,
            "this test only means anything while {0} really is ambiguous in {1}; a tz-database update " +
            "that moved the transition would leave it passing while proving nothing",
            ambiguousWallClock, zoneId);

        var anchor = mapping.Last().ToDateTimeOffset();

        // The vacuity guard, and the defect stated as an arithmetic fact: send this instant back as a
        // wall clock plus a zone and what comes out the far side is a DIFFERENT instant. If this ever
        // stops being true the substitution is no longer lossy and the assertion below is free.
        ResolveAsGoogleWould(ambiguousWallClock, zoneId).Should().NotBe(anchor,
            "an offset-less wall clock in the repeated hour cannot name the second of the two instants");

        // The occurrence the user renames is a later one, nowhere near a transition — the damage is to
        // the series' ORIGIN, and it does not need the edited occurrence to be anywhere special.
        var editedOccurrence = anchor.AddDays(28);

        var (http, sut) = CreateComposedSut(editedOccurrence, zoneId);
        var google = ArrangeSeriesMasterAndPatch(http, anchor, zoneId);
        ArrangeEmptyReconcileWindow(http);

        // Act — a title-only edit: same start, same end, same all-day flag.
        await sut.UpdateRecurringAsync(
            EventId,
            new UpdateEventRequest("Renamed swimming", editedOccurrence, editedOccurrence.AddHours(1), false, "The pool", "Body"),
            RecurrenceScope.AllInSeries);

        // Assert — Google still holds the instant it started with.
        google.AnchorInstant.Should().Be(anchor,
            "a rename asks to change a title; restating when the series happens re-resolves its anchor " +
            "and moves every occurrence of the series");

        google.PatchedSummary.Should().Be("Renamed swimming", "the edit the user DID ask for still lands");
    }

    [Theory]
    [MemberData(nameof(RepeatedHourAnchors))]
    public async Task UpdateRecurringAsync_AllInSeriesTimeChangeOfASeriesAnchoredInTheRepeatedHour_StillSendsTheShiftedOrigin(
        string zoneId, string ambiguousWallClock)
    {
        // The counterpart pin. Omitting start and end is not the new normal: a request that really
        // does move the series still carries the master's origin, shifted by the delta the user
        // applied. Google re-resolving that wall clock is then the user's own instruction rather than
        // an incidental rewrite, so the anchor MOVING here is correct.
        var zone = DateTimeZoneProviders.Tzdb[zoneId];
        var local = OffsetlessPattern.Parse(ambiguousWallClock).Value;
        var anchor = zone.MapLocal(local).Last().ToDateTimeOffset();
        var editedOccurrence = anchor.AddDays(28);

        var (http, sut) = CreateComposedSut(editedOccurrence, zoneId);
        var google = ArrangeSeriesMasterAndPatch(http, anchor, zoneId);
        ArrangeEmptyReconcileWindow(http);

        // Move the edited occurrence three hours later, for the whole series.
        var movedTo = editedOccurrence.AddHours(3);
        await sut.UpdateRecurringAsync(
            EventId,
            new UpdateEventRequest("Swimming", movedTo, movedTo.AddHours(1), false, "The pool", "Body"),
            RecurrenceScope.AllInSeries);

        google.PatchedStartWallClock.Should().NotBeNull("a genuine timing change must still send start and end");
        google.PatchedStartZone.Should().Be(zoneId, "the series keeps the zone Google anchored it to");

        // The origin moved by the requested delta and by nothing else: three hours past the anchor's
        // own wall clock, expressed in the series' zone rather than the host's.
        var expected = OffsetlessPattern.Format(
            Instant.FromDateTimeOffset(anchor).InZone(zone).LocalDateTime.PlusHours(3));
        google.PatchedStartWallClock.Should().Be(expected);
    }

    // ── Google's semantics, as far as they matter here ────────────────────────

    /// <summary>
    /// What Google does with an offset-less <c>dateTime</c> and a <c>timeZone</c>: it resolves the
    /// pair against that zone. For a wall clock in the repeated hour it has two instants to choose
    /// from and picks one; for one in a DST gap it has none and shifts.
    /// </summary>
    /// <remarks>
    /// <b>Which one it picks is deliberately not the point.</b> The choice modelled here is the
    /// earlier instant, because that is what a lenient resolver does, but the defect does not depend
    /// on it: the pair simply cannot name both instants, so ONE of the two anchors a series can
    /// legitimately hold is unreachable by a write that sends a wall clock. Anchoring a test on
    /// Google's tie-break would be asserting against a guess; anchoring it on the loss is not.
    /// </remarks>
    private static DateTimeOffset ResolveAsGoogleWould(string offsetlessWallClock, string zoneId) =>
        DateTimeZoneProviders.Tzdb[zoneId]
            .AtLeniently(OffsetlessPattern.Parse(offsetlessWallClock).Value)
            .ToDateTimeOffset();

    /// <summary>The master as the stand-in holds it, and what the patch it received said.</summary>
    private sealed class FakeGoogleSeries(DateTimeOffset anchor)
    {
        public DateTimeOffset AnchorInstant { get; set; } = anchor;
        public string? PatchedSummary { get; set; }
        public string? PatchedStartWallClock { get; set; }
        public string? PatchedStartZone { get; set; }
    }

    /// <summary>
    /// The events.get for the master and the events.patch that follows, sharing one piece of state:
    /// the anchor instant. A patch body carrying a <c>start</c> re-resolves it exactly as Google
    /// would; a body with no <c>start</c> key leaves it alone, which is what events.patch's merge
    /// does.
    /// </summary>
    private static FakeGoogleSeries ArrangeSeriesMasterAndPatch(
        Mock<HttpMessageHandler> http, DateTimeOffset anchor, string zoneId)
    {
        var series = new FakeGoogleSeries(anchor);
        var zone = DateTimeZoneProviders.Tzdb[zoneId];

        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains($"events/{SeriesId}")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                // Google reports the master's DTSTART with its true offset, alongside the zone the
                // recurrence is anchored to. Rendering it with the offset is what makes the instant
                // unambiguous on the way IN — and what FamilyHQ cannot express on the way out.
                var withOffset = Instant.FromDateTimeOffset(series.AnchorInstant).InZone(zone);
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        id = SeriesId,
                        summary = "Swimming",
                        start = new
                        {
                            dateTime = withOffset.ToDateTimeOffset().ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture),
                            timeZone = zoneId
                        },
                        end = new
                        {
                            dateTime = withOffset.ToDateTimeOffset().AddHours(1).ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture),
                            timeZone = zoneId
                        },
                        recurrence = new[] { "RRULE:FREQ=WEEKLY;BYDAY=SU" }
                    }))
                };
            });

        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Patch && req.RequestUri!.ToString().Contains($"events/{SeriesId}")),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(body);

                series.PatchedSummary = doc.RootElement.GetProperty("summary").GetString();

                if (!doc.RootElement.TryGetProperty("start", out var start)
                    || start.ValueKind == JsonValueKind.Null
                    || start.GetProperty("dateTime").ValueKind == JsonValueKind.Null)
                {
                    return; // no start in the body — the merge leaves Google's own DTSTART alone.
                }

                series.PatchedStartWallClock = start.GetProperty("dateTime").GetString();
                series.PatchedStartZone = start.GetProperty("timeZone").GetString();
                series.AnchorInstant = ResolveAsGoogleWould(series.PatchedStartWallClock!, series.PatchedStartZone!);
            })
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { id = SeriesId }))
            });

        return series;
    }

    /// <summary>
    /// The window re-fetch the update performs afterwards. It has nothing to prove here, so it
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

    // ── the composed system under test ────────────────────────────────────────

    /// <summary>
    /// A real <see cref="GoogleCalendarClient"/> over a mocked transport, wired into a real
    /// <see cref="CalendarEventService"/>. The repository and the hash cache are mocks; the
    /// wall-clock conversion is the REAL <see cref="TimeZoneService"/>, because substituting the one
    /// computation this test is about would assert against invented behaviour. Its persisted-zone
    /// reads go to mocked repositories and yield nothing, so the family has no configured zone —
    /// which is deliberate: the series' own zone must reach the write without one.
    /// </summary>
    private static (Mock<HttpMessageHandler> Http, CalendarEventService Sut) CreateComposedSut(
        DateTimeOffset editedOccurrence, string seriesZoneId)
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

        var timeZoneService = new TimeZoneService(
            currentUser.Object,
            Mock.Of<IDisplaySettingRepository>(),
            Mock.Of<ILocationSettingRepository>(),
            Mock.Of<ITimeZoneLookup>());

        var client = new GoogleCalendarClient(
            httpClient,
            new GoogleAuthService(httpClient, options, new RecordingLogger<GoogleAuthService>(),
                new Mock<IIdTokenValidator>().Object, new Mock<ITokenStore>().Object),
            tokenStore.Object,
            currentUser.Object,
            new AccessTokenCache(TimeProvider.System),
            options,
            new RecordingLogger<GoogleCalendarClient>(),
            timeZoneService,
            TestPiiRedactor.Instance);

        var sut = new CalendarEventService(
            client,
            CreateRepository(editedOccurrence, seriesZoneId).Object,
            new Mock<ICalendarMigrationService>().Object,
            new MemberTagParser(),
            new Mock<IOutboundWriteHashCache>().Object,
            currentUser.Object,
            new NodaTimeRecurrenceTimeZoneFactory(),
            new RecordingLogger<CalendarEventService>());

        return (http, sut);
    }

    private static Mock<ICalendarRepository> CreateRepository(DateTimeOffset editedOccurrence, string seriesZoneId)
    {
        var repo = new Mock<ICalendarRepository>();
        var calendar = new CalendarInfo { Id = CalendarId, GoogleCalendarId = GoogleCalId, DisplayName = "Alice" };

        var edited = new CalendarEvent
        {
            Id = EventId,
            GoogleEventId = "inst-later",
            Title = "Swimming",
            Start = editedOccurrence,
            End = editedOccurrence.AddHours(1),
            Description = "Body",
            OwnerCalendarInfoId = CalendarId,
            GoogleRecurringEventId = SeriesId,
            RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=SU",
            IanaTimeZone = seriesZoneId
        };

        repo.Setup(r => r.GetEventAsync(EventId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(edited);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calendar]);
        repo.Setup(r => r.GetCalendarByIdAsync(CalendarId, It.IsAny<CancellationToken>())).ReturnsAsync(calendar);
        repo.Setup(r => r.GetSyncStateAsync(CalendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncState
            {
                CalendarInfoId = CalendarId,
                SyncWindowStart = editedOccurrence.AddDays(-30),
                SyncWindowEnd = editedOccurrence.AddDays(30)
            });
        repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>())).ReturnsAsync([edited]);
        repo.Setup(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        return repo;
    }
}
