using System.Text.Json;
using FamilyHQ.Simulator.Controllers;
using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.DTOs;
using FamilyHQ.Simulator.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FamilyHQ.Simulator.Tests.Controllers;

/// <summary>
/// FHQ-207: the backdoor that lets an E2E scenario change a calendar's Google-side default
/// reminders mid-run. Without a CHANGE, RefreshCalendarDefaultsAsync early-returns and the
/// FHQ-205 write path is never exercised in CI.
/// </summary>
public class BackdoorCalendarsControllerTests
{
    private const string CalendarId = "cal-family-events";

    private static SimContext CreateDb() =>
        new(new DbContextOptionsBuilder<SimContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static void SeedCalendar(SimContext db, string? remindersJson)
    {
        db.Calendars.Add(new SimulatedCalendar
        {
            Id = CalendarId,
            Summary = "Family Events",
            UserId = "user-1",
            DefaultRemindersJson = remindersJson
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task SetDefaultReminders_WithOverrides_WritesTheOverridesAsGoogleShapedJson()
    {
        // Arrange
        using var db = CreateDb();
        SeedCalendar(db, null);
        var sut = new BackdoorCalendarsController(db);

        // Act
        var result = await sut.SetDefaultReminders(
            CalendarId,
            new SetCalendarDefaultRemindersRequest([new GoogleEventReminderOverride("popup", 45)]),
            CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();

        var stored = db.Calendars.Single(c => c.Id == CalendarId).DefaultRemindersJson;
        stored.Should().NotBeNull();
        JsonSerializer.Deserialize<List<GoogleEventReminderOverride>>(stored!)
            .Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new GoogleEventReminderOverride("popup", 45));
    }

    [Fact]
    public async Task SetDefaultReminders_WithNullOverrides_ClearsTheStoredValue()
    {
        // "Google reports no defaults for this calendar" — the state every E2E calendar starts in,
        // and the left-hand side of production's null -> value transition.

        // Arrange
        using var db = CreateDb();
        SeedCalendar(db, JsonSerializer.Serialize(
            new List<GoogleEventReminderOverride> { new("popup", 30) }));
        var sut = new BackdoorCalendarsController(db);

        // Act
        await sut.SetDefaultReminders(
            CalendarId, new SetCalendarDefaultRemindersRequest(null), CancellationToken.None);

        // Assert
        db.Calendars.Single(c => c.Id == CalendarId).DefaultRemindersJson.Should().BeNull();
    }

    [Fact]
    public async Task SetDefaultReminders_ForAnUnknownCalendar_Returns404()
    {
        // A scenario that mistypes or hard-codes an id must fail loudly at the backdoor call,
        // not silently do nothing and then fail 30s later as an unrelated timeout.

        // Arrange
        using var db = CreateDb();
        var sut = new BackdoorCalendarsController(db);

        // Act
        var result = await sut.SetDefaultReminders(
            "no-such-calendar",
            new SetCalendarDefaultRemindersRequest([new GoogleEventReminderOverride("popup", 10)]),
            CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
