using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Exceptions;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class CalendarEventServiceTests
{
    private static readonly Guid CalAId   = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CalBId   = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SharedId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid EventId  = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_SingleMember_CreatesOnIndividualCalendar()
    {
        var (google, repo, migration, tagParser, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");

        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([calA]);
        google.Setup(g => g.CreateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _, EventReminders? _) =>
                { e.GoogleEventId = "new-gid"; return e; });
        repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var request = new CreateEventRequest(
            [CalAId], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        var result = await sut.CreateAsync(request);

        google.Verify(g => g.CreateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        result.GoogleEventId.Should().Be("new-gid");
        result.OwnerCalendarInfoId.Should().Be(CalAId);
    }

    [Fact]
    public async Task CreateAsync_TwoMembers_CreatesOnSharedCalendar()
    {
        var (google, repo, migration, tagParser, sut) = CreateSut();
        var calA   = Cal(CalAId, "cal-a@google.com", "Alice");
        var calB   = Cal(CalBId, "cal-b@google.com", "Bob");
        var shared = Cal(SharedId, "shared@google.com", "Family", isShared: true);

        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([calA, calB, shared]);
        repo.Setup(r => r.GetSharedCalendarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(shared);
        google.Setup(g => g.CreateEventAsync("shared@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _, EventReminders? _) =>
                { e.GoogleEventId = "shared-gid"; return e; });
        repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var request = new CreateEventRequest(
            [CalAId, CalBId], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        var result = await sut.CreateAsync(request);

        google.Verify(g => g.CreateEventAsync("shared@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        result.OwnerCalendarInfoId.Should().Be(SharedId);
    }

    [Fact]
    public async Task CreateAsync_UnknownCalendarId_Throws()
    {
        var (google, repo, migration, tagParser, sut) = CreateSut();
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([Cal(CalAId, "cal-a@google.com", "Alice")]);

        var request = new CreateEventRequest(
            [CalBId], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

        await sut.Invoking(s => s.CreateAsync(request))
            .Should().ThrowAsync<UnknownCalendarException>();
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_UpdatesGoogleAndDb()
    {
        var (google, repo, migration, tagParser, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");
        var evt  = Event(EventId, "old-gid", CalAId, calA);

        repo.Setup(r => r.GetEventAsync(EventId, "u-1", It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.PatchEventFieldsAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _, EventReminders? _) => e);
        repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var request = new UpdateEventRequest("New Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        var result = await sut.UpdateAsync(EventId, request);

        google.Verify(g => g.PatchEventFieldsAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        result.Title.Should().Be("New Title");
    }

    // ── SetMembersAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task SetMembersAsync_EmptyList_ThrowsNoMembersException()
    {
        var (google, repo, migration, tagParser, sut) = CreateSut();

        await sut.Invoking(s => s.SetMembersAsync(EventId, Array.Empty<Guid>()))
            .Should().ThrowAsync<NoMembersException>();
    }

    [Fact]
    public async Task SetMembersAsync_NoMigrationNeeded_UpdatesGoogleAndDb()
    {
        var (google, repo, migration, tagParser, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");
        var evt  = Event(EventId, "gid-1", CalAId, calA);

        repo.Setup(r => r.GetEventAsync(EventId, "u-1", It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        migration.Setup(m => m.EnsureCorrectCalendarAsync(It.IsAny<CalendarEvent>(), It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        google.Setup(g => g.PatchEventFieldsAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _, EventReminders? _) => e);
        repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await sut.SetMembersAsync(EventId, [CalAId]);

        google.Verify(g => g.PatchEventFieldsAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetMembersAsync_MigrationPerformed_SkipsGoogleUpdate()
    {
        var (google, repo, migration, tagParser, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");
        var calB = Cal(CalBId, "cal-b@google.com", "Bob");
        var evt  = Event(EventId, "gid-1", CalAId, calA);

        repo.Setup(r => r.GetEventAsync(EventId, "u-1", It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA, calB]);
        migration.Setup(m => m.EnsureCorrectCalendarAsync(It.IsAny<CalendarEvent>(), It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await sut.SetMembersAsync(EventId, [CalAId, CalBId]);

        google.Verify(g => g.PatchEventFieldsAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_DeletesFromGoogleAndDb()
    {
        var (google, repo, migration, tagParser, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");
        var evt  = Event(EventId, "gid-1", CalAId, calA);

        repo.Setup(r => r.GetEventAsync(EventId, "u-1", It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.DeleteEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await sut.DeleteAsync(EventId);

        google.Verify(g => g.DeleteEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── FHQ-214: the delete is idempotent against a concurrent inbound sync ────
    //
    // Google's push for the DELETE we have just made arrives within seconds, so a targeted sync can
    // remove the same local row before our own SaveChanges commits. EF then reports zero rows
    // affected on a DELETE it expected to affect one and raises DbUpdateConcurrencyException. The
    // delete succeeded end to end — the event is gone from Google and gone from the database — so
    // the only thing that failed was the bookkeeping of an already-removed row, and the family saw
    // an HTTP 500 with the modal stuck open on an event that no longer existed.

    [Fact]
    public async Task DeleteAsync_RowAlreadyRemovedByConcurrentSync_Completes()
    {
        var logger = new RecordingLogger<CalendarEventService>();
        var (google, repo, _, _, sut) = CreateSut(logger);
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");
        var evt  = Event(EventId, "gid-1", CalAId, calA);

        repo.Setup(r => r.GetEventAsync(EventId, "u-1", It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException(
                "The database operation was expected to affect 1 row(s), but actually affected 0 row(s)"));
        // The confirming re-read finds the row genuinely gone: the requested end state holds.
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync((CalendarEvent?)null);

        await sut.Invoking(s => s.DeleteAsync(EventId)).Should().NotThrowAsync();

        // Exactly one Google DELETE: the race is resolved locally, never by re-issuing the write.
        google.Verify(g => g.DeleteEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()), Times.Once);
        // No retry of the save either — the row being absent is already the desired end state.
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_RowAlreadyRemovedByConcurrentSync_LogsTheRaceAsAnExpectedOutcome()
    {
        var logger = new RecordingLogger<CalendarEventService>();
        var (_, repo, _, _, sut) = CreateSut(logger);
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");
        var evt  = Event(EventId, "gid-1", CalAId, calA);

        repo.Setup(r => r.GetEventAsync(EventId, "u-1", It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("expected to affect 1 row(s), but actually affected 0 row(s)"));
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync((CalendarEvent?)null);

        await sut.DeleteAsync(EventId);

        // Two writers agreeing is an expected outcome, not a fault: nothing above Information.
        logger.Records.Should().NotContain(r =>
            r.Level == LogLevel.Warning || r.Level == LogLevel.Error || r.Level == LogLevel.Critical);
        logger.Records.Should().Contain(r => r.Level == LogLevel.Information && r.Message.Contains("already gone"));
    }

    [Fact]
    public async Task DeleteAsync_ConcurrencyFailureWithRowStillPresent_Rethrows()
    {
        var logger = new RecordingLogger<CalendarEventService>();
        var (google, repo, _, _, sut) = CreateSut(logger);
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");
        var evt  = Event(EventId, "gid-1", CalAId, calA);

        repo.Setup(r => r.GetEventAsync(EventId, "u-1", It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("a real conflict on something else"));
        // The row is still there, so this is NOT the benign race — it must not be swallowed.
        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);

        await sut.Invoking(s => s.DeleteAsync(EventId))
            .Should().ThrowAsync<DbUpdateConcurrencyException>();

        // Even across the bounded re-drive, Google is told exactly once.
        google.Verify(g => g.DeleteEventAsync("cal-a@google.com", "gid-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── IDOR guard ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WithOtherUsersEventId_ThrowsEventNotFoundException()
    {
        var (_, repo, _, _, sut) = CreateSut();
        repo.Setup(r => r.GetEventAsync(EventId, "u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        var request = new UpdateEventRequest("T", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            false, null, null);
        await sut.Invoking(s => s.UpdateAsync(EventId, request))
                 .Should().ThrowAsync<EventNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_WithOtherUsersEventId_ThrowsEventNotFoundException()
    {
        var (_, repo, _, _, sut) = CreateSut();
        repo.Setup(r => r.GetEventAsync(EventId, "u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        await sut.Invoking(s => s.DeleteAsync(EventId))
                 .Should().ThrowAsync<EventNotFoundException>();
    }

    [Fact]
    public async Task SetMembersAsync_WithOtherUsersEventId_ThrowsEventNotFoundException()
    {
        var (_, repo, _, _, sut) = CreateSut();
        repo.Setup(r => r.GetEventAsync(EventId, "u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        await sut.Invoking(s => s.SetMembersAsync(EventId, [CalAId]))
                 .Should().ThrowAsync<EventNotFoundException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CalendarInfo Cal(Guid id, string googleId, string displayName, bool isShared = false) =>
        new() { Id = id, GoogleCalendarId = googleId, DisplayName = displayName, IsShared = isShared };

    private static CalendarEvent Event(Guid id, string googleId, Guid ownerCalId, params CalendarInfo[] members) =>
        new()
        {
            Id                  = id,
            GoogleEventId       = googleId,
            Title               = "Test Event",
            Start               = DateTimeOffset.UtcNow,
            End                 = DateTimeOffset.UtcNow.AddHours(1),
            OwnerCalendarInfoId = ownerCalId,
            Members             = members.ToList()
        };

    private static (Mock<IGoogleCalendarClient> google, Mock<ICalendarRepository> repo,
        Mock<ICalendarMigrationService> migration, Mock<IMemberTagParser> tagParser,
        CalendarEventService sut) CreateSut(ILogger<CalendarEventService>? logger = null)
    {
        var google      = new Mock<IGoogleCalendarClient>();
        var repo        = new Mock<ICalendarRepository>();
        var migration   = new Mock<ICalendarMigrationService>();
        var tagParser   = new Mock<IMemberTagParser>();
        var cache       = new Mock<IOutboundWriteHashCache>();
        var currentUser = new Mock<ICurrentUserService>();
        logger ??= new Mock<ILogger<CalendarEventService>>().Object;

        currentUser.SetupGet(u => u.UserId).Returns("u-1");

        // Default: tag parser returns normalised description unchanged
        tagParser.Setup(p => p.NormaliseDescription(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                 .Returns((string d, IReadOnlyList<string> _) => d ?? string.Empty);
        tagParser.Setup(p => p.StripMemberTag(It.IsAny<string>()))
                 .Returns((string d) => d ?? string.Empty);

        var sut = new CalendarEventService(
            google.Object, repo.Object, migration.Object, tagParser.Object, cache.Object, currentUser.Object,
            new NodaTimeRecurrenceTimeZoneFactory(), logger);
        return (google, repo, migration, tagParser, sut);
    }
}
