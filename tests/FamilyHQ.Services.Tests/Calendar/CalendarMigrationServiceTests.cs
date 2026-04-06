using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class CalendarMigrationServiceTests
{
    private static readonly Guid IndividualCalId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SharedCalId     = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MemberBCalId    = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid EventId         = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private static CalendarInfo IndividualCal => new()
        { Id = IndividualCalId, GoogleCalendarId = "eoin@", DisplayName = "Eoin", IsShared = false };
    private static CalendarInfo SharedCal => new()
        { Id = SharedCalId, GoogleCalendarId = "shared@", DisplayName = "Shared", IsShared = true };
    private static CalendarInfo MemberBCal => new()
        { Id = MemberBCalId, GoogleCalendarId = "sarah@", DisplayName = "Sarah", IsShared = false };

    private (Mock<IGoogleCalendarClient> google, Mock<ICalendarRepository> repo, CalendarMigrationService sut) CreateSut()
    {
        var google = new Mock<IGoogleCalendarClient>();
        var repo   = new Mock<ICalendarRepository>();
        var logger = new Mock<ILogger<CalendarMigrationService>>();
        var sut    = new CalendarMigrationService(google.Object, repo.Object, logger.Object);
        return (google, repo, sut);
    }

    [Fact]
    public async Task EnsureCorrectCalendar_SingleMemberOnCorrectCalendar_NoMigration()
    {
        var (google, repo, sut) = CreateSut();
        var evt = new CalendarEvent { Id = EventId, GoogleEventId = "gid1", Title = "T",
            Start = DateTimeOffset.UtcNow, End = DateTimeOffset.UtcNow.AddHours(1),
            OwnerCalendarInfoId = IndividualCalId };

        repo.Setup(r => r.GetCalendarByIdAsync(IndividualCalId, default)).ReturnsAsync(IndividualCal);
        repo.Setup(r => r.GetSharedCalendarAsync(default)).ReturnsAsync(SharedCal);

        var migrated = await sut.EnsureCorrectCalendarAsync(evt, [IndividualCal]);

        migrated.Should().BeFalse();
        google.Verify(g => g.CreateEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task EnsureCorrectCalendar_TwoMembersOnIndividualCalendar_MigratesToShared()
    {
        var (google, repo, sut) = CreateSut();
        var evt = new CalendarEvent { Id = EventId, GoogleEventId = "gid1", Title = "T",
            Start = DateTimeOffset.UtcNow, End = DateTimeOffset.UtcNow.AddHours(1),
            Description = "note [members: Eoin, Sarah]",
            OwnerCalendarInfoId = IndividualCalId };

        repo.Setup(r => r.GetCalendarByIdAsync(IndividualCalId, default)).ReturnsAsync(IndividualCal);
        repo.Setup(r => r.GetSharedCalendarAsync(default)).ReturnsAsync(SharedCal);
        google.Setup(g => g.CreateEventAsync("shared@", It.IsAny<CalendarEvent>(), It.IsAny<string>(), default))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) =>
                { e.GoogleEventId = "gid2"; return e; });
        google.Setup(g => g.DeleteEventAsync("eoin@", "gid1", default)).Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), default)).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(0);

        var migrated = await sut.EnsureCorrectCalendarAsync(evt, [IndividualCal, MemberBCal]);

        migrated.Should().BeTrue();
        google.Verify(g => g.CreateEventAsync("shared@", It.IsAny<CalendarEvent>(), It.IsAny<string>(), default), Times.Once);
        google.Verify(g => g.DeleteEventAsync("eoin@", "gid1", default), Times.Once);
        evt.GoogleEventId.Should().Be("gid2");
        evt.OwnerCalendarInfoId.Should().Be(SharedCalId);
    }

    [Fact]
    public async Task EnsureCorrectCalendar_OneMemberOnSharedCalendar_MigratesToIndividual()
    {
        var (google, repo, sut) = CreateSut();
        var evt = new CalendarEvent { Id = EventId, GoogleEventId = "gid1", Title = "T",
            Start = DateTimeOffset.UtcNow, End = DateTimeOffset.UtcNow.AddHours(1),
            Description = "[members: Eoin]",
            OwnerCalendarInfoId = SharedCalId };

        repo.Setup(r => r.GetCalendarByIdAsync(SharedCalId, default)).ReturnsAsync(SharedCal);
        repo.Setup(r => r.GetSharedCalendarAsync(default)).ReturnsAsync(SharedCal);
        google.Setup(g => g.CreateEventAsync("eoin@", It.IsAny<CalendarEvent>(), It.IsAny<string>(), default))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) =>
                { e.GoogleEventId = "gid3"; return e; });
        google.Setup(g => g.DeleteEventAsync("shared@", "gid1", default)).Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), default)).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(0);

        var migrated = await sut.EnsureCorrectCalendarAsync(evt, [IndividualCal]);

        migrated.Should().BeTrue();
        google.Verify(g => g.CreateEventAsync("eoin@", It.IsAny<CalendarEvent>(), It.IsAny<string>(), default), Times.Once);
        google.Verify(g => g.DeleteEventAsync("shared@", "gid1", default), Times.Once);
        evt.GoogleEventId.Should().Be("gid3");
        evt.OwnerCalendarInfoId.Should().Be(IndividualCalId);
    }

    [Fact]
    public async Task EnsureCorrectCalendar_MultiMemberOnSharedCalendar_NoMigration()
    {
        var (google, repo, sut) = CreateSut();
        var evt = new CalendarEvent { Id = EventId, GoogleEventId = "gid1", Title = "T",
            Start = DateTimeOffset.UtcNow, End = DateTimeOffset.UtcNow.AddHours(1),
            OwnerCalendarInfoId = SharedCalId };

        repo.Setup(r => r.GetCalendarByIdAsync(SharedCalId, default)).ReturnsAsync(SharedCal);
        repo.Setup(r => r.GetSharedCalendarAsync(default)).ReturnsAsync(SharedCal);

        var migrated = await sut.EnsureCorrectCalendarAsync(evt, [IndividualCal, MemberBCal]);

        migrated.Should().BeFalse();
        google.Verify(g => g.CreateEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), default), Times.Never);
    }
}
