using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class CalendarEventServiceEchoGuardTests
{
    private static readonly Guid CalAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    // ── CreateAsync echo-guard ────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_records_outbound_hash_after_Google_succeeds()
    {
        var (google, repo, _, _, cache, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");

        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([calA]);
        google.Setup(g => g.CreateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) =>
                { e.GoogleEventId = "google-evt-id-123"; return e; });
        repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var request = new CreateEventRequest(
            [CalAId], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        await sut.CreateAsync(request, CancellationToken.None);

        cache.Verify(
            c => c.Record("google-evt-id-123", It.Is<string>(h => !string.IsNullOrEmpty(h))),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_does_not_record_if_Google_throws()
    {
        var (google, repo, _, _, cache, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");

        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([calA]);
        google.Setup(g => g.CreateEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Google says no"));

        var request = new CreateEventRequest(
            [CalAId], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

        var act = () => sut.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        cache.Verify(c => c.Record(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ── UpdateAsync echo-guard ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_records_outbound_hash_after_Google_succeeds()
    {
        var (google, repo, _, _, cache, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");
        var evt = Event(EventId, "google-evt-id-456", CalAId, calA);

        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.UpdateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) => e);
        repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var request = new UpdateEventRequest("New Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        await sut.UpdateAsync(EventId, request, CancellationToken.None);

        cache.Verify(
            c => c.Record("google-evt-id-456", It.Is<string>(h => !string.IsNullOrEmpty(h))),
            Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_does_not_record_if_Google_throws()
    {
        var (google, repo, _, _, cache, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");
        var evt = Event(EventId, "google-evt-id-456", CalAId, calA);

        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        google.Setup(g => g.UpdateEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var request = new UpdateEventRequest("New Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);
        var act = () => sut.UpdateAsync(EventId, request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        cache.Verify(c => c.Record(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ── SetMembersAsync echo-guard ────────────────────────────────────────────

    [Fact]
    public async Task SetMembersAsync_records_outbound_hash_after_description_rewrite()
    {
        var (google, repo, migration, _, cache, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");
        var evt = Event(EventId, "google-evt-id-789", CalAId, calA);

        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        migration.Setup(m => m.EnsureCorrectCalendarAsync(It.IsAny<CalendarEvent>(), It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        google.Setup(g => g.UpdateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) => e);
        repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        await sut.SetMembersAsync(EventId, new[] { CalAId }, CancellationToken.None);

        cache.Verify(
            c => c.Record("google-evt-id-789", It.Is<string>(h => !string.IsNullOrEmpty(h))),
            Times.Once);
    }

    [Fact]
    public async Task SetMembersAsync_does_not_record_if_Google_throws()
    {
        var (google, repo, migration, _, cache, sut) = CreateSut();
        var calA = Cal(CalAId, "cal-a@google.com", "Alice");
        var evt = Event(EventId, "google-evt-id-789", CalAId, calA);

        repo.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calA]);
        migration.Setup(m => m.EnsureCorrectCalendarAsync(It.IsAny<CalendarEvent>(), It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        google.Setup(g => g.UpdateEventAsync("cal-a@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var act = () => sut.SetMembersAsync(EventId, new[] { CalAId }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        cache.Verify(c => c.Record(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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
        Mock<IOutboundWriteHashCache> cache,
        CalendarEventService sut) CreateSut()
    {
        var google    = new Mock<IGoogleCalendarClient>();
        var repo      = new Mock<ICalendarRepository>();
        var migration = new Mock<ICalendarMigrationService>();
        var tagParser = new Mock<IMemberTagParser>();
        var cache     = new Mock<IOutboundWriteHashCache>();
        var logger    = new Mock<ILogger<CalendarEventService>>();

        tagParser.Setup(p => p.NormaliseDescription(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                 .Returns((string d, IReadOnlyList<string> _) => d ?? string.Empty);
        tagParser.Setup(p => p.StripMemberTag(It.IsAny<string>()))
                 .Returns((string d) => d ?? string.Empty);

        var sut = new CalendarEventService(
            google.Object, repo.Object, migration.Object, tagParser.Object, cache.Object, logger.Object);
        return (google, repo, migration, tagParser, cache, sut);
    }
}
