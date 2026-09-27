using System.Text.Json;
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
using Xunit;

namespace FamilyHQ.Simulator.Tests.Controllers;

/// <summary>
/// The wiring between the event endpoints and Google's reminder behaviour: every write validates the
/// body as sent and stores what Google would store, and every read reports what Google would report.
/// </summary>
/// <remarks>
/// The behaviours asserted here were observed against the live API, including the two error bodies —
/// Google puts these reminder rejections in the <c>calendar</c> domain, not the <c>global</c> one it
/// uses for a missing time zone.
/// </remarks>
public class EventsControllerRemindersTests
{
    private const string CountLimitReason = "eventRemindersCountExceedsLimit";
    private const string UseDefaultConflictReason = "cannotUseDefaultRemindersAndSpecifyOverride";
    private const string StoredUseDefault = """{"useDefault":true}""";

    // ── CreateEvent ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateEvent_WithSixOverrides_IsRejectedAndStoresNothing()
    {
        using var db = CreateDb();
        var sut = CreateSut(db, userId: "alice");

        var result = await sut.CreateEvent("cal-alice", TimedBody(SixOverrides()));

        ReasonOf(result).Should().Be(CountLimitReason);
        db.Events.Should().BeEmpty("a rejected write leaves no event behind");
    }

    [Fact]
    public async Task CreateEvent_WithUseDefaultAndOverrides_IsRejected()
    {
        using var db = CreateDb();
        var sut = CreateSut(db, userId: "alice");

        var result = await sut.CreateEvent(
            "cal-alice", TimedBody(new GoogleEventReminders(UseDefault: true, Overrides: [new("popup", 30)])));

        ReasonOf(result).Should().Be(UseDefaultConflictReason);
        db.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateEvent_WithRemindersGoogleRewrites_SucceedsAndStoresTheRewrittenValue()
    {
        using var db = CreateDb();
        var sut = CreateSut(db, userId: "alice");

        var result = await sut.CreateEvent("cal-alice", TimedBody(RewrittenByGoogle()));

        result.Should().BeOfType<OkObjectResult>("Google answers 200 and rewrites rather than rejecting");
        StoredReminders(await db.Events.SingleAsync()).Overrides
            .Should().BeEquivalentTo(ExpectedAfterRewrite());
    }

    // ── UpdateEvent (PUT) ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateEvent_WithSixOverrides_IsRejectedAndLeavesStoredRemindersUntouched()
    {
        using var db = CreateDb();
        await SeedEventAsync(db, "evt-1", StoredUseDefault);
        var sut = CreateSut(db, userId: "alice");

        var result = await sut.UpdateEvent("cal-alice", "evt-1", TimedBody(SixOverrides()));

        ReasonOf(result).Should().Be(CountLimitReason);
        (await db.Events.SingleAsync()).RemindersJson.Should().Be(StoredUseDefault);
    }

    [Fact]
    public async Task UpdateEvent_WithUseDefaultAndOverrides_IsRejectedAndLeavesStoredRemindersUntouched()
    {
        using var db = CreateDb();
        await SeedEventAsync(db, "evt-1", StoredUseDefault);
        var sut = CreateSut(db, userId: "alice");

        var result = await sut.UpdateEvent(
            "cal-alice", "evt-1", TimedBody(new GoogleEventReminders(UseDefault: true, Overrides: [new("popup", 30)])));

        ReasonOf(result).Should().Be(UseDefaultConflictReason);
        (await db.Events.SingleAsync()).RemindersJson.Should().Be(StoredUseDefault);
    }

    [Fact]
    public async Task UpdateEvent_WithRemindersGoogleRewrites_SucceedsAndStoresTheRewrittenValue()
    {
        using var db = CreateDb();
        await SeedEventAsync(db, "evt-1", StoredUseDefault);
        var sut = CreateSut(db, userId: "alice");

        var result = await sut.UpdateEvent("cal-alice", "evt-1", TimedBody(RewrittenByGoogle()));

        result.Should().BeOfType<OkObjectResult>();
        StoredReminders(await db.Events.SingleAsync()).Overrides
            .Should().BeEquivalentTo(ExpectedAfterRewrite());
    }

    // ── PatchEvent ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PatchEvent_WithSixOverrides_IsRejectedAndLeavesStoredRemindersUntouched()
    {
        using var db = CreateDb();
        await SeedEventAsync(db, "evt-1", StoredUseDefault);
        var sut = CreateSut(db, userId: "alice");

        var result = await sut.PatchEvent(
            "cal-alice", "evt-1", new GoogleEventRequest { Reminders = SixOverrides() });

        ReasonOf(result).Should().Be(CountLimitReason);
        (await db.Events.SingleAsync()).RemindersJson.Should().Be(StoredUseDefault);
    }

    [Fact]
    public async Task PatchEvent_WithUseDefaultAndOverrides_IsRejectedAndLeavesStoredRemindersUntouched()
    {
        using var db = CreateDb();
        await SeedEventAsync(db, "evt-1", StoredUseDefault);
        var sut = CreateSut(db, userId: "alice");

        var result = await sut.PatchEvent(
            "cal-alice",
            "evt-1",
            new GoogleEventRequest
            {
                Reminders = new GoogleEventReminders(UseDefault: true, Overrides: [new("popup", 30)])
            });

        ReasonOf(result).Should().Be(UseDefaultConflictReason);
        (await db.Events.SingleAsync()).RemindersJson.Should().Be(StoredUseDefault);
    }

    [Fact]
    public async Task PatchEvent_WithRemindersGoogleRewrites_SucceedsAndStoresTheRewrittenValue()
    {
        using var db = CreateDb();
        await SeedEventAsync(db, "evt-1", StoredUseDefault);
        var sut = CreateSut(db, userId: "alice");

        var result = await sut.PatchEvent(
            "cal-alice", "evt-1", new GoogleEventRequest { Reminders = RewrittenByGoogle() });

        result.Should().BeOfType<OkObjectResult>();
        StoredReminders(await db.Events.SingleAsync()).Overrides
            .Should().BeEquivalentTo(ExpectedAfterRewrite());
    }

    [Fact]
    public async Task PatchEvent_WithAnEmptyOverridesArray_StoresTheExplicitlyNoneState()
    {
        // A patch carrying reminders replaces the whole overrides array, so this is how a client
        // removes every reminder from an event that had some.
        using var db = CreateDb();
        await SeedEventAsync(db, "evt-1", """{"useDefault":false,"overrides":[{"method":"popup","minutes":30}]}""");
        var sut = CreateSut(db, userId: "alice");

        var result = await sut.PatchEvent(
            "cal-alice",
            "evt-1",
            new GoogleEventRequest { Reminders = new GoogleEventReminders(UseDefault: false, Overrides: []) });

        result.Should().BeOfType<OkObjectResult>();
        var stored = StoredReminders(await db.Events.SingleAsync());
        stored.UseDefault.Should().BeFalse();
        stored.Overrides.Should().BeEmpty();
    }

    // ── The single-occurrence ("This event") write path ────────────────────────

    [Fact]
    public async Task UpdateEvent_OnAnUnstoredSeriesInstance_StoresTheRewrittenRemindersOnTheException()
    {
        // Editing one occurrence makes it an exception with reminders of its own. Google stores the
        // reminders the request carried, rewritten the same way as any other write.
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-master",
            CalendarId = "cal-alice",
            Summary = "Weekly",
            UserId = "alice",
            StartTime = new DateTime(2027, 1, 12, 14, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2027, 1, 12, 15, 0, 0, DateTimeKind.Utc),
            RecurrenceRule = "RRULE:FREQ=WEEKLY;COUNT=3",
            StartTimeZone = "Europe/London"
        });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, userId: "alice");

        var result = await sut.UpdateEvent(
            "cal-alice", "evt-master_20270112T140000Z", TimedBody(RewrittenByGoogle()));

        result.Should().BeOfType<OkObjectResult>();
        var exception = await db.Events.SingleAsync(e => e.Id == "evt-master_20270112T140000Z");
        StoredReminders(exception).Overrides.Should().BeEquivalentTo(ExpectedAfterRewrite());
    }

    [Fact]
    public async Task UpdateEvent_OnAnUnstoredSeriesInstance_WithSixOverrides_IsRejectedAndStoresNoException()
    {
        using var db = CreateDb();
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-master",
            CalendarId = "cal-alice",
            Summary = "Weekly",
            UserId = "alice",
            StartTime = new DateTime(2027, 1, 12, 14, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2027, 1, 12, 15, 0, 0, DateTimeKind.Utc),
            RecurrenceRule = "RRULE:FREQ=WEEKLY;COUNT=3",
            StartTimeZone = "Europe/London"
        });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, userId: "alice");

        var result = await sut.UpdateEvent(
            "cal-alice", "evt-master_20270112T140000Z", TimedBody(SixOverrides()));

        ReasonOf(result).Should().Be(CountLimitReason);
        db.Events.Should().ContainSingle().Which.Id.Should().Be("evt-master");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The reminder error <c>reason</c> a rejected write answered with.</summary>
    private static string? ReasonOf(IActionResult result)
    {
        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = JsonSerializer.SerializeToElement(bad.Value).GetProperty("error");

        error.GetProperty("code").GetInt32().Should().Be(400);
        var first = error.GetProperty("errors")[0];
        first.GetProperty("domain").GetString().Should().Be("calendar");
        first.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();

        return first.GetProperty("reason").GetString();
    }

    /// <summary>One more override than Google will store, which is the only count it rejects.</summary>
    private static GoogleEventReminders SixOverrides() =>
        new(UseDefault: false, Overrides:
        [
            new("popup", 5), new("popup", 10), new("popup", 15),
            new("popup", 20), new("popup", 25), new("popup", 30)
        ]);

    /// <summary>
    /// Five overrides Google accepts with a 200 and then stores as three: an offset below zero and one
    /// above four weeks are clamped, the duplicate pair collapses, and the unknown method is dropped.
    /// </summary>
    private static GoogleEventReminders RewrittenByGoogle() =>
        new(UseDefault: false, Overrides:
        [
            new("popup", -540), new("popup", 40321), new("email", 60), new("email", 60), new("sms", 30)
        ]);

    private static GoogleEventReminderOverride[] ExpectedAfterRewrite() =>
        [new("popup", 0), new("popup", 40320), new("email", 60)];

    private static GoogleEventReminders StoredReminders(SimulatedEvent stored) =>
        JsonSerializer.Deserialize<GoogleEventReminders>(stored.RemindersJson!)!;

    private static GoogleEventRequest TimedBody(GoogleEventReminders? reminders) => new()
    {
        Summary = "Reminder Event",
        Start = new GoogleDateTime { DateTime = new DateTime(2027, 1, 12, 14, 0, 0, DateTimeKind.Utc) },
        End = new GoogleDateTime { DateTime = new DateTime(2027, 1, 12, 15, 0, 0, DateTimeKind.Utc) },
        Reminders = reminders
    };

    private static async Task SeedEventAsync(SimContext db, string id, string? remindersJson)
    {
        db.Events.Add(new SimulatedEvent
        {
            Id = id,
            CalendarId = "cal-alice",
            Summary = "Reminder Event",
            UserId = "alice",
            StartTime = new DateTime(2027, 1, 12, 14, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2027, 1, 12, 15, 0, 0, DateTimeKind.Utc),
            RemindersJson = remindersJson
        });
        await db.SaveChangesAsync();
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
        var controller = new EventsController(
            db,
            new Mock<ILogger<EventsController>>().Object,
            new FamilyHQ.Simulator.State.SyncFailureModeStore(),
            new FamilyHQ.Simulator.State.OutboundWriteCountStore(),
            new FamilyHQ.Simulator.Services.NodaTimeRecurrenceTimeZoneFactory());
        var httpContext = new DefaultHttpContext();
        if (userId != null)
            httpContext.Request.Headers.Authorization = $"Bearer simulated_{userId}_abc123nonce";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }
}
