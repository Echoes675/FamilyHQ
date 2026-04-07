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
