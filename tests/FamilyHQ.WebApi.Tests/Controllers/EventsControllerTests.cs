using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class EventsControllerTests
{
    private static readonly Guid CalAId  = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    // ── POST /api/events ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateEvent_Returns201WithCalendarEventDto()
    {
        var (service, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var newEvent = Event(EventId, "gid-1", CalAId, calA);

        service.Setup(s => s.CreateAsync(It.IsAny<CreateEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newEvent);

        var request = new CreateEventRequest(
            [CalAId], "Title", FixedStart, FixedEnd, false, null, null);
        var result = await sut.CreateEvent(request, CancellationToken.None);

        var created = result.Should().BeOfType<CreatedResult>().Subject;
        var dto = created.Value.Should().BeOfType<CalendarEventDto>().Subject;
        dto.Id.Should().Be(EventId);
        dto.GoogleEventId.Should().Be("gid-1");
        dto.Members.Should().ContainSingle(c => c.Id == CalAId);
        // No ViewModel fields in response
        dto.Should().BeOfType<CalendarEventDto>();
    }

    [Fact]
    public async Task CreateEvent_InvalidRequest_Returns400()
    {
        var (_, sut) = CreateSut();
        var request = new CreateEventRequest([], "Title", FixedStart, FixedEnd, false, null, null);
        var result = await sut.CreateEvent(request, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── PUT /api/events/{eventId} ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateEvent_Returns200WithCalendarEventDto()
    {
        var (service, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var updatedEvent = Event(EventId, "gid-1", CalAId, calA);

        service.Setup(s => s.UpdateAsync(EventId, It.IsAny<UpdateEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedEvent);

        var request = new UpdateEventRequest(
            "New Title", FixedStart, FixedEnd, false, null, null);
        var result = await sut.UpdateEvent(EventId, request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<CalendarEventDto>().Subject;
        dto.Id.Should().Be(EventId);
        dto.Members.Should().ContainSingle(c => c.Id == CalAId);
    }

    [Fact]
    public async Task UpdateEvent_InvalidRequest_Returns400()
    {
        var (_, sut) = CreateSut();
        var request = new UpdateEventRequest(
            "", FixedStart, FixedEnd, false, null, null);
        var result = await sut.UpdateEvent(EventId, request, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateEvent_RecurringEvent_ProjectsRecurrenceOntoDto()
    {
        var (service, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var updatedEvent = Event(EventId, "gid-1", CalAId, calA);
        updatedEvent.GoogleRecurringEventId = "series-1";
        updatedEvent.RecurrenceRule = "RRULE:FREQ=WEEKLY";

        service.Setup(s => s.UpdateAsync(EventId, It.IsAny<UpdateEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedEvent);

        var request = new UpdateEventRequest("New Title", FixedStart, FixedEnd, false, null, null);
        var result = await sut.UpdateEvent(EventId, request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<CalendarEventDto>().Subject;
        dto.IsRecurring.Should().BeTrue();
        dto.RecurrenceRule.Should().Be("RRULE:FREQ=WEEKLY");
    }

    // ── PUT /api/events/{eventId}/recurring ───────────────────────────────────

    [Fact]
    public async Task UpdateRecurringEvent_DispatchesToServiceWithScope_Returns200()
    {
        var (service, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var updatedEvent = Event(EventId, "gid-1", CalAId, calA);
        updatedEvent.GoogleRecurringEventId = "series-1";
        updatedEvent.RecurrenceRule = "RRULE:FREQ=DAILY";

        service.Setup(s => s.UpdateRecurringAsync(
                EventId, It.IsAny<UpdateEventRequest>(), RecurrenceScope.ThisAndFollowing, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedEvent);

        var request = new UpdateEventRequest("New Title", FixedStart, FixedEnd, false, null, null,
            RecurrenceRule: "RRULE:FREQ=DAILY");
        var result = await sut.UpdateRecurringEvent(EventId, RecurrenceScope.ThisAndFollowing, request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<CalendarEventDto>().Subject;
        dto.Id.Should().Be(EventId);
        service.Verify(s => s.UpdateRecurringAsync(
            EventId, It.IsAny<UpdateEventRequest>(), RecurrenceScope.ThisAndFollowing, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateRecurringEvent_InvalidRequest_Returns400()
    {
        var (_, sut) = CreateSut();
        var request = new UpdateEventRequest("", FixedStart, FixedEnd, false, null, null);
        var result = await sut.UpdateRecurringEvent(EventId, RecurrenceScope.AllInSeries, request, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateRecurringEvent_EventNotPartOfSeries_Returns400()
    {
        var (service, sut) = CreateSut();
        var request = new UpdateEventRequest("Title", FixedStart, FixedEnd, false, null, null);
        service.Setup(s => s.UpdateRecurringAsync(
                EventId, It.IsAny<UpdateEventRequest>(), It.IsAny<RecurrenceScope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"Event {EventId} is not part of a recurring series."));

        var result = await sut.UpdateRecurringEvent(EventId, RecurrenceScope.ThisOnly, request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateRecurringEvent_MemberChangeOutsideAllScope_Returns400()
    {
        var (service, sut) = CreateSut();
        var request = new UpdateEventRequest("Title", FixedStart, FixedEnd, false, null, null);
        service.Setup(s => s.UpdateRecurringAsync(
                EventId, It.IsAny<UpdateEventRequest>(), RecurrenceScope.ThisOnly, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Member changes apply to the whole series."));

        var result = await sut.UpdateRecurringEvent(EventId, RecurrenceScope.ThisOnly, request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── DELETE /api/events/{eventId}/recurring ────────────────────────────────

    [Fact]
    public async Task DeleteRecurringEvent_DispatchesToServiceWithScope_Returns204()
    {
        var (service, sut) = CreateSut();
        service.Setup(s => s.DeleteRecurringAsync(EventId, RecurrenceScope.AllInSeries, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await sut.DeleteRecurringEvent(EventId, RecurrenceScope.AllInSeries, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        service.Verify(s => s.DeleteRecurringAsync(EventId, RecurrenceScope.AllInSeries, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteRecurringEvent_InvalidScope_Returns400()
    {
        var (service, sut) = CreateSut();
        // The service's default scope branch fails fast on an unmapped scope value.
        service.Setup(s => s.DeleteRecurringAsync(EventId, It.IsAny<RecurrenceScope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentOutOfRangeException("scope"));

        var result = await sut.DeleteRecurringEvent(EventId, (RecurrenceScope)99, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DeleteRecurringEvent_EventNotPartOfSeries_Returns400()
    {
        var (service, sut) = CreateSut();
        service.Setup(s => s.DeleteRecurringAsync(EventId, It.IsAny<RecurrenceScope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"Event {EventId} is not part of a recurring series."));

        var result = await sut.DeleteRecurringEvent(EventId, RecurrenceScope.ThisOnly, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── DELETE /api/events/{eventId} ──────────────────────────────────────────

    [Fact]
    public async Task DeleteEvent_Returns204()
    {
        var (service, sut) = CreateSut();
        service.Setup(s => s.DeleteAsync(EventId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await sut.DeleteEvent(EventId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    // ── PUT /api/events/{eventId}/members ─────────────────────────────────────

    [Fact]
    public async Task SetMembers_Returns200WithCalendarEventDto()
    {
        var (service, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var updatedEvent = Event(EventId, "gid-1", CalAId, calA);

        service.Setup(s => s.SetMembersAsync(EventId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedEvent);

        var request = new SetEventMembersRequest([CalAId]);
        var result = await sut.SetMembers(EventId, request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<CalendarEventDto>();
    }

    [Fact]
    public async Task SetMembers_EmptyList_Returns400()
    {
        var (_, sut) = CreateSut();
        var request = new SetEventMembersRequest([]);
        var result = await sut.SetMembers(EventId, request, CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CalendarInfo Cal(Guid id, string googleId) =>
        new() { Id = id, GoogleCalendarId = googleId, DisplayName = "Cal" };

    private static readonly DateTimeOffset FixedStart = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixedEnd   = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    private static CalendarEvent Event(Guid id, string googleId, Guid ownerCalId, params CalendarInfo[] cals) =>
        new() { Id = id, GoogleEventId = googleId, Title = "Test",
                Start = FixedStart, End = FixedEnd,
                OwnerCalendarInfoId = ownerCalId, Members = cals.ToList() };

    private static (Mock<ICalendarEventService>, EventsController) CreateSut()
    {
        var service = new Mock<ICalendarEventService>();
        var logger  = new Mock<ILogger<EventsController>>();
        return (service, new EventsController(service.Object, logger.Object));
    }
}
