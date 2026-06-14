using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class PlacementReconcilerTests
{
    private static readonly Guid CalAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CalBId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddDays(-30);
    private static readonly DateTimeOffset End = DateTimeOffset.UtcNow.AddDays(365);

    private static (Mock<ICalendarRepository> repo, Mock<ICalendarMigrationService> migration, PlacementReconciler sut) CreateSut()
    {
        var repo = new Mock<ICalendarRepository>();
        var migration = new Mock<ICalendarMigrationService>();
        var logger = new Mock<ILogger<PlacementReconciler>>();
        var sut = new PlacementReconciler(repo.Object, migration.Object, logger.Object);
        return (repo, migration, sut);
    }

    private static CalendarInfo CalA => new() { Id = CalAId, DisplayName = "A", IsShared = false };
    private static CalendarInfo CalB => new() { Id = CalBId, DisplayName = "B", IsShared = false };

    [Fact]
    public async Task ReconcileForUser_NonRecurringMultiMemberEvent_InvokesMigrationWithMembersAndReturnsTrue()
    {
        var (repo, migration, sut) = CreateSut();

        var evt = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            GoogleEventId = "gid1",
            Title = "Dinner",
            Start = Start.AddDays(1),
            End = Start.AddDays(1).AddHours(1),
            Members = new List<CalendarInfo> { CalA, CalB }
        };

        repo.Setup(r => r.GetEventsAsync(Start, End, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        // The reconciler re-loads each event tracked (via GetEventAsync) before migrating (FHQ-68).
        repo.Setup(r => r.GetEventAsync(evt.Id, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        migration.Setup(m => m.EnsureCorrectCalendarAsync(evt, It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var changed = await sut.ReconcileForUserAsync(Start, End);

        changed.Should().BeTrue();
        migration.Verify(m => m.EnsureCorrectCalendarAsync(
            evt,
            It.Is<IReadOnlyList<CalendarInfo>>(members => members.Count == 2
                && members.Any(c => c.Id == CalAId)
                && members.Any(c => c.Id == CalBId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReconcileForUser_RecurringSeriesInstances_InvokesSeriesMigrationExactlyOnce()
    {
        var (repo, migration, sut) = CreateSut();

        const string seriesId = "series-1";
        CalendarEvent Instance(int day) => new()
        {
            Id = Guid.NewGuid(),
            GoogleEventId = $"gid-{day}",
            Title = "Standup",
            Start = Start.AddDays(day),
            End = Start.AddDays(day).AddHours(1),
            GoogleRecurringEventId = seriesId,
            RecurrenceRule = "RRULE:FREQ=DAILY",
            Members = new List<CalendarInfo> { CalA }
        };

        repo.Setup(r => r.GetEventsAsync(Start, End, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { Instance(1), Instance(2), Instance(3) });
        migration.Setup(m => m.EnsureCorrectCalendarForSeriesAsync(seriesId, It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await sut.ReconcileForUserAsync(Start, End);

        migration.Verify(m => m.EnsureCorrectCalendarForSeriesAsync(
            seriesId, It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()), Times.Once);
        migration.Verify(m => m.EnsureCorrectCalendarAsync(
            It.IsAny<CalendarEvent>(), It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileForUser_EmptyEventList_ReturnsFalseAndNoMigrationCalls()
    {
        var (repo, migration, sut) = CreateSut();

        repo.Setup(r => r.GetEventsAsync(Start, End, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());

        var changed = await sut.ReconcileForUserAsync(Start, End);

        changed.Should().BeFalse();
        migration.Verify(m => m.EnsureCorrectCalendarAsync(
            It.IsAny<CalendarEvent>(), It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()), Times.Never);
        migration.Verify(m => m.EnsureCorrectCalendarForSeriesAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileForUser_FirstEventMigrationThrows_StillProcessesNextAndReturnsTrue()
    {
        var (repo, migration, sut) = CreateSut();

        var failing = new CalendarEvent
        {
            Id = Guid.NewGuid(), GoogleEventId = "gid-bad", Title = "Bad",
            Start = Start.AddDays(1), End = Start.AddDays(1).AddHours(1),
            Members = new List<CalendarInfo> { CalA }
        };
        var moving = new CalendarEvent
        {
            Id = Guid.NewGuid(), GoogleEventId = "gid-good", Title = "Good",
            Start = Start.AddDays(2), End = Start.AddDays(2).AddHours(1),
            Members = new List<CalendarInfo> { CalA, CalB }
        };

        repo.Setup(r => r.GetEventsAsync(Start, End, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { failing, moving });
        repo.Setup(r => r.GetEventAsync(failing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(failing);
        repo.Setup(r => r.GetEventAsync(moving.Id, It.IsAny<CancellationToken>())).ReturnsAsync(moving);
        migration.Setup(m => m.EnsureCorrectCalendarAsync(failing, It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        migration.Setup(m => m.EnsureCorrectCalendarAsync(moving, It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var changed = await sut.ReconcileForUserAsync(Start, End);

        changed.Should().BeTrue();
        migration.Verify(m => m.EnsureCorrectCalendarAsync(
            moving, It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
