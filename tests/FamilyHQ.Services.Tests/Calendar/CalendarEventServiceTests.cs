using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
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
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) =>
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
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) =>
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
            .Should().ThrowAsync<ArgumentException>();
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_UpdatesGoogleAndDb()
    {
        var (google, repo, migration, tagParser, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");
        var evt  = Event(EventId, "old-gid", CalAId, calA);

        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.UpdateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) => e);
        repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var request = new UpdateEventRequest("New Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        var result = await sut.UpdateAsync(EventId, request);

        google.Verify(g => g.UpdateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        result.Title.Should().Be("New Title");
    }

    // ── SetMembersAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task SetMembersAsync_EmptyList_ThrowsArgumentException()
    {
        var (google, repo, migration, tagParser, sut) = CreateSut();

        await sut.Invoking(s => s.SetMembersAsync(EventId, Array.Empty<Guid>()))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SetMembersAsync_NoMigrationNeeded_UpdatesGoogleAndDb()
    {
        var (google, repo, migration, tagParser, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");
        var evt  = Event(EventId, "gid-1", CalAId, calA);

        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        migration.Setup(m => m.EnsureCorrectCalendarAsync(It.IsAny<CalendarEvent>(), It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        google.Setup(g => g.UpdateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) => e);
        repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await sut.SetMembersAsync(EventId, [CalAId]);

        google.Verify(g => g.UpdateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
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

        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA, calB]);
        migration.Setup(m => m.EnsureCorrectCalendarAsync(It.IsAny<CalendarEvent>(), It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await sut.SetMembersAsync(EventId, [CalAId, CalBId]);

        google.Verify(g => g.UpdateEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_DeletesFromGoogleAndDb()
    {
        var (google, repo, migration, tagParser, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");
        var evt  = Event(EventId, "gid-1", CalAId, calA);

        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
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
        CalendarEventService sut) CreateSut()
    {
        var google    = new Mock<IGoogleCalendarClient>();
        var repo      = new Mock<ICalendarRepository>();
        var migration = new Mock<ICalendarMigrationService>();
        var tagParser = new Mock<IMemberTagParser>();
        var logger    = new Mock<ILogger<CalendarEventService>>();

        // Default: tag parser returns normalised description unchanged
        tagParser.Setup(p => p.NormaliseDescription(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                 .Returns((string d, IReadOnlyList<string> _) => d ?? string.Empty);
        tagParser.Setup(p => p.StripMemberTag(It.IsAny<string>()))
                 .Returns((string d) => d ?? string.Empty);

        var sut = new CalendarEventService(google.Object, repo.Object, migration.Object, tagParser.Object, logger.Object);
        return (google, repo, migration, tagParser, sut);
    }
}
