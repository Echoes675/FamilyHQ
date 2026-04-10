using FamilyHQ.Simulator.Controllers;
using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.DTOs;
using FamilyHQ.Simulator.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace FamilyHQ.Simulator.Tests.Controllers;

public class EventsControllerTests
{
    // ── ListEvents ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListEvents_ReturnsOnlyEventsForAuthenticatedUserAndCalendar()
    {
        // Arrange
        using var db = CreateDb();
        db.Events.AddRange(
            new SimulatedEvent { Id = "evt-alice-1", CalendarId = "cal-alice", Summary = "Alice Event", UserId = "alice" },
            new SimulatedEvent { Id = "evt-bob-1", CalendarId = "cal-alice", Summary = "Bob Event", UserId = "bob" });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act
        var result = await sut.ListEvents("cal-alice");

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("evt-alice-1");
        json.Should().NotContain("evt-bob-1");
    }

    [Fact]
    public async Task ListEvents_DoesNotReturnEventsFromOtherCalendars()
    {
        // Arrange
        using var db = CreateDb();
        db.Events.AddRange(
            new SimulatedEvent { Id = "evt-cal1", CalendarId = "cal-1", Summary = "Cal 1 Event", UserId = "alice" },
            new SimulatedEvent { Id = "evt-cal2", CalendarId = "cal-2", Summary = "Cal 2 Event", UserId = "alice" });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act
        var result = await sut.ListEvents("cal-1");

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("evt-cal1");
        json.Should().NotContain("evt-cal2");
    }

    // ── CreateEvent ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateEvent_AssignsUserIdFromToken()
    {
        // Arrange
        using var db = CreateDb();
        var sut = CreateSut(db, userId: "alice");
        var body = new GoogleEventRequest
        {
            Summary = "New Meeting",
            Start = new GoogleDateTime { DateTime = DateTime.UtcNow },
            End = new GoogleDateTime { DateTime = DateTime.UtcNow.AddHours(1) }
        };

        // Act
        await sut.CreateEvent("cal-alice", body);

        // Assert
        var created = await db.Events.FirstAsync(e => e.Summary == "New Meeting");
        created.UserId.Should().Be("alice");
        created.CalendarId.Should().Be("cal-alice");
    }

    // ── UpdateEvent ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateEvent_WhenEventBelongsToUser_UpdatesSuccessfully()
    {
        // Arrange
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-1",
            CalendarId = "cal-alice",
            Summary = "Original",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1),
            UserId = "alice"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");
        var body = new GoogleEventRequest
        {
            Summary = "Updated",
            Start = new GoogleDateTime { DateTime = DateTime.UtcNow },
            End = new GoogleDateTime { DateTime = DateTime.UtcNow.AddHours(1) }
        };

