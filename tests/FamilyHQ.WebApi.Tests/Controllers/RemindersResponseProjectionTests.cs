using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

/// <summary>
/// Reminders are stored and synced, but until they appear on a RESPONSE the browser cannot show an
/// event's reminders or say what inheriting the calendar's defaults would actually do. These tests
/// pin the projection at both response seams: the grid feed and the single-event responses.
/// </summary>
/// <remarks>
/// Two properties matter more than the mapping itself.
/// <para>
/// Four states have to stay apart — never synced (<c>null</c>), following the calendar's defaults,
/// an explicit list, and explicitly none. Google treats the last two differently on a write, and
/// "nothing known yet" is not "no reminders", so collapsing any pair here would eventually send the
/// wrong thing back.
/// </para>
/// <para>
/// And nothing Google supplied may be normalised on the way out. A delivery method the kiosk cannot
/// offer and an offset it would never choose both arrive from phones; Google is the authority on its
/// own data, so the response carries them verbatim and the kiosk's validation applies only to values
/// the kiosk itself creates.
/// </para>
/// </remarks>
public class RemindersResponseProjectionTests
{
    private static readonly Guid CalAId  = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private static readonly DateTimeOffset FixedStart = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixedEnd   = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    private const string DayKey = "2026-06-15";

    // ── The grid feed: GET /api/calendars/events ──────────────────────────────

    [Fact]
    public async Task GetEventsForMonth_EventFollowsCalendarDefault_ProjectsTheInheritingShape()
    {
        var dto = await ProjectMonthFeed(EventReminders.InheritsCalendarDefault);

        dto.Reminders.Should().NotBeNull();
        dto.Reminders!.UseDefault.Should().BeTrue();
        dto.Reminders.Overrides.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEventsForMonth_EventHasExplicitOverrides_ProjectsThemVerbatim()
    {
        // A method the kiosk's own form cannot produce and an offset it would never offer: both were
        // set elsewhere, and both have to reach the browser unchanged.
        var stored = EventReminders.Explicit([new EventReminder("sms", 47), new EventReminder("popup", 10)]);

        var dto = await ProjectMonthFeed(stored);

        dto.Reminders.Should().NotBeNull();
        dto.Reminders!.SameAs(stored).Should().BeTrue();
    }

    [Fact]
    public async Task GetEventsForMonth_EventHasNoRemindersOfItsOwn_StaysDistinctFromInheriting()
    {
        var dto = await ProjectMonthFeed(EventReminders.ExplicitlyNone);

        dto.Reminders.Should().NotBeNull();
        dto.Reminders!.UseDefault.Should().BeFalse();
        dto.Reminders.Overrides.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEventsForMonth_EventNeverSynced_ProjectsNullRatherThanAnEmptySet()
    {
        var dto = await ProjectMonthFeed(storedReminders: null);

        dto.Reminders.Should().BeNull("an absent object means nothing is known yet, not that the event has no reminders");
    }

    // ── The calendar list: GET /api/calendars ─────────────────────────────────

    [Fact]
    public async Task GetCalendars_CalendarHasDefaultReminders_ProjectsThem()
    {
        var defaults = EventReminders.Explicit([new EventReminder("popup", 30)]);
        var (repository, systemUnderTest) = CreateCalendarsSut();
        repository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { Calendar(defaults) });

        var result = await systemUnderTest.GetCalendars(CancellationToken.None);

        var dtos = result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<IEnumerable<EventCalendarDto>>().Subject.ToList();
        dtos.Should().ContainSingle()
            .Which.DefaultReminders!.SameAs(defaults).Should().BeTrue();
    }

    [Fact]
    public async Task GetCalendars_CalendarDefaultsNeverReported_ProjectsNull()
    {
        var (repository, systemUnderTest) = CreateCalendarsSut();
        repository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { Calendar(defaultReminders: null) });

        var result = await systemUnderTest.GetCalendars(CancellationToken.None);

        var dtos = result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeAssignableTo<IEnumerable<EventCalendarDto>>().Subject.ToList();
        dtos.Should().ContainSingle().Which.DefaultReminders.Should().BeNull();
    }

