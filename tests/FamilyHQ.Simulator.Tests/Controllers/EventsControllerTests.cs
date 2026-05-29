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

    // ── PatchEvent (no-op without a recurrence array) ─────────────────────────

    [Fact]
    public async Task PatchEvent_WithoutRecurrenceArray_IsNoOp_Returns200()
    {
        // A patch carrying no recurrence field is the legacy attendee-patch case — the member-tag
        // model derives members from the description, so there is nothing to update.
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
            { Id = "evt-1", CalendarId = "cal-org", Summary = "Test", UserId = "alice" });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act — body with no recurrence field.
        var result = await sut.PatchEvent("cal-org", "evt-1", new GoogleEventRequest());

        // Assert — no-op returns 200 without modifying attendees.
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

    // ── ListEvents — recurrence expansion (FHQ-18.11) ─────────────────────────

    [Fact]
    public async Task ListEvents_SingleEvents_ExpandsSeriesMasterIntoOneInstancePerOccurrence()
    {
        // Arrange — a weekly series master with COUNT=3, anchored on a Tuesday.
        using var db = CreateDb();
        var seriesStart = new DateTime(2026, 6, 2, 18, 0, 0, DateTimeKind.Utc); // Tuesday
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-series",
            CalendarId = "cal-alice",
            Summary = "Soccer practice",
            StartTime = seriesStart,
            EndTime = seriesStart.AddHours(1),
            UserId = "alice",
            ContentHash = "hash-abc",
            RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act — a wide window so all three occurrences fall inside it.
        var result = await sut.ListEvents("cal-alice",
            singleEvents: true,
            timeMin: "2026-06-01T00:00:00Z",
            timeMax: "2026-08-01T00:00:00Z");

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);

        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");

        // Three instances, NO bare master row.
        items.GetArrayLength().Should().Be(3);
        json.Should().NotContain("\"id\":\"evt-series\"");

        // Each instance: synthetic id, recurringEventId back to the master, master content-hash.
        var first = items[0];
        first.GetProperty("id").GetString().Should().Be("evt-series_20260602T180000Z");
        first.GetProperty("recurringEventId").GetString().Should().Be("evt-series");
        first.GetProperty("extendedProperties").GetProperty("private")
            .GetProperty("content-hash").GetString().Should().Be("hash-abc");
        first.GetProperty("status").GetString().Should().Be("confirmed");

        // Instances are spaced one week apart, carrying the master's summary.
        items[1].GetProperty("id").GetString().Should().Be("evt-series_20260609T180000Z");
        items[2].GetProperty("id").GetString().Should().Be("evt-series_20260616T180000Z");
        items[2].GetProperty("summary").GetString().Should().Be("Soccer practice");
    }

    [Fact]
    public async Task ListEvents_SingleEvents_ClipsOccurrencesToTheSyncWindow()
    {
        // Arrange — unbounded weekly series; only the occurrences inside the window are emitted.
        using var db = CreateDb();
        var seriesStart = new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc); // Tuesday
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-series",
            CalendarId = "cal-alice",
            Summary = "Standup",
            StartTime = seriesStart,
            EndTime = seriesStart.AddMinutes(30),
            UserId = "alice",
            RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=TU"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Window covers only the second and third Tuesdays (Jun 9 and Jun 16).
        var result = await sut.ListEvents("cal-alice",
            singleEvents: true,
            timeMin: "2026-06-08T00:00:00Z",
            timeMax: "2026-06-17T00:00:00Z");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");

        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("id").GetString().Should().Be("evt-series_20260609T090000Z");
        items[1].GetProperty("id").GetString().Should().Be("evt-series_20260616T090000Z");
    }

    [Fact]
    public async Task ListEvents_WithoutSingleEvents_ReturnsMasterRowUnexpanded()
    {
        // Arrange — default (non-single-events) listing leaves the master untouched.
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-series",
            CalendarId = "cal-alice",
            Summary = "Soccer practice",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1),
            UserId = "alice",
            RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act — default params (singleEvents=false).
        var result = await sut.ListEvents("cal-alice");

        // Assert — the master row is returned as-is, not expanded.
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("id").GetString().Should().Be("evt-series");
    }

    [Fact]
    public async Task GetEvent_WhenMasterHasRecurrenceRule_ReturnsRecurrenceArray()
    {
        // Arrange — the two-pass master fetch must surface the RRULE in a recurrence array.
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-series",
            CalendarId = "cal-alice",
            Summary = "Soccer practice",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1),
            UserId = "alice",
            RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act
        var result = await sut.GetEvent("cal-alice", "evt-series");

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var recurrence = doc.RootElement.GetProperty("recurrence");
        recurrence.GetArrayLength().Should().Be(1);
        recurrence[0].GetString().Should().Be("RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3");
    }

    // ── WRITE-side recurrence primitives (FHQ-18.11 Pass 2) ───────────────────

    [Fact]
    public async Task CreateEvent_WithRecurrenceArray_StoresMasterAndListExpandsIntoInstances()
    {
        // Arrange — events.insert with a recurrence array creates a series master (the native
        // "create recurring event" path). The reconcile list (singleEvents=true) must then expand
        // it into one instance per occurrence.
        using var db = CreateDb();
        var sut = CreateSut(db, userId: "alice");
        var seriesStart = new DateTime(2026, 6, 2, 18, 0, 0, DateTimeKind.Utc); // Tuesday
        var body = new GoogleEventRequest
        {
            Summary = "Soccer practice",
            Start = new GoogleDateTime { DateTime = seriesStart },
            End = new GoogleDateTime { DateTime = seriesStart.AddHours(1) },
            Recurrence = new List<string> { "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3" },
            ExtendedProperties = new GoogleEventRequest.GoogleEventExtendedPropertiesRequest
            {
                Private = new Dictionary<string, string> { ["content-hash"] = "hash-abc" }
            }
        };

        // Act — create, then reconcile-list the window.
        await sut.CreateEvent("cal-alice", body);
        var stored = await db.Events.FirstAsync(e => e.Summary == "Soccer practice");
        var listResult = await sut.ListEvents("cal-alice",
            singleEvents: true,
            timeMin: "2026-06-01T00:00:00Z",
            timeMax: "2026-08-01T00:00:00Z");

        // Assert — the stored master carries the RRULE and content-hash; the list expands to 3.
        stored.RecurrenceRule.Should().Be("RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3");
        stored.ContentHash.Should().Be("hash-abc");

        var ok = listResult.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        doc.RootElement.GetProperty("items").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task CreateEvent_WithoutRecurrenceArray_StoresNonRecurringEvent()
    {
        // Arrange — a plain (non-recurring) insert must leave RecurrenceRule null.
        using var db = CreateDb();
        var sut = CreateSut(db, userId: "alice");
        var body = new GoogleEventRequest
        {
            Summary = "One-off",
            Start = new GoogleDateTime { DateTime = DateTime.UtcNow },
            End = new GoogleDateTime { DateTime = DateTime.UtcNow.AddHours(1) }
        };

        // Act
        await sut.CreateEvent("cal-alice", body);

        // Assert
        var stored = await db.Events.FirstAsync(e => e.Summary == "One-off");
        stored.RecurrenceRule.Should().BeNull();
    }

    [Fact]
    public async Task PatchEvent_WithRecurrenceArray_AddsRuleAndListExpandsIntoInstances()
    {
        // Arrange — toggle ON: an existing non-recurring event gains an RRULE via events.patch.
        using var db = CreateDb();
        var start = new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc); // Tuesday
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-toggle",
            CalendarId = "cal-alice",
            Summary = "Standup",
            StartTime = start,
            EndTime = start.AddMinutes(30),
            UserId = "alice"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");
        var patch = new GoogleEventRequest
        {
            Recurrence = new List<string> { "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3" }
        };

        // Act — patch-add, then reconcile-list.
        var patchResult = await sut.PatchEvent("cal-alice", "evt-toggle", patch);
        var listResult = await sut.ListEvents("cal-alice",
            singleEvents: true,
            timeMin: "2026-06-01T00:00:00Z",
            timeMax: "2026-08-01T00:00:00Z");

        // Assert — the master now has the rule and the list expands to 3 instances.
        patchResult.Should().BeOfType<OkObjectResult>();
        (await db.Events.FindAsync("evt-toggle"))!.RecurrenceRule
            .Should().Be("RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3");

        var ok = listResult.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        doc.RootElement.GetProperty("items").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task PatchEvent_WithEmptyRecurrenceArray_ClearsRuleAndListReturnsSingleEvent()
    {
        // Arrange — toggle OFF/collapse: a series master is patched with an empty recurrence array.
        using var db = CreateDb();
        var start = new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc); // Tuesday
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-series",
            CalendarId = "cal-alice",
            Summary = "Standup",
            StartTime = start,
            EndTime = start.AddMinutes(30),
            UserId = "alice",
            RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");
        var patch = new GoogleEventRequest { Recurrence = new List<string>() };

        // Act — patch-clear, then reconcile-list.
        await sut.PatchEvent("cal-alice", "evt-series", patch);
        var listResult = await sut.ListEvents("cal-alice",
            singleEvents: true,
            timeMin: "2026-06-01T00:00:00Z",
            timeMax: "2026-08-01T00:00:00Z");

        // Assert — the rule is cleared and the list returns exactly one (collapsed) event.
        (await db.Events.FindAsync("evt-series"))!.RecurrenceRule.Should().BeNull();

        var ok = listResult.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = doc.RootElement.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("id").GetString().Should().Be("evt-series");
    }

    [Fact]
    public async Task PatchEvent_WithRecurrenceArray_WhenEventBelongsToDifferentUser_ReturnsNotFound()
    {
        // Arrange — recurrence patches are user-scoped like every other write.
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
        var patch = new GoogleEventRequest
        {
            Recurrence = new List<string> { "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3" }
        };

        // Act
        var result = await sut.PatchEvent("cal-bob", "evt-bob", patch);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
        (await db.Events.FindAsync("evt-bob"))!.RecurrenceRule.Should().BeNull();
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
        var controller = new EventsController(db, logger, new FamilyHQ.Simulator.State.SyncFailureModeStore(), new FamilyHQ.Simulator.State.OutboundWriteCountStore());
        var httpContext = new DefaultHttpContext();
        if (userId != null)
            httpContext.Request.Headers.Authorization = $"Bearer simulated_{userId}_abc123nonce";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }
}
