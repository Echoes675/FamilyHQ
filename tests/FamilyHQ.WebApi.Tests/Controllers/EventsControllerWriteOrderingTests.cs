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
/// Where the owning-calendar load sits relative to the write, for every action that writes. Google is
/// the system of record, so an action that reports a failure for work which already landed there is
/// worse than a slow one: the person at the kiosk is told the save failed and presses save again, and
/// a retried create leaves the family a second event in the Google Calendar app. So the calendars are
/// loaded before the service is called and the owner is found among them afterwards, which leaves
/// nothing between a successful write and its response that can fail.
/// </summary>
/// <remarks>
/// These tests pin the ordering rather than the response, because the response is identical either
/// way — a later tidy-up that folds the load back into the response path would read as a
/// simplification and pass every other test in this folder.
/// <para>
/// The write is observed at <see cref="ICalendarEventService"/>, which is as far as the controller can
/// see: the service is what talks to Google and then to the database. The marker is recorded when the
/// controller dispatches the write, so an entry after it would mean I/O that a half-done write could
/// strand.
/// </para>
/// </remarks>
public class EventsControllerWriteOrderingTests
{
    private const string CalendarsLoaded = "calendars loaded";
    private const string WriteDispatched = "write dispatched to the service";

    private static readonly Guid CalAId  = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private static readonly DateTimeOffset FixedStart = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixedEnd   = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateEvent_LoadsTheCalendarsBeforeTheWriteAndReadsNothingAfterIt()
    {
        var (service, _, order, sut) = CreateRecordingSut(Cal());
        service.Setup(s => s.CreateAsync(It.IsAny<CreateEventRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add(WriteDispatched))
            .ReturnsAsync(Event());

        await sut.CreateEvent(
            new CreateEventRequest([CalAId], "Title", FixedStart, FixedEnd, false, null, null),
            CancellationToken.None);

        order.Should().Equal(CalendarsLoaded, WriteDispatched);
    }

    [Fact]
    public async Task CreateEvent_ResolvesTheOwnerWithoutALookupAfterTheWrite()
    {
        // Weaker than the ordering assertion above — it names one method rather than ruling out I/O —
        // but it is the method that used to be called here, so it says plainly what was moved. The
        // owner fields are asserted alongside it: the point is that the load moved, not that it went.
        var defaults = EventReminders.Explicit([new EventReminder("popup", 15)]);
        var owner = Cal();
        owner.DefaultReminders = defaults;
        var (service, repository, _, sut) = CreateRecordingSut(owner);
        service.Setup(s => s.CreateAsync(It.IsAny<CreateEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Event());

        var result = await sut.CreateEvent(
            new CreateEventRequest([CalAId], "Title", FixedStart, FixedEnd, false, null, null),
            CancellationToken.None);

        var dto = result.Should().BeOfType<CreatedResult>().Subject
            .Value.Should().BeOfType<CalendarEventDto>().Subject;
        dto.OwningCalendarId.Should().Be(CalAId);
        dto.OwningCalendarDefaultReminders.Should().BeSameAs(defaults);
        repository.Verify(
            r => r.GetCalendarByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateEvent_LoadsTheCalendarsBeforeTheWriteAndReadsNothingAfterIt()
    {
        var (service, _, order, sut) = CreateRecordingSut(Cal());
        service.Setup(s => s.UpdateAsync(EventId, It.IsAny<UpdateEventRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add(WriteDispatched))
            .ReturnsAsync(Event());

        await sut.UpdateEvent(
            EventId, new UpdateEventRequest("New Title", FixedStart, FixedEnd, false, null, null),
            CancellationToken.None);

        order.Should().Equal(CalendarsLoaded, WriteDispatched);
    }

    [Fact]
    public async Task UpdateRecurringEvent_LoadsTheCalendarsBeforeTheWriteAndReadsNothingAfterIt()
    {
        var (service, _, order, sut) = CreateRecordingSut(Cal());
        service.Setup(s => s.UpdateRecurringAsync(
                EventId, It.IsAny<UpdateEventRequest>(), RecurrenceScope.AllInSeries, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add(WriteDispatched))
            .ReturnsAsync(Event());

        await sut.UpdateRecurringEvent(
            EventId, RecurrenceScope.AllInSeries,
            new UpdateEventRequest("New Title", FixedStart, FixedEnd, false, null, null,
                RecurrenceRule: "RRULE:FREQ=DAILY"),
            CancellationToken.None);

        order.Should().Equal(CalendarsLoaded, WriteDispatched);
    }

    [Fact]
    public async Task SetMembers_LoadsTheCalendarsBeforeTheWriteAndReadsNothingAfterIt()
    {
        var (service, _, order, sut) = CreateRecordingSut(Cal());
        service.Setup(s => s.SetMembersAsync(EventId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add(WriteDispatched))
            .ReturnsAsync(Event());

        await sut.SetMembers(EventId, new SetEventMembersRequest([CalAId]), CancellationToken.None);

        order.Should().Equal(CalendarsLoaded, WriteDispatched);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CalendarInfo Cal() =>
        new() { Id = CalAId, GoogleCalendarId = "cal-a@google.com", DisplayName = "Cal" };

    private static CalendarEvent Event() =>
        new() { Id = EventId, GoogleEventId = "gid-1", Title = "Test",
                Start = FixedStart, End = FixedEnd,
                OwnerCalendarInfoId = CalAId, Members = [Cal()] };

    private static (
        Mock<ICalendarEventService> Service,
        Mock<ICalendarRepository> Repository,
        List<string> Order,
        EventsController SystemUnderTest) CreateRecordingSut(CalendarInfo owner)
    {
        var order = new List<string>();
        // Strict, so the pre-write load is the only repository call a write action is allowed to make.
        // A lookup reintroduced after the write throws here whichever repository method it uses, which
        // a Verify naming one method would not catch.
        var repository = new Mock<ICalendarRepository>(MockBehavior.Strict);
        repository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add(CalendarsLoaded))
            .ReturnsAsync([owner]);

        var service = new Mock<ICalendarEventService>();
        var sut = new EventsController(
            service.Object,
            repository.Object,
            new Mock<ICurrentUserService>().Object,
            new Mock<ILogger<EventsController>>().Object);
        return (service, repository, order, sut);
    }
}
