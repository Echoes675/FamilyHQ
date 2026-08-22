using FamilyHQ.Simulator.Controllers;
using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.Models;
using FamilyHQ.Simulator.State;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace FamilyHQ.Simulator.Tests.Controllers;

public class CalendarsControllerTests
{
    [Fact]
    public async Task GetCalendarList_ReturnsOnlyCalendarsForAuthenticatedUser()
    {
        // Arrange
        using var db = CreateDb();
        db.Calendars.AddRange(
            new SimulatedCalendar { Id = "cal-alice", Summary = "Alice Cal", UserId = "alice" },
            new SimulatedCalendar { Id = "cal-bob", Summary = "Bob Cal", UserId = "bob" });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        // Act
        var result = await sut.GetCalendarList();

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("cal-alice");
        json.Should().NotContain("cal-bob");
    }

    [Fact]
    public async Task GetCalendarList_WithDifferentUser_ReturnsOnlyThatUsersCalendars()
    {
        // Arrange
        using var db = CreateDb();
        db.Calendars.AddRange(
            new SimulatedCalendar { Id = "cal-alice", Summary = "Alice Cal", UserId = "alice" },
            new SimulatedCalendar { Id = "cal-bob-1", Summary = "Bob Cal 1", UserId = "bob" },
            new SimulatedCalendar { Id = "cal-bob-2", Summary = "Bob Cal 2", UserId = "bob" });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "bob");

        // Act
        var result = await sut.GetCalendarList();

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("cal-bob-1");
        json.Should().Contain("cal-bob-2");
        json.Should().NotContain("cal-alice");
    }

    [Fact]
    public async Task GetCalendarList_ReportsTheCalendarsOwnDefaultTimeZone()
    {
        // FHQ-164: Google returns the calendar resource's `timeZone` on every calendarList entry, and
        // the app stores it as the last Google-supplied rung of its series-zone discovery ladder. A
        // Simulator that never reports one leaves that rung unexercised in every environment that
        // runs against it.
        using var db = CreateDb();
        db.Calendars.Add(new SimulatedCalendar
        {
            Id = "cal-alice", Summary = "Alice Cal", UserId = "alice", TimeZone = "Europe/London"
        });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, userId: "alice");

        var result = await sut.GetCalendarList();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("\"timeZone\":\"Europe/London\"");
    }

    [Fact]
    public async Task GetCalendarList_WhenNoTokenPresent_ReturnsEmptyList()
    {
        // Arrange
        using var db = CreateDb();
        db.Calendars.Add(new SimulatedCalendar { Id = "cal-alice", Summary = "Alice Cal", UserId = "alice" });
        await db.SaveChangesAsync();

        // No Authorization header
        var sut = CreateSut(db, bearerToken: null);

        // Act
        var result = await sut.GetCalendarList();

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("\"items\":[]");
    }

    [Fact]
    public async Task GetCalendarList_WhenRefreshTokenInvalidGrantInjected_Returns401()
    {
        // Arrange — FHQ-82: a revoked refresh token must also 401 the Calendar API, mirroring real
        // Google (a revoked OAuth grant invalidates the access token too, not just /token). Without
        // this, a cached access token masks the revocation and reauth is never detected.
        using var db = CreateDb();
        db.Calendars.Add(new SimulatedCalendar { Id = "cal-alice", Summary = "Alice Cal", UserId = "alice" });
        await db.SaveChangesAsync();

        var failureStore = new SyncFailureModeStore();
        failureStore.Set("alice", SyncFailureMode.RefreshTokenInvalidGrant);
        var sut = CreateSut(db, userId: "alice", failureStore: failureStore);

        // Act
        var result = await sut.GetCalendarList();

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task GetCalendarList_WhenCalendarApi403Injected_Returns403()
    {
        // Regression — the pre-existing CalendarApi403 injection mode must be unaffected by the
        // RefreshTokenInvalidGrant → 401 change above.
        using var db = CreateDb();
        db.Calendars.Add(new SimulatedCalendar { Id = "cal-alice", Summary = "Alice Cal", UserId = "alice" });
        await db.SaveChangesAsync();

        var failureStore = new SyncFailureModeStore();
        failureStore.Set("alice", SyncFailureMode.CalendarApi403);
        var sut = CreateSut(db, userId: "alice", failureStore: failureStore);

        // Act
        var result = await sut.GetCalendarList();

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
    }

    private static SimContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SimContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimContext(options);
    }

    private static CalendarsController CreateSut(
        SimContext db, string? userId = null, string? bearerToken = "auto", SyncFailureModeStore? failureStore = null)
    {
        var controller = new CalendarsController(db, failureStore ?? new SyncFailureModeStore());
        var httpContext = new DefaultHttpContext();

        if (bearerToken == "auto" && userId != null)
            httpContext.Request.Headers.Authorization = $"Bearer simulated_{userId}_abc123nonce";
        else if (bearerToken != null)
            httpContext.Request.Headers.Authorization = bearerToken;

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }
}
