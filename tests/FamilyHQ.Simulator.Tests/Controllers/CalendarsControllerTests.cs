using FamilyHQ.Simulator.Controllers;
using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.Models;
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

    private static SimContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SimContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimContext(options);
    }

    private static CalendarsController CreateSut(SimContext db, string? userId = null, string? bearerToken = "auto")
    {
        var controller = new CalendarsController(db);
        var httpContext = new DefaultHttpContext();

        if (bearerToken == "auto" && userId != null)
            httpContext.Request.Headers.Authorization = $"Bearer simulated_{userId}_abc123nonce";
        else if (bearerToken != null)
            httpContext.Request.Headers.Authorization = bearerToken;

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }
}
