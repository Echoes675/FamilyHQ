using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Models;
using FamilyHQ.WebUi.Services;
using FamilyHQ.WebUi.ViewModels;
using Moq;
using Moq.Protected;

namespace FamilyHQ.WebUi.Tests.Services;

/// <summary>
/// The client half of the reminders timeline. <see cref="CalendarApiService.GetUpcomingRemindersAsync"/>
/// has to carry every field of the envelope — the household's date, the instant that date expires,
/// and every field of every row — to the view model untouched, because it is the only source the
/// timeline has and the only place "which day is it" is answered for it.
/// <see cref="CalendarApiService.GetEventAsync"/> fetches one event by id and must answer a 404 with
/// null rather than an exception: an event can be deleted on a phone between whatever named the id
/// and the fetch for it, which is ordinary, not an error worth a dialog.
/// </summary>
/// <remarks>
/// These go through a real serialise/deserialise round trip, following
/// <see cref="CalendarApiServiceRemindersTests"/>'s shape — a stubbed <see cref="HttpMessageHandler"/>,
/// no real HTTP — rather than constructing view models directly, since the wire format is exactly
/// where a field could quietly go missing.
/// </remarks>
public class CalendarApiServiceUpcomingTests
{
    private static readonly Guid CalAId  = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CalBId  = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private static readonly DateTimeOffset FixedStart = new(2026, 3, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixedEnd   = new(2026, 3, 21, 10, 0, 0, TimeSpan.Zero);

    // The options ASP.NET Core serialises a response with, so the test feeds the client the same
    // camel-cased shape production does rather than a .NET-default one it would also accept.
    private static readonly JsonSerializerOptions WireFormat = new(JsonSerializerDefaults.Web);

    // ── GetUpcomingRemindersAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetUpcomingRemindersAsync_MapsEveryFieldOfTheEnvelopeAndItsRows()
    {
        var dto = new UpcomingRemindersDto(
            new DateOnly(2026, 3, 21),
            new DateTimeOffset(2026, 3, 22, 0, 0, 0, TimeSpan.Zero),
            [new UpcomingReminderEventDto(
                EventId, "Dentist", new DateOnly(2026, 3, 21), new TimeOnly(9, 0),
                [new ReminderMemberDto("Alice", "#ff0000")])]);
        var sut = CreateSut(Json(dto));

        var payload = await sut.GetUpcomingRemindersAsync(CancellationToken.None);

        payload.Today.Should().Be(new DateOnly(2026, 3, 21));
        payload.NextRolloverUtc.Should().Be(new DateTimeOffset(2026, 3, 22, 0, 0, 0, TimeSpan.Zero));
        var row = payload.Rows.Should().ContainSingle().Subject;
        row.EventId.Should().Be(EventId);
        row.EventTitle.Should().Be("Dentist");
        row.EventDate.Should().Be(new DateOnly(2026, 3, 21));
        row.EventTimeOfDay.Should().Be(new TimeOnly(9, 0));
        var member = row.Members.Should().ContainSingle().Subject;
        member.DisplayName.Should().Be("Alice");
        member.Color.Should().Be("#ff0000");
    }

    [Fact]
    public async Task GetUpcomingRemindersAsync_AnAllDayRowKeepsANullTimeThroughTheWireFormat()
    {
        // A null EventTimeOfDay is the ONLY all-day signal the client gets, and the row renders "all
        // day" off it. A null that deserialised to TimeOnly.MinValue instead would tell the family an
        // all-day event starts at 00:00 — which is the stored boundary read as a time, the exact
        // reading this design exists to stop.
        var dto = new UpcomingRemindersDto(
            new DateOnly(2026, 3, 21),
            new DateTimeOffset(2026, 3, 22, 0, 0, 0, TimeSpan.Zero),
            [new UpcomingReminderEventDto(
                EventId, "Bank Holiday", new DateOnly(2026, 3, 21), null, [])]);
        var sut = CreateSut(Json(dto));

        var payload = await sut.GetUpcomingRemindersAsync(CancellationToken.None);

        payload.Rows.Should().ContainSingle().Which.EventTimeOfDay.Should().BeNull();
    }

    [Fact]
    public async Task GetUpcomingRemindersAsync_WithTheTimeOfDayPropertyAbsentEntirely_StillReadsAsAllDay()
    {
        // Not the same test as the one above. That one serialises an explicit null; this one is the
        // shape a server that OMITS the property produces, which is what a future serialiser setting
        // (DefaultIgnoreCondition) would quietly turn every all-day row into.
        var sut = CreateSut(
            """
            {"today":"2026-03-21","nextRolloverUtc":"2026-03-22T00:00:00+00:00",
             "rows":[{"eventId":"eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee","eventTitle":"Bank Holiday",
                      "eventDate":"2026-03-21","members":[]}]}
            """);

        var payload = await sut.GetUpcomingRemindersAsync(CancellationToken.None);

        payload.Rows.Should().ContainSingle().Which.EventTimeOfDay.Should().BeNull();
    }

    [Fact]
    public async Task GetUpcomingRemindersAsync_WithNoRows_ReturnsAnEnvelopeWithAnEmptyListNotNull()
    {
        var dto = new UpcomingRemindersDto(
            new DateOnly(2026, 3, 21), new DateTimeOffset(2026, 3, 22, 0, 0, 0, TimeSpan.Zero), []);
        var sut = CreateSut(Json(dto));

        var payload = await sut.GetUpcomingRemindersAsync(CancellationToken.None);

        payload.Rows.Should().NotBeNull().And.BeEmpty();
        payload.Today.Should().Be(new DateOnly(2026, 3, 21));
    }

    // ── GetEventAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEventAsync_WhenTheServerReturns404_ReturnsNull()
    {
        var sut = CreateSutWithStatus(HttpStatusCode.NotFound, "");

        var evt = await sut.GetEventAsync(EventId, CancellationToken.None);

        evt.Should().BeNull();
    }

    [Fact]
    public async Task GetEventAsync_CarriesRemindersAndTheOwningCalendarDefaults()
    {
        // Regression guard: GetEventAsync is the third site in CalendarApiService that builds a
        // CalendarEventViewModel. The other two (GetEventsForMonthAsync, MapToViewModel) were fixed in
        // a dedicated round after omitting these two fields left the event modal silently falling
        // back to predicting the owning calendar instead of reading what the server stored. Dropping
        // them here would make this the one path that still predicts.
        var reminders = EventReminders.Explicit([new EventReminder("popup", 15)]);
        var ownerDefaults = EventReminders.Explicit([new EventReminder("popup", 30)]);
        var dto = new CalendarEventDto(
            EventId, "gid-1", "Dentist", FixedStart, FixedEnd, false, null, null,
            [new EventCalendarDto(CalAId, "Cal A", "#ff0000")],
            Reminders: reminders,
            OwningCalendarId: CalBId,
            OwningCalendarDefaultReminders: ownerDefaults);
        var sut = CreateSut(Json(dto));

        var evt = await sut.GetEventAsync(EventId, CancellationToken.None);

        evt.Should().NotBeNull();
        evt!.Reminders!.SameAs(reminders).Should().BeTrue();
        evt.OwningCalendarId.Should().Be(CalBId);
        evt.OwningCalendarDefaultReminders!.SameAs(ownerDefaults).Should().BeTrue();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Json<T>(T payload) => JsonSerializer.Serialize(payload, WireFormat);

    private static CalendarApiService CreateSut(string body) => CreateSutWithStatus(HttpStatusCode.OK, body);

    private static CalendarApiService CreateSutWithStatus(HttpStatusCode status, string body)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = status,
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

        return new CalendarApiService(new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://test.local/")
        });
    }
}
