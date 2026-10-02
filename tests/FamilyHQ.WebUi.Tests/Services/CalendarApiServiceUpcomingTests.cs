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
/// has to carry every field of every event row to the view model untouched — it is the only source
/// the timeline has. <see cref="CalendarApiService.GetEventAsync"/> backs the tap-to-open path and
/// must answer a 404 with null rather than an exception: a row can outlive its event if it is deleted
/// on a phone between the list fetch and the tap, and that is ordinary, not an error worth a dialog.
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

    private static readonly DateTimeOffset FixedNextReminder = new(2026, 3, 21, 8, 45, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixedStart        = new(2026, 3, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixedEnd          = new(2026, 3, 21, 10, 0, 0, TimeSpan.Zero);

    // The options ASP.NET Core serialises a response with, so the test feeds the client the same
    // camel-cased shape production does rather than a .NET-default one it would also accept.
    private static readonly JsonSerializerOptions WireFormat = new(JsonSerializerDefaults.Web);

    // ── GetUpcomingRemindersAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetUpcomingRemindersAsync_MapsEveryFieldOfARow()
    {
        var dto = new UpcomingReminderEventDto(
            EventId, "Dentist", FixedStart, EventIsAllDay: false,
            ReminderCount: 2, FixedNextReminder, NextReminderMinutes: 15, NextReminderMethod: "popup",
            IsDefault: false, [new ReminderMemberDto("Alice", "#ff0000")]);
        var sut = CreateSut(Json(new List<UpcomingReminderEventDto> { dto }));

        var rows = await sut.GetUpcomingRemindersAsync(CancellationToken.None);

        var row = rows.Should().ContainSingle().Subject;
        row.EventId.Should().Be(EventId);
        row.EventTitle.Should().Be("Dentist");
        row.EventStart.Should().Be(FixedStart);
        row.EventIsAllDay.Should().BeFalse();
        row.ReminderCount.Should().Be(2);
        row.NextReminderAt.Should().Be(FixedNextReminder);
        row.NextReminderMinutes.Should().Be(15);
        row.NextReminderMethod.Should().Be("popup");
        row.IsDefault.Should().BeFalse();
        var member = row.Members.Should().ContainSingle().Subject;
        member.DisplayName.Should().Be("Alice");
        member.Color.Should().Be("#ff0000");
    }

    [Fact]
    public async Task GetUpcomingRemindersAsync_PreservesAMethodTheKioskCannotCreate()
    {
        // The kiosk's own picker only ever offers popup/email, but a phone can set anything Google
        // accepts — "sms" among them. Round-tripping it verbatim is exactly what
        // UpcomingReminderEventDto's own remark promises; silently normalising it would be a display
        // that disagrees with Google.
        var dto = new UpcomingReminderEventDto(
            EventId, "Dentist", FixedStart, EventIsAllDay: false,
            ReminderCount: 1, FixedNextReminder, NextReminderMinutes: 10, NextReminderMethod: "sms",
            IsDefault: true, []);
        var sut = CreateSut(Json(new List<UpcomingReminderEventDto> { dto }));

        var rows = await sut.GetUpcomingRemindersAsync(CancellationToken.None);

        rows.Should().ContainSingle().Which.NextReminderMethod.Should().Be("sms");
    }

    [Fact]
    public async Task GetUpcomingRemindersAsync_WithNoRows_ReturnsEmptyNotNull()
    {
        var sut = CreateSut(Json(new List<UpcomingReminderEventDto>()));

        var rows = await sut.GetUpcomingRemindersAsync(CancellationToken.None);

        rows.Should().NotBeNull();
        rows.Should().BeEmpty();
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
        // back to predicting the owning calendar instead of reading what the server stored. This is
        // the tap-to-open path from the reminders timeline, so dropping them here would make that one
        // path the one that still predicts.
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
