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

    // ── Reading an event ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetEvent_OnATimedEventWithNothingStored_ReportsFollowingTheCalendarDefault()
    {
        // Google never omits the reminders object. A timed event with nothing of its own follows the
        // calendar's default, and says so rather than reporting the default's contents.
        using var db = CreateDb();
        await SeedCalendarAsync(db, "cal-alice", defaults: [new("popup", 30)]);
        await SeedEventAsync(db, "evt-1", remindersJson: null);
        var sut = CreateSut(db, userId: "alice");

        var reminders = RemindersOf(await sut.GetEvent("cal-alice", "evt-1"));

        reminders.GetProperty("useDefault").GetBoolean().Should().BeTrue();
        reminders.TryGetProperty("overrides", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetEvent_OnAnAllDayEventWithNothingStored_ReportsTheCalendarDefaultsAsItsOwn()
    {
        // An all-day event never inherits: Google copies the calendar's defaults onto it as explicit
        // overrides, so useDefault:true does not occur on one.
        using var db = CreateDb();
        await SeedCalendarAsync(db, "cal-alice", defaults: [new("popup", 30)]);
        await SeedEventAsync(db, "evt-1", remindersJson: null, isAllDay: true);
        var sut = CreateSut(db, userId: "alice");

        var reminders = RemindersOf(await sut.GetEvent("cal-alice", "evt-1"));

        reminders.GetProperty("useDefault").GetBoolean().Should().BeFalse();
        MinutesOf(reminders).Should().Equal(30);
    }

    [Fact]
    public async Task GetEvent_OnAnAllDayEventWhenTheCalendarHasNoDefaults_ReportsNeitherDefaultNorOverrides()
    {
        using var db = CreateDb();
        await SeedCalendarAsync(db, "cal-alice", defaults: null);
        await SeedEventAsync(db, "evt-1", remindersJson: null, isAllDay: true);
        var sut = CreateSut(db, userId: "alice");

        var reminders = RemindersOf(await sut.GetEvent("cal-alice", "evt-1"));

        reminders.GetProperty("useDefault").GetBoolean().Should().BeFalse();
        reminders.TryGetProperty("overrides", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetEvent_WhenStoredAsExplicitlyNone_OmitsTheOverridesKey()
    {
        // Stored as useDefault:false with an empty array; Google reads it back with no overrides key
        // at all, so a client must read a missing array as "none" rather than as "unknown".
        using var db = CreateDb();
        await SeedCalendarAsync(db, "cal-alice", defaults: [new("popup", 30)]);
        await SeedEventAsync(db, "evt-1", """{"useDefault":false,"overrides":[]}""");
        var sut = CreateSut(db, userId: "alice");

        var reminders = RemindersOf(await sut.GetEvent("cal-alice", "evt-1"));

        reminders.GetProperty("useDefault").GetBoolean().Should().BeFalse();
        reminders.TryGetProperty("overrides", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetEvent_DoesNotReportOverridesInTheOrderTheyWereStored()
    {
        using var db = CreateDb();
        await SeedEventAsync(
            db,
            "evt-1",
            """{"useDefault":false,"overrides":[{"method":"popup","minutes":10},{"method":"popup","minutes":60}]}""");
        var sut = CreateSut(db, userId: "alice");

        var reminders = RemindersOf(await sut.GetEvent("cal-alice", "evt-1"));

        MinutesOf(reminders).Should().NotEqual(new[] { 10, 60 }).And.BeEquivalentTo(new[] { 10, 60 });
    }

    [Fact]
    public async Task CreateEvent_WithNoRemindersKey_ReportsFollowingTheCalendarDefault()
    {
        // The create response is a read like any other, so it reports the object too — the app never
        // sees a missing reminders key from Google.
        using var db = CreateDb();
        await SeedCalendarAsync(db, "cal-alice", defaults: [new("popup", 30)]);
        var sut = CreateSut(db, userId: "alice");

        var reminders = RemindersOf(await sut.CreateEvent("cal-alice", TimedBody(reminders: null)));

        reminders.GetProperty("useDefault").GetBoolean().Should().BeTrue();
    }

    // ── Reading a recurring series ────────────────────────────────────────────

    [Fact]
    public async Task ListEvents_OnAnExpandedInstance_ReportsTheMastersReminders()
    {
        using var db = CreateDb();
        await SeedCalendarAsync(db, "cal-alice", defaults: [new("popup", 30)]);
        await SeedSeriesMasterAsync(
            db, """{"useDefault":false,"overrides":[{"method":"popup","minutes":25}]}""");
        var sut = CreateSut(db, userId: "alice");

        var result = await sut.ListEvents(
            "cal-alice", singleEvents: true, timeMin: "2027-01-01T00:00:00Z", timeMax: "2027-02-01T00:00:00Z");

        var instance = ItemWithId(result, "evt-master_20270119T140000Z");
        MinutesOf(instance.GetProperty("reminders")).Should().Equal(25);
    }

    [Fact]
    public async Task ListEvents_OnAnExceptionOverride_ReportsItsOwnRemindersNotTheMasters()
    {
        // Editing one occurrence's reminders makes that occurrence an exception with reminders of its
        // own; its siblings still report the master's.
        using var db = CreateDb();
        await SeedCalendarAsync(db, "cal-alice", defaults: [new("popup", 30)]);
        await SeedSeriesMasterAsync(
            db, """{"useDefault":false,"overrides":[{"method":"popup","minutes":25}]}""");
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-master_20270119T140000Z",
            CalendarId = "cal-alice",
            Summary = "Weekly",
            UserId = "alice",
            StartTime = new DateTime(2027, 1, 19, 14, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2027, 1, 19, 15, 0, 0, DateTimeKind.Utc),
            RecurringEventId = "evt-master",
            OriginalStartTime = new DateTime(2027, 1, 19, 14, 0, 0, DateTimeKind.Utc),
            RemindersJson = """{"useDefault":false,"overrides":[{"method":"popup","minutes":5}]}"""
        });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, userId: "alice");

        var result = await sut.ListEvents(
            "cal-alice", singleEvents: true, timeMin: "2027-01-01T00:00:00Z", timeMax: "2027-02-01T00:00:00Z");

        MinutesOf(ItemWithId(result, "evt-master_20270119T140000Z").GetProperty("reminders"))
            .Should().Equal(5);
        MinutesOf(ItemWithId(result, "evt-master_20270126T140000Z").GetProperty("reminders"))
            .Should().Equal(new[] { 25 }, "a sibling occurrence still follows the master");
    }

    [Fact]
    public async Task ListEvents_ReportsEachAllDayEventsOwnCalendarsDefaults()
    {
        // Two calendars with different defaults, several events each: the defaults a listing reports
        // come from each event's own calendar. The projection is handed one pre-loaded lookup for the
        // whole request rather than querying per event, which a listing of a family's month would
        // otherwise turn into an N+1 against a double the E2E suite waits on.
        using var db = CreateDb();
        await SeedCalendarAsync(db, "cal-alice", defaults: [new("popup", 30)]);
        await SeedCalendarAsync(db, "cal-other", defaults: [new("email", 120)]);
        await SeedEventAsync(db, "evt-1", remindersJson: null, isAllDay: true);
        await SeedEventAsync(db, "evt-2", remindersJson: null, isAllDay: true);
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-3",
            CalendarId = "cal-other",
            Summary = "Elsewhere",
            UserId = "alice",
            StartTime = new DateTime(2027, 1, 12, 0, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2027, 1, 13, 0, 0, 0, DateTimeKind.Utc),
            IsAllDay = true
        });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, userId: "alice");

        var result = await sut.ListEvents("cal-alice");

        MinutesOf(ItemWithId(result, "evt-1").GetProperty("reminders")).Should().Equal(30);
        MinutesOf(ItemWithId(result, "evt-2").GetProperty("reminders")).Should().Equal(30);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The <c>reminders</c> object a single-event response reported.</summary>
    private static JsonElement RemindersOf(IActionResult result)
    {
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return JsonSerializer.SerializeToElement(ok.Value).GetProperty("reminders");
    }

    private static JsonElement ItemWithId(IActionResult result, string id)
    {
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return JsonSerializer.SerializeToElement(ok.Value)
            .GetProperty("items")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == id);
    }

    private static int[] MinutesOf(JsonElement reminders) =>
        [.. reminders.GetProperty("overrides").EnumerateArray().Select(o => o.GetProperty("minutes").GetInt32())];

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

    private static async Task SeedEventAsync(
        SimContext db, string id, string? remindersJson, bool isAllDay = false)
    {
        db.Events.Add(new SimulatedEvent
        {
            Id = id,
            CalendarId = "cal-alice",
            Summary = "Reminder Event",
            UserId = "alice",
            StartTime = new DateTime(2027, 1, 12, 14, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2027, 1, 12, 15, 0, 0, DateTimeKind.Utc),
            IsAllDay = isAllDay,
            RemindersJson = remindersJson
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCalendarAsync(
        SimContext db, string id, List<GoogleEventReminderOverride>? defaults)
    {
        db.Calendars.Add(new SimulatedCalendar
        {
            Id = id,
            Summary = id,
            UserId = "alice",
            DefaultRemindersJson = defaults is null ? null : JsonSerializer.Serialize(defaults)
        });
        await db.SaveChangesAsync();
    }

    /// <summary>A three-occurrence weekly series starting 2027-01-12T14:00Z.</summary>
    private static async Task SeedSeriesMasterAsync(SimContext db, string? remindersJson)
    {
        db.Events.Add(new SimulatedEvent
        {
            Id = "evt-master",
            CalendarId = "cal-alice",
            Summary = "Weekly",
            UserId = "alice",
            StartTime = new DateTime(2027, 1, 12, 14, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2027, 1, 12, 15, 0, 0, DateTimeKind.Utc),
            StartTimeZone = "Europe/London",
            RecurrenceRule = "RRULE:FREQ=WEEKLY;COUNT=3",
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
            new FamilyHQ.Time.NodaTimeRecurrenceTimeZoneFactory());
        var httpContext = new DefaultHttpContext();
        if (userId != null)
            httpContext.Request.Headers.Authorization = $"Bearer simulated_{userId}_abc123nonce";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }
}
