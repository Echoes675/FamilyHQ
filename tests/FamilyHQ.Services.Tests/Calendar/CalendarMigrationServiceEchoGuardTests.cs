using FluentAssertions;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class CalendarMigrationServiceEchoGuardTests
{
    private static readonly Guid IndividualCalId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SharedCalId     = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EventId         = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private static CalendarInfo IndividualCal => new()
        { Id = IndividualCalId, GoogleCalendarId = "eoin@", DisplayName = "Eoin", IsShared = false };
    private static CalendarInfo SharedCal => new()
        { Id = SharedCalId, GoogleCalendarId = "shared@", DisplayName = "Shared", IsShared = true };
    private static CalendarInfo MemberBCal => new()
        { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), GoogleCalendarId = "sarah@", DisplayName = "Sarah", IsShared = false };

    private static CalendarEvent MakeEvent(string googleEventId, Guid ownerCalId) =>
        new()
        {
            Id                  = EventId,
            GoogleEventId       = googleEventId,
            Title               = "Test Event",
            Start               = DateTimeOffset.UtcNow,
            End                 = DateTimeOffset.UtcNow.AddHours(1),
            OwnerCalendarInfoId = ownerCalId,
        };

    private (Mock<IGoogleCalendarClient> google, Mock<ICalendarRepository> repo,
             Mock<IOutboundWriteHashCache> cache, CalendarMigrationService sut) CreateSut()
    {
        var google = new Mock<IGoogleCalendarClient>();
        var repo   = new Mock<ICalendarRepository>();
        var cache  = new Mock<IOutboundWriteHashCache>();
        var logger = new Mock<ILogger<CalendarMigrationService>>();

        // Wire common repo responses so tests only need to override what they care about
        repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var sut = new CalendarMigrationService(google.Object, repo.Object, cache.Object, logger.Object);
        return (google, repo, cache, sut);
    }

    [Fact]
    public async Task EnsureCorrectCalendarAsync_records_new_event_id_after_migration()
    {
        var (google, repo, cache, sut) = CreateSut();
        var evt = MakeEvent("old-google-id", IndividualCalId);

        repo.Setup(r => r.GetCalendarByIdAsync(IndividualCalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IndividualCal);
        repo.Setup(r => r.GetSharedCalendarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SharedCal);
        google.Setup(g => g.CreateEventAsync("shared@", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) =>
                  { e.GoogleEventId = "new-google-id"; return e; });
        google.Setup(g => g.DeleteEventAsync("eoin@", "old-google-id", It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var result = await sut.EnsureCorrectCalendarAsync(evt, [IndividualCal, MemberBCal]);

        result.Should().BeTrue();
        cache.Verify(c => c.Record("new-google-id", It.Is<string>(h => !string.IsNullOrEmpty(h))), Times.Once);
        cache.Verify(c => c.Record("old-google-id", It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnsureCorrectCalendarAsync_does_not_record_if_create_new_fails()
    {
        var (google, repo, cache, sut) = CreateSut();
        var evt = MakeEvent("old-google-id", IndividualCalId);

        repo.Setup(r => r.GetCalendarByIdAsync(IndividualCalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IndividualCal);
        repo.Setup(r => r.GetSharedCalendarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SharedCal);
        google.Setup(g => g.CreateEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("Google rejected create"));

        var act = () => sut.EnsureCorrectCalendarAsync(evt, [IndividualCal, MemberBCal]);

        await act.Should().ThrowAsync<InvalidOperationException>();
        cache.Verify(c => c.Record(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnsureCorrectCalendarAsync_records_new_event_id_even_if_delete_old_fails()
    {
        // The new event already exists in Google and will produce an echo webhook.
        // Record must happen before the delete attempt so the guard works regardless.
        var (google, repo, cache, sut) = CreateSut();
        var evt = MakeEvent("old-google-id", IndividualCalId);

        repo.Setup(r => r.GetCalendarByIdAsync(IndividualCalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IndividualCal);
        repo.Setup(r => r.GetSharedCalendarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SharedCal);
        google.Setup(g => g.CreateEventAsync("shared@", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) =>
                  { e.GoogleEventId = "new-google-id"; return e; });
        google.Setup(g => g.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("delete failed"));

        var act = () => sut.EnsureCorrectCalendarAsync(evt, [IndividualCal, MemberBCal]);
        await act.Should().ThrowAsync<InvalidOperationException>();

        cache.Verify(c => c.Record("new-google-id", It.Is<string>(h => !string.IsNullOrEmpty(h))), Times.Once);
    }

    [Fact]
    public async Task EnsureCorrectCalendarAsync_does_not_record_when_no_migration_needed()
    {
        // Already on the correct calendar — early return, Record should never be called.
        var (google, repo, cache, sut) = CreateSut();
        var evt = MakeEvent("gid1", IndividualCalId);

        repo.Setup(r => r.GetCalendarByIdAsync(IndividualCalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IndividualCal);

        var result = await sut.EnsureCorrectCalendarAsync(evt, [IndividualCal]);

        result.Should().BeFalse();
        cache.Verify(c => c.Record(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
