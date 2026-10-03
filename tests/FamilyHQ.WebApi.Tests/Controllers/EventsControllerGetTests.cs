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
/// GET /api/events/{eventId} — added so the reminders timeline can open an event that sits in a month
/// the dashboard never loaded, where there is nothing in memory to open. Also covers the two
/// owning-calendar fields this endpoint is the first to populate: EventModalLogic.OwningCalendarDefaults
/// reads them for an existing event instead of re-predicting the owner client-side.
/// </summary>
public class EventsControllerGetTests
{
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid CalAId  = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string UserId = "user-1";

    private static readonly DateTimeOffset FixedStart = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixedEnd   = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetEvent_WhenTheEventExistsForThisUser_ReturnsIt()
    {
        var (calendarRepository, _, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA);

        calendarRepository.Setup(r => r.GetEventAsync(EventId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evt);
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(CalAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calA);

        var result = await sut.GetEvent(EventId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<CalendarEventDto>().Subject;
        dto.Id.Should().Be(EventId);
        dto.GoogleEventId.Should().Be("gid-1");
    }

    [Fact]
    public async Task GetEvent_WhenTheEventDoesNotExist_Returns404()
    {
        var (calendarRepository, _, sut) = CreateSut();
        calendarRepository.Setup(r => r.GetEventAsync(EventId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        var result = await sut.GetEvent(EventId, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    // not 403 — do not confirm it exists
    [Fact]
    public async Task GetEvent_WhenTheEventBelongsToAnotherUser_Returns404()
    {
        // The userId overload already enforces this scoping by returning null for an event it doesn't
        // own, which is indistinguishable here from a missing id. The controller must not add its own
        // ownership check on top: doing so (and answering 403 for "exists but isn't yours") would leak
        // the event's existence to a caller who has no legitimate way to learn it.
        var (calendarRepository, _, sut) = CreateSut();
        calendarRepository.Setup(r => r.GetEventAsync(EventId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        var result = await sut.GetEvent(EventId, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
        // No event means nothing to look an owner up for — a stray call here would be the N+1 this
        // endpoint has no excuse for, since it fetches exactly one event.
        calendarRepository.Verify(r => r.GetCalendarByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetEvent_CarriesTheOwningCalendarAndItsDefaults()
    {
        var (calendarRepository, _, sut) = CreateSut();
        var defaults = EventReminders.Explicit([new EventReminder("popup", 30)]);
        var calA = Cal(CalAId, "cal-a@google.com");
        calA.DefaultReminders = defaults;
        var evt = Event(EventId, "gid-1", CalAId, calA);

        calendarRepository.Setup(r => r.GetEventAsync(EventId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evt);
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(CalAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calA);

        var result = await sut.GetEvent(EventId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<CalendarEventDto>().Subject;
        dto.OwningCalendarId.Should().Be(CalAId);
        dto.OwningCalendarDefaultReminders.Should().BeSameAs(defaults);
    }

    [Fact]
    public async Task GetEvent_CarriesRemindersExactlyAsStored_IncludingNull()
    {
        var (calendarRepository, _, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA);
        evt.Reminders = null; // never synced — must not be reported as "no reminders"

        calendarRepository.Setup(r => r.GetEventAsync(EventId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(evt);
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(CalAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calA);

        var result = await sut.GetEvent(EventId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<CalendarEventDto>().Subject;
        dto.Reminders.Should().BeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CalendarInfo Cal(Guid id, string googleId) =>
        new() { Id = id, GoogleCalendarId = googleId, DisplayName = "Cal" };

    private static CalendarEvent Event(Guid id, string googleId, Guid ownerCalId, params CalendarInfo[] cals) =>
        new() { Id = id, GoogleEventId = googleId, Title = "Test",
                Start = FixedStart, End = FixedEnd,
                OwnerCalendarInfoId = ownerCalId, Members = cals.ToList() };

    private static (Mock<ICalendarRepository>, Mock<ICalendarEventService>, EventsController) CreateSut()
    {
        var calendarRepository = new Mock<ICalendarRepository>();
        var service = new Mock<ICalendarEventService>();
        var currentUser = new Mock<ICurrentUserService>();
        var logger = new Mock<ILogger<EventsController>>();
        currentUser.SetupGet(c => c.UserId).Returns(UserId);

        var sut = new EventsController(service.Object, calendarRepository.Object, currentUser.Object, logger.Object);
        return (calendarRepository, service, sut);
    }
}
