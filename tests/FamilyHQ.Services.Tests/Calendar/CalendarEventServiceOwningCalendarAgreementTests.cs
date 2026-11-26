using FamilyHQ.Core.Calendar;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Time;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// Pins <c>CalendarEventService.CreateAsync</c>'s routing to <see cref="OwningCalendarRule"/>, the
/// one written statement of which Google calendar an event lands on.
/// <para>
/// The event modal has to predict that calendar for an event it has not saved yet, because default
/// reminders belong to a calendar and the Reminders tab cannot describe — or pre-fill — them until it
/// knows which one. The server's create path decides it for real. Those two agreed by inspection
/// only, so a change to the server's routing would have shipped silently and surfaced as the tab
/// offering the wrong defaults, which reads as a reminders bug rather than a routing one.
/// </para>
/// <para>
/// <b>Why this test and not a shared call.</b> Having <c>CreateAsync</c> call the Core rule directly
/// would be the better end state, but it is the path that decides which Google calendar an event is
/// written to — the family sees a mistake there in the Google Calendar app, on events created on
/// their phones. The rule is extracted and the server is pinned to it by assertion; moving the call
/// site is a separate, deliberate change.
/// </para>
/// <para>
/// Every expectation below comes out of <see cref="OwningCalendarRule.OwningCalendarFor"/>. None is
/// restated inline, which is the whole mechanism: a change to either side alone turns this red.
/// </para>
/// </summary>
public class CalendarEventServiceOwningCalendarAgreementTests
{
    private static readonly Guid CalAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CalBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SharedId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    /// <remarks>
    /// The two selection shapes the server accepts. A memberless create never reaches
    /// <c>CreateAsync</c> — <c>CreateEventRequestValidator</c> rejects it with "At least one calendar
    /// is required" — so there is no third case to agree about here.
    /// </remarks>
    public static TheoryData<string, Guid[]> AcceptedSelections() => new()
    {
        { "exactly one member", [CalAId] },
        { "more than one member", [CalAId, CalBId] }
    };

    [Theory]
    [MemberData(nameof(AcceptedSelections))]
    public async Task CreateAsync_PutsTheEventOnTheCalendarTheSharedRuleNames(
        string selectionShape, Guid[] selectedCalendarIds)
    {
        var calendars = Calendars();
        var (google, sut) = CreateSut(calendars);
        var writtenGoogleCalendarId = CaptureTheCalendarWrittenTo(google);

        var created = await sut.CreateAsync(Request(selectedCalendarIds));

        var expected = OwningCalendarRule.OwningCalendarFor(selectedCalendarIds, Candidates(calendars));
        expected.Should().NotBeNull("both selection shapes the server accepts have an answer");
        created.OwnerCalendarInfoId.Should().Be(
            expected.Value,
            "a create naming {0} must land on the calendar the shared owning-calendar rule names",
            selectionShape);
        // The owning id is what the modal reads back, but the routing's real effect is which Google
        // calendar the event is written to. Asserting both means neither can move on its own.
        writtenGoogleCalendarId().Should().Be(calendars.Single(c => c.Id == expected.Value).GoogleCalendarId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<CalendarInfo> Calendars() =>
    [
        Cal(CalAId, "cal-a@google.com", "Alice"),
        Cal(CalBId, "cal-b@google.com", "Bob"),
        Cal(SharedId, "shared@google.com", "Family", isShared: true)
    ];

    /// <summary>
    /// The same calendars, reduced to what the rule is allowed to read. This is a projection, not a
    /// second opinion: it drops fields, it does not choose between calendars.
    /// </summary>
    private static IReadOnlyCollection<OwningCalendarCandidate> Candidates(
        IEnumerable<CalendarInfo> calendars) =>
        [.. calendars.Select(c => new OwningCalendarCandidate(c.Id, c.IsShared))];

    private static CalendarInfo Cal(Guid id, string googleId, string displayName, bool isShared = false) =>
        new() { Id = id, GoogleCalendarId = googleId, DisplayName = displayName, IsShared = isShared };

    private static CreateEventRequest Request(IReadOnlyList<Guid> selectedCalendarIds) =>
        new(selectedCalendarIds, "Title",
            new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero),
            false, null, null);

    /// <summary>
    /// Accepts a write to any calendar and reports which one it was, so that a server picking the
    /// wrong calendar fails on the assertion rather than on an unmatched mock setup.
    /// </summary>
    private static Func<string?> CaptureTheCalendarWrittenTo(Mock<IGoogleCalendarClient> google)
    {
        string? written = null;
        google.Setup(g => g.CreateEventAsync(
                It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<EventReminders?>()))
            .ReturnsAsync((string googleCalendarId, CalendarEvent e, string _, CancellationToken _, EventReminders? _) =>
            {
                written = googleCalendarId;
                e.GoogleEventId = "new-gid";
                return e;
            });
        return () => written;
    }

    private static (Mock<IGoogleCalendarClient> google, CalendarEventService sut)
        CreateSut(IReadOnlyList<CalendarInfo> calendars)
    {
        var google = new Mock<IGoogleCalendarClient>();
        var repo = new Mock<ICalendarRepository>();
        var tagParser = new Mock<IMemberTagParser>();
        var currentUser = new Mock<ICurrentUserService>();

        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(calendars);
        // Mirrors what CalendarRepository.GetSharedCalendarAsync does — the shared calendar of the
        // list above. Substituting the repository is not restating the routing rule: the decision
        // under test is the server's single-vs-multi branch, which chooses whether to ask this at all.
        repo.Setup(r => r.GetSharedCalendarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendars.FirstOrDefault(c => c.IsShared));
        repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        tagParser.Setup(p => p.NormaliseDescription(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns((string d, IReadOnlyList<string> _) => d ?? string.Empty);

        currentUser.SetupGet(u => u.UserId).Returns("u-1");

        var sut = new CalendarEventService(
            google.Object, repo.Object, new Mock<ICalendarMigrationService>().Object, tagParser.Object,
            new Mock<IOutboundWriteHashCache>().Object, currentUser.Object,
            new NodaTimeRecurrenceTimeZoneFactory(),
            new Mock<ILogger<CalendarEventService>>().Object);
        return (google, sut);
    }
}