        // Act
        var result = await sut.UpdateEvent("cal-alice", "evt-1", body);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var updated = await db.Events.FindAsync("evt-1");
        updated!.Summary.Should().Be("Updated");
    }

    [Fact]
    public async Task UpdateEvent_WhenEventBelongsToDifferentUser_ReturnsNotFound()
    {
        // Arrange
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-bob",
            CalendarId = "cal-bob",
            Summary = "Bob Event",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1),
            UserId = "bob"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");
        var body = new GoogleEventRequest
        {
            Summary = "Hacked",
            Start = new GoogleDateTime { DateTime = DateTime.UtcNow },
            End = new GoogleDateTime { DateTime = DateTime.UtcNow.AddHours(1) }
        };

        // Act
        var result = await sut.UpdateEvent("cal-bob", "evt-bob", body);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    // ── DeleteEvent ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteEvent_WhenEventBelongsToUser_DeletesSuccessfully()
    {
        // Arrange
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-alice",
            CalendarId = "cal-alice",
            Summary = "Alice Event",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1),
            UserId = "alice"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act
        var result = await sut.DeleteEvent("cal-alice", "evt-alice");

        // Assert
        result.Should().BeOfType<NoContentResult>();
        db.Events.Should().NotContain(e => e.Id == "evt-alice");
    }

    [Fact]
    public async Task DeleteEvent_WhenEventBelongsToDifferentUser_ReturnsNotFound()
    {
        // Arrange
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-bob",
            CalendarId = "cal-bob",
            Summary = "Bob Event",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1),
            UserId = "bob"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act
        var result = await sut.DeleteEvent("cal-bob", "evt-bob");

        // Assert — simulator returns Google-format error body on not found
        result.Should().BeOfType<NotFoundObjectResult>();
        db.Events.Should().Contain(e => e.Id == "evt-bob");
    }

    [Fact]
    public async Task DeleteEvent_RemovesAttendeeRows()
    {
        // Arrange
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
            { Id = "evt-1", CalendarId = "cal-org", Summary = "Test", UserId = "alice" });
        db.EventAttendees.Add(new SimulatedEventAttendee
            { EventId = "evt-1", AttendeeCalendarId = "cal-att" });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act
        await sut.DeleteEvent("cal-org", "evt-1");

        // Assert
        var orphans = await db.EventAttendees.Where(a => a.EventId == "evt-1").ToListAsync();
        orphans.Should().BeEmpty();
    }

    // ── ListEvents — attendee filter ──────────────────────────────────────────

    [Fact]
    public async Task ListEvents_ReturnsEventWhereCalendarIsAttendee()
    {
        // Arrange
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
            { Id = "evt-1", CalendarId = "cal-organiser", Summary = "Multi", UserId = "alice" });
        db.EventAttendees.Add(new SimulatedEventAttendee
            { EventId = "evt-1", AttendeeCalendarId = "cal-attendee" });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act
        var result = await sut.ListEvents("cal-attendee");

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("evt-1");
    }

    [Fact]
    public async Task ListEvents_ResponseIncludesOrganizerAndAttendees()
    {
        // Arrange
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
            { Id = "evt-1", CalendarId = "cal-org", Summary = "With Attendee", UserId = "alice" });
        db.EventAttendees.Add(new SimulatedEventAttendee
            { EventId = "evt-1", AttendeeCalendarId = "cal-att" });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act
        var result = await sut.ListEvents("cal-org");

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("\"organizer\"");
        json.Should().Contain("cal-org");
        json.Should().Contain("\"attendees\"");
        json.Should().Contain("cal-att");
    }

    // ── GetEvent (new) ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEvent_ReturnsEventWithOrganizerAndAttendees()
    {
        // Arrange
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
            { Id = "evt-1", CalendarId = "cal-org", Summary = "Test", UserId = "alice" });
        db.EventAttendees.Add(new SimulatedEventAttendee
            { EventId = "evt-1", AttendeeCalendarId = "cal-att" });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act
        var result = await sut.GetEvent("cal-org", "evt-1");

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("evt-1");
        json.Should().Contain("\"organizer\"");
        json.Should().Contain("\"attendees\"");
        json.Should().Contain("cal-att");
    }

    [Fact]
    public async Task GetEvent_Returns404WhenNotFound()
    {
        // Arrange
        using var db = CreateDb();
        var sut = CreateSut(db, userId: "alice");

        // Act
        var result = await sut.GetEvent("cal-org", "no-such-event");

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── PatchEvent (no-op) ───────────────────────────────────────────────────

    [Fact]
    public async Task PatchEvent_IsNoOp_Returns200()
    {
        // PatchEvent was made a no-op in the member-tag refactor — the API receives
        // attendee patches from Google but the new model derives members from the
        // description tag, so patching attendees has no effect.
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
            { Id = "evt-1", CalendarId = "cal-org", Summary = "Test", UserId = "alice" });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act — PatchEvent is synchronous (no-op)
        var result = sut.PatchEvent("cal-org", "evt-1");

        // Assert — no-op returns 200 without modifying attendees
        result.Should().BeOfType<OkResult>();
        var attendees = await db.EventAttendees.Where(a => a.EventId == "evt-1").ToListAsync();
        attendees.Should().BeEmpty();
    }

    // ── MoveEvent ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task MoveEvent_WhenEventBelongsToUser_UpdatesCalendarId()
    {
        // Arrange
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-alice",
            CalendarId = "cal-source",
            Summary = "Alice Event",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1),
            UserId = "alice"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act
        var result = await sut.MoveEvent("cal-source", "evt-alice", "cal-destination");

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var moved = await db.Events.FindAsync("evt-alice");
        moved!.CalendarId.Should().Be("cal-destination");
    }

    [Fact]
    public async Task MoveEvent_WhenEventBelongsToDifferentUser_ReturnsNotFound()
    {
        // Arrange
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-bob",
            CalendarId = "cal-bob",
            Summary = "Bob Event",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1),
            UserId = "bob"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act
        var result = await sut.MoveEvent("cal-bob", "evt-bob", "cal-alice");

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
        var unchanged = await db.Events.FindAsync("evt-bob");
        unchanged!.CalendarId.Should().Be("cal-bob");
    }

    private static SimContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SimContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimContext(options);
    }

    private static EventsController CreateSut(SimContext db, string? userId = null)
    {
        var logger = new Mock<ILogger<EventsController>>().Object;
        var controller = new EventsController(db, logger);
        var httpContext = new DefaultHttpContext();
        if (userId != null)
            httpContext.Request.Headers.Authorization = $"Bearer simulated_{userId}_abc123nonce";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }
}
