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
    public async Task ListEvents_SingleEvents_UnboundedSeriesWithoutTimeBounds_StaysBounded()
    {
        // Regression guard: the app's incremental sync sends NO timeMin/timeMax. An unbounded
        // "weekly forever" series must NOT expand to the engine's hard cap (~10k) — which would
        // make the listing pathologically slow and the app upsert thousands of rows. The Simulator
        // bounds the default horizon (~14 months), so a weekly series yields well under ~100.
        using var db = CreateDb();
        var seriesStart = DateTime.UtcNow.Date.AddDays(1).AddHours(9);
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-forever",
            CalendarId = "cal-alice",
            Summary = "Standup",
            StartTime = seriesStart,
            EndTime = seriesStart.AddMinutes(30),
            UserId = "alice",
            RecurrenceRule = "RRULE:FREQ=WEEKLY" // no COUNT/UNTIL → unbounded
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // No timeMin/timeMax — the incremental-sync shape that previously expanded to the hard cap.
        var result = await sut.ListEvents("cal-alice", singleEvents: true);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");

        // ~14-month horizon of weekly occurrences ≈ 60; assert it is bounded, never near the cap.
        items.GetArrayLength().Should().BeLessThan(100);
        items.GetArrayLength().Should().BeGreaterThan(0);
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
    public async Task CreateEvent_RecurringTimedWithoutTimeZone_Returns400_LikeGoogle()
    {
        // FHQ-42: real Google rejects a recurring timed event with no start timeZone
        // (400 "Missing time zone definition for start time."). The simulator must mirror this so the
        // recurring-create E2E scenarios catch a regression of the WebApi timeZone fix.
        using var db = CreateDb();
        var sut = CreateSut(db, userId: "alice");
        var seriesStart = new DateTime(2026, 6, 2, 18, 0, 0, DateTimeKind.Utc);
        var body = new GoogleEventRequest
        {
            Summary = "No TZ",
            Start = new GoogleDateTime { DateTime = seriesStart }, // timed start, no timeZone
            End = new GoogleDateTime { DateTime = seriesStart.AddHours(1) },
            Recurrence = new List<string> { "RRULE:FREQ=DAILY;COUNT=7" }
        };

        var result = await sut.CreateEvent("cal-alice", body);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Events.CountAsync(e => e.Summary == "No TZ")).Should().Be(0);
    }

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
            Start = new GoogleDateTime { DateTime = seriesStart, TimeZone = "UTC" },
            End = new GoogleDateTime { DateTime = seriesStart.AddHours(1), TimeZone = "UTC" },
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
    public async Task CreateEvent_CapturesStartTimeZone()
    {
        // FHQ-43: the app anchors a recurring timed event to the user's effective IANA zone via
        // Google start.timeZone. The simulator must persist it (it previously discarded it) so an
        // E2E backdoor read can prove the configured zone reached Google.
        using var db = CreateDb();
        var sut = CreateSut(db, userId: "alice");
        var seriesStart = new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc);
        var body = new GoogleEventRequest
        {
            Summary = "Standup",
            Start = new GoogleDateTime { DateTime = seriesStart, TimeZone = "Europe/London" },
            End = new GoogleDateTime { DateTime = seriesStart.AddMinutes(15), TimeZone = "Europe/London" },
            Recurrence = new List<string> { "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3" }
        };

        await sut.CreateEvent("cal-alice", body);

        var stored = await db.Events.FirstAsync(e => e.Summary == "Standup");
        stored.StartTimeZone.Should().Be("Europe/London");
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

    // ── Exception overrides (FHQ-18.11 Pass 3) ────────────────────────────────

    [Fact]
    public async Task UpdateEvent_OnCompoundInstanceId_StoresExceptionThatExpansionSurfaces_SiblingsUnchanged()
    {
        // Arrange — a weekly COUNT=3 series. The app edits "This event" by PUTting the SECOND
        // occurrence's compound instance id; Google turns that into an exception override.
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
            RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");
        var secondSlot = seriesStart.AddDays(7); // 2026-06-09T18:00:00Z
        var instanceId = "evt-series_20260609T180000Z";
        var editBody = new GoogleEventRequest
        {
            Summary = "Soccer practice (moved)",
            Start = new GoogleDateTime { DateTime = secondSlot.AddHours(1) }, // shifted one hour later
            End = new GoogleDateTime { DateTime = secondSlot.AddHours(2) }
        };

        // Act — PUT the single instance, then reconcile-list the window.
        var putResult = await sut.UpdateEvent("cal-alice", instanceId, editBody);
        var listResult = await sut.ListEvents("cal-alice",
            singleEvents: true,
            timeMin: "2026-06-01T00:00:00Z",
            timeMax: "2026-08-01T00:00:00Z");

        // Assert — the PUT stored an override row linked to the master at the right slot.
        putResult.Should().BeOfType<OkObjectResult>();
        var stored = await db.Events.FindAsync(instanceId);
        stored!.RecurringEventId.Should().Be("evt-series");
        stored.OriginalStartTime.Should().Be(secondSlot);
        stored.RecurrenceRule.Should().BeNull("an override is not itself a series master");

        var ok = listResult.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = doc.RootElement.GetProperty("items");

        // Still three instances (the override replaces the computed slot, not adds to it).
        items.GetArrayLength().Should().Be(3);

        // Occurrence 1 and 3 are untouched; occurrence 2 carries the override fields +
        // recurringEventId + originalStartTime.
        items[0].GetProperty("summary").GetString().Should().Be("Soccer practice");
        items[0].GetProperty("id").GetString().Should().Be("evt-series_20260602T180000Z");

        var overridden = items[1];
        overridden.GetProperty("id").GetString().Should().Be(instanceId);
        overridden.GetProperty("summary").GetString().Should().Be("Soccer practice (moved)");
        overridden.GetProperty("recurringEventId").GetString().Should().Be("evt-series");
        overridden.GetProperty("originalStartTime").GetProperty("dateTime").GetString()
            .Should().Contain("2026-06-09T18:00:00");

        items[2].GetProperty("summary").GetString().Should().Be("Soccer practice");
        items[2].GetProperty("id").GetString().Should().Be("evt-series_20260616T180000Z");
    }

    [Fact]
    public async Task ListEvents_SingleEvents_HonoursUntilTruncation_DropsPostSplitOccurrences()
    {
        // Arrange — the "This and following" split truncates the original master's RRULE with an
        // UNTIL one second before the split instant. Expansion must drop occurrences at/after UNTIL.
        using var db = CreateDb();
        var seriesStart = new DateTime(2026, 6, 2, 18, 0, 0, DateTimeKind.Utc); // Tuesday
        // Truncate just before the THIRD occurrence (2026-06-16T18:00:00Z): UNTIL = split − 1s.
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-series",
            CalendarId = "cal-alice",
            Summary = "Soccer practice",
            StartTime = seriesStart,
            EndTime = seriesStart.AddHours(1),
            UserId = "alice",
            RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=TU;UNTIL=20260616T175959Z"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act
        var listResult = await sut.ListEvents("cal-alice",
            singleEvents: true,
            timeMin: "2026-06-01T00:00:00Z",
            timeMax: "2026-08-01T00:00:00Z");

        // Assert — only the first two occurrences survive; the third (>= UNTIL) is dropped.
        var ok = listResult.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = doc.RootElement.GetProperty("items");

        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("id").GetString().Should().Be("evt-series_20260602T180000Z");
        items[1].GetProperty("id").GetString().Should().Be("evt-series_20260609T180000Z");
    }

    [Fact]
    public async Task UpdateEvent_OnMasterId_AllEvents_ReflectsNewFields_AndPreservesPriorExceptionOverride()
    {
        // Arrange — a series with an EXISTING exception override on its second occurrence (created by
        // a prior "This event" edit). An "All events" edit then PUTs the master's fields (title).
        using var db = CreateDb();
        var seriesStart = new DateTime(2026, 6, 2, 18, 0, 0, DateTimeKind.Utc); // Tuesday
        var secondSlot = seriesStart.AddDays(7); // 2026-06-09T18:00:00Z
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-series",
            CalendarId = "cal-alice",
            Summary = "Soccer practice",
            StartTime = seriesStart,
            EndTime = seriesStart.AddHours(1),
            UserId = "alice",
            RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3"
        });
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-series_20260609T180000Z",
            CalendarId = "cal-alice",
            Summary = "Soccer practice (moved)",
            StartTime = secondSlot.AddHours(1),
            EndTime = secondSlot.AddHours(2),
            UserId = "alice",
            RecurringEventId = "evt-series",
            OriginalStartTime = secondSlot
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");
        // All-events edit: app PUTs the master's id with new field values and NO recurrence array.
        var masterEdit = new GoogleEventRequest
        {
            Summary = "Football training",
            Start = new GoogleDateTime { DateTime = seriesStart },
            End = new GoogleDateTime { DateTime = seriesStart.AddHours(1) }
        };

        // Act
        await sut.UpdateEvent("cal-alice", "evt-series", masterEdit);
        var listResult = await sut.ListEvents("cal-alice",
            singleEvents: true,
            timeMin: "2026-06-01T00:00:00Z",
            timeMax: "2026-08-01T00:00:00Z");

        // Assert — the master kept its RRULE (field-only patch) and its summary changed.
        var master = await db.Events.FindAsync("evt-series");
        master!.Summary.Should().Be("Football training");
        master.RecurrenceRule.Should().Be("RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3");

        var ok = listResult.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = doc.RootElement.GetProperty("items");

        items.GetArrayLength().Should().Be(3);
        // Occurrences 1 and 3 reflect the new master title.
        items[0].GetProperty("summary").GetString().Should().Be("Football training");
        items[2].GetProperty("summary").GetString().Should().Be("Football training");
        // The pre-existing override on occurrence 2 is PRESERVED (not clobbered by the master patch).
        items[1].GetProperty("id").GetString().Should().Be("evt-series_20260609T180000Z");
        items[1].GetProperty("summary").GetString().Should().Be("Soccer practice (moved)");
        items[1].GetProperty("recurringEventId").GetString().Should().Be("evt-series");
    }

    // ── Instance cancellation / delete-scope (FHQ-18.11 Pass 4) ───────────────

    [Fact]
    public async Task DeleteEvent_OnCompoundInstanceId_CancelsThatOccurrence_SiblingsRemain()
    {
        // Arrange — a weekly COUNT=3 series. The app deletes "This event" by DELETEing the SECOND
        // occurrence's compound instance id; Google cancels that slot (status "cancelled").
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
            RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");
        var instanceId = "evt-series_20260609T180000Z"; // second occurrence

        // Act — delete the single instance, then reconcile-list the window.
        var deleteResult = await sut.DeleteEvent("cal-alice", instanceId);
        var listResult = await sut.ListEvents("cal-alice",
            singleEvents: true,
            timeMin: "2026-06-01T00:00:00Z",
            timeMax: "2026-08-01T00:00:00Z");

        // Assert — delete is a success (204) and stored a cancellation tombstone for the slot.
        deleteResult.Should().BeOfType<NoContentResult>();
        var stored = await db.Events.FindAsync(instanceId);
        stored!.IsCancelled.Should().BeTrue();
        stored.RecurringEventId.Should().Be("evt-series");
        stored.OriginalStartTime.Should().Be(seriesStart.AddDays(7));

        var ok = listResult.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = doc.RootElement.GetProperty("items");

        // Count drops by one; only occurrences 1 and 3 remain, the cancelled slot is omitted.
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("id").GetString().Should().Be("evt-series_20260602T180000Z");
        items[1].GetProperty("id").GetString().Should().Be("evt-series_20260616T180000Z");
    }

    [Fact]
    public async Task DeleteEvent_OnCompoundInstanceId_WhenSlotHadContentOverride_CancelsIt()
    {
        // Arrange — a series whose SECOND occurrence already carries a content exception (a prior
        // "This event" edit). Deleting that occurrence converts the override to a cancellation, so
        // the slot disappears entirely.
        using var db = CreateDb();
        var seriesStart = new DateTime(2026, 6, 2, 18, 0, 0, DateTimeKind.Utc); // Tuesday
        var secondSlot = seriesStart.AddDays(7); // 2026-06-09T18:00:00Z
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-series",
            CalendarId = "cal-alice",
            Summary = "Soccer practice",
            StartTime = seriesStart,
            EndTime = seriesStart.AddHours(1),
            UserId = "alice",
            RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3"
        });
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-series_20260609T180000Z",
            CalendarId = "cal-alice",
            Summary = "Soccer practice (moved)",
            StartTime = secondSlot.AddHours(1),
            EndTime = secondSlot.AddHours(2),
            UserId = "alice",
            RecurringEventId = "evt-series",
            OriginalStartTime = secondSlot
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act
        var deleteResult = await sut.DeleteEvent("cal-alice", "evt-series_20260609T180000Z");
        var listResult = await sut.ListEvents("cal-alice",
            singleEvents: true,
            timeMin: "2026-06-01T00:00:00Z",
            timeMax: "2026-08-01T00:00:00Z");

        // Assert — the override row is flagged cancelled and the slot drops out of the listing.
        deleteResult.Should().BeOfType<NoContentResult>();
        (await db.Events.FindAsync("evt-series_20260609T180000Z"))!.IsCancelled.Should().BeTrue();

        var ok = listResult.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = doc.RootElement.GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("id").GetString().Should().Be("evt-series_20260602T180000Z");
        items[1].GetProperty("id").GetString().Should().Be("evt-series_20260616T180000Z");
    }

    [Fact]
    public async Task DeleteEvent_OnMasterId_AllEvents_RemovesTheWholeSeries()
    {
        // Arrange — "All events" delete: the app DELETEs the MASTER id. The whole series disappears.
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
            RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act
        var deleteResult = await sut.DeleteEvent("cal-alice", "evt-series");
        var listResult = await sut.ListEvents("cal-alice",
            singleEvents: true,
            timeMin: "2026-06-01T00:00:00Z",
            timeMax: "2026-08-01T00:00:00Z");

        // Assert — the master is gone and no occurrences remain.
        deleteResult.Should().BeOfType<NoContentResult>();
        db.Events.Should().NotContain(e => e.Id == "evt-series");

        var ok = listResult.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        doc.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task DeleteEvent_OnMasterUntilTruncation_ThisAndFollowing_DropsPostSplitOccurrences()
    {
        // Arrange — "This and following" delete truncates the master's RRULE with UNTIL = split − 1s.
        // Mirrors how the app patches the master before reconciling; expansion drops the tail.
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
            RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=3"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Patch the master to truncate just before the SECOND occurrence (2026-06-09T18:00:00Z).
        await sut.PatchEvent("cal-alice", "evt-series", new GoogleEventRequest
        {
            Recurrence = new List<string> { "RRULE:FREQ=WEEKLY;BYDAY=TU;UNTIL=20260609T175959Z" }
        });

        // Act
        var listResult = await sut.ListEvents("cal-alice",
            singleEvents: true,
            timeMin: "2026-06-01T00:00:00Z",
            timeMax: "2026-08-01T00:00:00Z");

        // Assert — only the first occurrence (before the split) survives.
        var ok = listResult.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var items = doc.RootElement.GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        items[0].GetProperty("id").GetString().Should().Be("evt-series_20260602T180000Z");
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