    // ── The single-event responses: POST/PUT /api/events ──────────────────────

    [Fact]
    public async Task UpdateEvent_ProjectsTheStoredRemindersVerbatim()
    {
        // The response the modal reads back after a save. It must reflect what is STORED — which is
        // what Google returned — rather than whatever the kiosk optimistically sent.
        var stored = EventReminders.Explicit([new EventReminder("email", 40320)]);
        var (service, systemUnderTest) = CreateEventsSut();
        service.Setup(s => s.UpdateAsync(EventId, It.IsAny<UpdateEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Event(stored));

        var result = await systemUnderTest.UpdateEvent(
            EventId, new UpdateEventRequest("Dentist", FixedStart, FixedEnd, false, null, null), CancellationToken.None);

        var dto = result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<CalendarEventDto>().Subject;
        dto.Reminders!.SameAs(stored).Should().BeTrue();
    }

    [Fact]
    public async Task CreateEvent_EventNeverSynced_ProjectsNull()
    {
        var (service, systemUnderTest) = CreateEventsSut();
        service.Setup(s => s.CreateAsync(It.IsAny<CreateEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Event(storedReminders: null));

        var result = await systemUnderTest.CreateEvent(
            new CreateEventRequest([CalAId], "Dentist", FixedStart, FixedEnd, false, null, null),
            CancellationToken.None);

        var dto = result.Should().BeOfType<CreatedResult>().Subject
            .Value.Should().BeOfType<CalendarEventDto>().Subject;
        dto.Reminders.Should().BeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task<CalendarEventDto> ProjectMonthFeed(EventReminders? storedReminders)
    {
        var calendar = Calendar(defaultReminders: null);
        var (repository, systemUnderTest) = CreateCalendarsSut();
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { Event(storedReminders, calendar) });
        repository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calendar });

        var result = await systemUnderTest.GetEventsForMonth(2026, 6, CancellationToken.None);

        var monthView = result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<MonthViewDto>().Subject;
        return monthView.Days[DayKey].Should().ContainSingle().Subject;
    }

    private static CalendarInfo Calendar(EventReminders? defaultReminders) => new()
    {
        Id = CalAId,
        GoogleCalendarId = "cal-a@google.com",
        DisplayName = "Cal A",
        Color = "#ff0000",
        IsVisible = true,
        DefaultReminders = defaultReminders
    };

    private static CalendarEvent Event(EventReminders? storedReminders, params CalendarInfo[] members) => new()
    {
        Id = EventId,
        GoogleEventId = "gid-1",
        Title = "Dentist",
        Start = FixedStart,
        End = FixedEnd,
        OwnerCalendarInfoId = CalAId,
        Members = members.Length == 0 ? [Calendar(defaultReminders: null)] : members.ToList(),
        Reminders = storedReminders
    };

    private static (Mock<ICalendarRepository> Repository, CalendarsController SystemUnderTest) CreateCalendarsSut()
    {
        var repository = new Mock<ICalendarRepository>();
        var systemUnderTest = new CalendarsController(
            repository.Object,
            new Mock<ILogger<CalendarsController>>().Object,
            new Mock<ITokenStore>().Object,
            new Mock<ICurrentUserService>().Object,
            new Mock<ICalendarSyncJobQueue>().Object,
            new Mock<ISyncJobSignal>().Object);
        return (repository, systemUnderTest);
    }

    private static (Mock<ICalendarEventService> Service, EventsController SystemUnderTest) CreateEventsSut()
    {
        var service = new Mock<ICalendarEventService>();
        return (service, new EventsController(service.Object, new Mock<ILogger<EventsController>>().Object));
    }
}
