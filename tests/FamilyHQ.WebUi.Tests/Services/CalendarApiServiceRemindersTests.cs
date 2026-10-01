using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Models;
using FamilyHQ.WebUi.Components.Dashboard;
using FamilyHQ.WebUi.Services;
using FamilyHQ.WebUi.ViewModels;
using Moq;
using Moq.Protected;

namespace FamilyHQ.WebUi.Tests.Services;

/// <summary>
/// The client half of carrying reminders to the browser: what the API sends has to survive
/// deserialisation and the view-model mapping intact, because the reminder picker opens from a view
/// model and has nothing else to read.
/// </summary>
/// <remarks>
/// These go through a real serialise/deserialise round trip rather than constructing view models
/// directly, since the wire format is exactly where a state could quietly collapse — an absent
/// object arriving as an empty list, or an unrecognised delivery method being dropped.
/// </remarks>
public class CalendarApiServiceRemindersTests
{
    private static readonly Guid CalAId  = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private static readonly DateTimeOffset FixedStart = new(2026, 3, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixedEnd   = new(2026, 3, 21, 10, 0, 0, TimeSpan.Zero);

    private const string DayKey = "2026-03-21";

    // The options ASP.NET Core serialises a response with, so the test feeds the client the same
    // camel-cased shape production does rather than a .NET-default one it would also accept.
    private static readonly JsonSerializerOptions WireFormat = new(JsonSerializerDefaults.Web);

    // ── The grid feed reaches the view model ──────────────────────────────────

    [Fact]
    public async Task GetEventsForMonthAsync_EventFollowsCalendarDefault_ReachesTheViewModelAsInheriting()
    {
        var vm = await MapMonthFeed(EventReminders.InheritsCalendarDefault);

        vm.Reminders.Should().NotBeNull();
        vm.Reminders!.UseDefault.Should().BeTrue();
        vm.Reminders.Overrides.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEventsForMonthAsync_ExplicitOverrides_SurviveValuesTheKioskCouldNotCreate()
    {
        var sent = EventReminders.Explicit([new EventReminder("sms", 47), new EventReminder("popup", 10)]);

        var vm = await MapMonthFeed(sent);

        vm.Reminders!.SameAs(sent).Should().BeTrue();
    }

    [Fact]
    public async Task GetEventsForMonthAsync_EventHasNoRemindersOfItsOwn_StaysDistinctFromInheriting()
    {
        var vm = await MapMonthFeed(EventReminders.ExplicitlyNone);

        vm.Reminders.Should().NotBeNull();
        vm.Reminders!.UseDefault.Should().BeFalse();
        vm.Reminders.Overrides.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEventsForMonthAsync_AbsentReminders_StayAbsentRatherThanBecomingAnEmptySet()
    {
        var vm = await MapMonthFeed(sentReminders: null);

        vm.Reminders.Should().BeNull();
    }

    [Fact]
    public async Task UpdateEventAsync_RemindersOnTheSaveResponse_ReachTheViewModel()
    {
        var stored = EventReminders.Explicit([new EventReminder("popup", 15)]);
        var sut = CreateSut(Json(EventDto(stored)));

        var vm = await sut.UpdateEventAsync(
            EventId, new UpdateEventRequest("Dentist", FixedStart, FixedEnd, false, null, null), CancellationToken.None);

        vm.Reminders!.SameAs(stored).Should().BeTrue();
    }

    // ── The calendar list carries its defaults ────────────────────────────────

    [Fact]
    public async Task GetCalendarsAsync_CalendarDefaults_ReachTheSummaryViewModel()
    {
        var defaults = EventReminders.Explicit([new EventReminder("popup", 30)]);
        var sut = CreateSut(Json(new List<EventCalendarDto>
        {
            new(CalAId, "Cal A", "#ff0000", IsShared: false, IsVisible: true, DefaultReminders: defaults)
        }));

        var calendars = await sut.GetCalendarsAsync(CancellationToken.None);

        calendars.Should().ContainSingle()
            .Which.DefaultReminders!.SameAs(defaults).Should().BeTrue();
    }

    [Fact]
    public async Task GetCalendarsAsync_CalendarDefaultsNeverReported_LeavesThemNull()
    {
        var sut = CreateSut(Json(new List<EventCalendarDto> { new(CalAId, "Cal A", "#ff0000") }));

        var calendars = await sut.GetCalendarsAsync(CancellationToken.None);

        calendars.Should().ContainSingle().Which.DefaultReminders.Should().BeNull();
    }

    // ── What the plumbing is for ──────────────────────────────────────────────

    [Fact]
    public async Task PlumbedValues_LetTheDefaultsBeCopiedInWhenInheritanceIsSwitchedOff()
    {
        // The acceptance criterion this whole seam exists for: the Google Calendar app pre-fills the
        // calendar's defaults when the family stops inheriting them. Without both halves on the
        // client the picker lands on an empty list, silently turning "inherit" into "none".
        var defaults = EventReminders.Explicit([new EventReminder("popup", 30), new EventReminder("email", 1440)]);
        var calendars = await CreateSut(Json(new List<EventCalendarDto>
        {
            new(CalAId, "Cal A", "#ff0000", IsShared: false, IsVisible: true, DefaultReminders: defaults)
        })).GetCalendarsAsync(CancellationToken.None);
        var evt = await MapMonthFeed(EventReminders.InheritsCalendarDefault);

        var picker = ReminderPickerModel.From(evt.Reminders, calendars.Single().DefaultReminders, evt.IsAllDay);
        picker.StopUsingCalendarDefault();

        picker.ToEventReminders().SameAs(defaults).Should().BeTrue();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<CalendarEventViewModel> MapMonthFeed(EventReminders? sentReminders)
    {
        var monthView = new MonthViewDto
        {
            Year = 2026,
            Month = 3,
            Days = new() { [DayKey] = [EventDto(sentReminders)] }
        };

        var result = await CreateSut(Json(monthView)).GetEventsForMonthAsync(2026, 3, CancellationToken.None);

        return result.Days[DayKey].Should().ContainSingle().Subject;
    }

    private static CalendarEventDto EventDto(EventReminders? reminders) => new(
        EventId, "gid-1", "Dentist", FixedStart, FixedEnd, false, null, null,
        [new EventCalendarDto(CalAId, "Cal A", "#ff0000")],
        IsRecurring: false,
        RecurrenceRule: null,
        Reminders: reminders);

    private static string Json<T>(T payload) => JsonSerializer.Serialize(payload, WireFormat);

    private static CalendarApiService CreateSut(string body)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

        return new CalendarApiService(new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://test.local/")
        });
    }
}
