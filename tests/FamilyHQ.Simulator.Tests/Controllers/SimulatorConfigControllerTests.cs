using FamilyHQ.Simulator.Controllers;
using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FamilyHQ.Simulator.Tests.Controllers;

public class SimulatorConfigControllerTests
{
    [Fact]
    public async Task Configure_CreatesUserIfNotExisting()
    {
        // Arrange
        using var db = CreateDb();
        var sut = new SimulatorConfigController(db);
        var config = new SimulatorConfigurationModel
        {
            UserName = "test-user",
            Calendars = [],
            Events = []
        };

        // Act
        await sut.Configure(config);

        // Assert
        var user = await db.Users.FindAsync("test-user");
        user.Should().NotBeNull();
        user!.Username.Should().Be("test-user");
    }

    [Fact]
    public async Task Configure_SeedsCalendarsAndEventsWithUserName()
    {
        // Arrange
        using var db = CreateDb();
        var sut = new SimulatorConfigController(db);
        var config = new SimulatorConfigurationModel
        {
            UserName = "alice",
            Calendars = [new CalendarModel { Id = "cal-1", Summary = "Work" }],
            Events = [new EventModel { Id = "evt-1", CalendarId = "cal-1", Summary = "Meeting" }]
        };

        // Act
        await sut.Configure(config);

        // Assert
        var calendars = await db.Calendars.ToListAsync();
        calendars.Should().ContainSingle(c => c.Id == "cal-1" && c.UserId == "alice");

        var events = await db.Events.ToListAsync();
        events.Should().ContainSingle(e => e.Id == "evt-1" && e.UserId == "alice");
    }

    [Fact]
    public async Task Configure_DoesNotDeleteOtherUsersData()
    {
        // Arrange
        using var db = CreateDb();
        db.Users.Add(new SimulatedUser { Id = "other-user", Username = "other-user" });
        db.Calendars.Add(new SimulatedCalendar { Id = "other-cal", Summary = "Other", UserId = "other-user" });
        db.Events.Add(new SimulatedEvent { Id = "other-evt", CalendarId = "other-cal", Summary = "Other Event", UserId = "other-user" });
        await db.SaveChangesAsync();

        var sut = new SimulatorConfigController(db);
        var config = new SimulatorConfigurationModel
        {
            UserName = "new-user",
            Calendars = [new CalendarModel { Id = "new-cal", Summary = "New Cal" }],
            Events = []
        };

        // Act
        await sut.Configure(config);

        // Assert
        db.Calendars.Should().Contain(c => c.Id == "other-cal");
        db.Events.Should().Contain(e => e.Id == "other-evt");
    }

    [Fact]
    public async Task Configure_ReplacesExistingDataForSameUser()
    {
        // Arrange
        using var db = CreateDb();
        db.Users.Add(new SimulatedUser { Id = "alice", Username = "alice" });
        db.Calendars.Add(new SimulatedCalendar { Id = "old-cal", Summary = "Old", UserId = "alice" });
        await db.SaveChangesAsync();

        var sut = new SimulatorConfigController(db);
        var config = new SimulatorConfigurationModel
        {
            UserName = "alice",
            Calendars = [new CalendarModel { Id = "new-cal", Summary = "New" }],
            Events = []
        };

        // Act
        await sut.Configure(config);

        // Assert
        db.Calendars.Should().NotContain(c => c.Id == "old-cal");
        db.Calendars.Should().Contain(c => c.Id == "new-cal" && c.UserId == "alice");
    }

    [Fact]
    public async Task Configure_WithNullBody_ReturnsBadRequest()
    {
        // Arrange
        using var db = CreateDb();
        var sut = new SimulatorConfigController(db);

        // Act
        var result = await sut.Configure(null!);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Configure_WithExistingUser_DoesNotCreateDuplicate()
    {
        // Arrange
        using var db = CreateDb();
        db.Users.Add(new SimulatedUser { Id = "alice", Username = "alice" });
        await db.SaveChangesAsync();

        var sut = new SimulatorConfigController(db);
        var config = new SimulatorConfigurationModel { UserName = "alice", Calendars = [], Events = [] };

        // Act
        await sut.Configure(config);

        // Assert
        db.Users.Count(u => u.Id == "alice").Should().Be(1);
    }

    private static SimContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SimContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimContext(options);
    }
}
