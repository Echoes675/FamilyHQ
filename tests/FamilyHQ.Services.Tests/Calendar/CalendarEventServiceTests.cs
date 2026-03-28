using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class CalendarEventServiceTests
{
    private static readonly Guid CalAId  = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CalBId  = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_SingleCalendar_CallsCreateEventOnlyNoPatch()
    {
        var (google, repo, rrule, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([calA]);
        google.Setup(g => g.CreateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, CancellationToken _) =>
                { e.GoogleEventId = "new-gid"; return e; });
        repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var request = new CreateEventRequest(
            [CalAId], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        var result = await sut.CreateAsync(request);

        google.Verify(g => g.CreateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        google.Verify(g => g.PatchEventAttendeesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        result.GoogleEventId.Should().Be("new-gid");
        result.OwnerCalendarInfoId.Should().Be(CalAId);
    }

    [Fact]
    public async Task CreateAsync_TwoCalendars_CallsCreateThenPatch()
    {
        var (google, repo, rrule, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var calB = Cal(CalBId, "cal-b@google.com");
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([calA, calB]);
        google.Setup(g => g.CreateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, CancellationToken _) =>
                { e.GoogleEventId = "new-gid"; return e; });
        google.Setup(g => g.PatchEventAttendeesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var request = new CreateEventRequest(
            [CalAId, CalBId], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        await sut.CreateAsync(request);

        google.Verify(g => g.PatchEventAttendeesAsync(
            "cal-a@google.com", "new-gid",
            It.Is<IEnumerable<string>>(ids => ids.Single() == "cal-b@google.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_UnknownCalendarId_ThrowsValidationException()
    {
        var (google, repo, rrule, sut) = CreateSut();
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Cal(CalAId, "cal-a@google.com")]);

        var request = new CreateEventRequest(
            [CalBId], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        await sut.Invoking(s => s.CreateAsync(request))
            .Should().ThrowAsync<Exception>(); // ValidationException or similar
    }

    [Fact]
    public async Task CreateAsync_DbFailureAfterGoogleSuccess_LogsReconciliationErrorAndRethrows()
    {
        var (google, repo, rrule, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.CreateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, CancellationToken _) =>
                { e.GoogleEventId = "gid-db-fail"; return e; });
        repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        var request = new CreateEventRequest(
            [CalAId], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

        await sut.Invoking(s => s.CreateAsync(request))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_CallsUpdateEventWithOwnerCalendar()
    {
        var (google, repo, rrule, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var evt = Event(EventId, "old-gid", CalAId, calA);
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.UpdateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, CancellationToken _) => e);
        repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var request = new UpdateEventRequest("New Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        var result = await sut.UpdateAsync(EventId, request);

        google.Verify(g => g.UpdateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        result.Title.Should().Be("New Title");
    }

    // ── AddCalendarAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task AddCalendarAsync_CallsPatchWithNewAttendee()
    {
        var (google, repo, rrule, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var calB = Cal(CalBId, "cal-b@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA); // only calA linked
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA, calB]);
        google.Setup(g => g.PatchEventAttendeesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await sut.AddCalendarAsync(EventId, CalBId);

        google.Verify(g => g.PatchEventAttendeesAsync(
            "cal-a@google.com", "gid-1",
            It.Is<IEnumerable<string>>(ids => ids.Contains("cal-b@google.com")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddCalendarAsync_Idempotent_NoGoogleCallWhenAlreadyLinked()
    {
        var (google, repo, rrule, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA); // calA already linked
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);

        await sut.AddCalendarAsync(EventId, CalAId);

        google.Verify(g => g.PatchEventAttendeesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── RemoveCalendarAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task RemoveCalendarAsync_NonOwner_CallsPatchWithoutRemovedCalendar()
    {
        var (google, repo, rrule, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var calB = Cal(CalBId, "cal-b@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA, calB); // owner=A, B is attendee
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA, calB]);
        google.Setup(g => g.PatchEventAttendeesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await sut.RemoveCalendarAsync(EventId, CalBId);

        google.Verify(g => g.PatchEventAttendeesAsync(
            "cal-a@google.com", "gid-1",
            It.Is<IEnumerable<string>>(ids => !ids.Contains("cal-b@google.com")),
            It.IsAny<CancellationToken>()), Times.Once);
        google.Verify(g => g.MoveEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveCalendarAsync_OwnerWithOthers_MovesEventThenPatchesInOrder()
    {
        var (google, repo, rrule, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var calB = Cal(CalBId, "cal-b@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA, calB); // owner=A, removing A
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA, calB]);

        var callOrder = new List<string>();
        google.Setup(g => g.MoveEventAsync("cal-a@google.com", "gid-1", "cal-b@google.com", It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("Move"))
            .ReturnsAsync("gid-1");
        google.Setup(g => g.PatchEventAttendeesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("Patch"))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await sut.RemoveCalendarAsync(EventId, CalAId);

        google.Verify(g => g.MoveEventAsync("cal-a@google.com", "gid-1", "cal-b@google.com", It.IsAny<CancellationToken>()), Times.Once);
        google.Verify(g => g.PatchEventAttendeesAsync(
            "cal-b@google.com", "gid-1",
            It.Is<IEnumerable<string>>(ids => !ids.Any()), // empty — new owner has no other attendees
            It.IsAny<CancellationToken>()), Times.Once);
        callOrder.Should().ContainInOrder("Move", "Patch");
    }

    [Fact]
    public async Task RemoveCalendarAsync_LastCalendar_DelegatesToDelete()
    {
        var (google, repo, rrule, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA); // only calendar
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.GetEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleEventDetail("gid-1", "cal-a@google.com", []));
        google.Setup(g => g.DeleteEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await sut.RemoveCalendarAsync(EventId, CalAId);

        google.Verify(g => g.PatchEventAttendeesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        google.Verify(g => g.MoveEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        google.Verify(g => g.DeleteEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_NoExternalAttendees_CallsGoogleDelete()
    {
        var (google, repo, rrule, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA);
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.GetEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleEventDetail("gid-1", "cal-a@google.com", []));
        google.Setup(g => g.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await sut.DeleteAsync(EventId);

        google.Verify(g => g.DeleteEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ExternalAttendeePresent_SkipsGoogleDelete()
    {
        var (google, repo, rrule, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA);
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.GetEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleEventDetail("gid-1", "cal-a@google.com", ["external@gmail.com"]));
        repo.Setup(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await sut.DeleteAsync(EventId);

        google.Verify(g => g.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_GetEventReturnsNull_SkipsGoogleDeleteDeletesLocally()
    {
        var (google, repo, rrule, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com");
        var evt = Event(EventId, "gid-1", CalAId, calA);
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.GetEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoogleEventDetail?)null);
        repo.Setup(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await sut.DeleteAsync(EventId);

        google.Verify(g => g.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CalendarInfo Cal(Guid id, string googleId) =>
        new() { Id = id, GoogleCalendarId = googleId, DisplayName = googleId };

    private static CalendarEvent Event(Guid id, string googleId, Guid ownerCalId, params CalendarInfo[] cals) =>
        new()
        {
            Id = id,
            GoogleEventId = googleId,
            Title = "Test Event",
            Start = DateTimeOffset.UtcNow,
            End = DateTimeOffset.UtcNow.AddHours(1),
            OwnerCalendarInfoId = ownerCalId,
            Calendars = cals.ToList()
        };

    private static (Mock<IGoogleCalendarClient>, Mock<ICalendarRepository>, Mock<IRruleExpander>, CalendarEventService) CreateSut()
    {
        var google = new Mock<IGoogleCalendarClient>();
        var repo   = new Mock<ICalendarRepository>();
        var rrule  = new Mock<IRruleExpander>();
        var logger = new Mock<ILogger<CalendarEventService>>();
        return (google, repo, rrule, new CalendarEventService(google.Object, repo.Object, rrule.Object, logger.Object));
    }
}
