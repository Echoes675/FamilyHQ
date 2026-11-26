using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Time;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// What an update says about reminders has to be readable afterwards. Google answers a write that
/// replaced an inherited reminder with none exactly as it answers one that never mentioned
/// reminders — both a 200 — so with no line of our own the two are indistinguishable once the save
/// has happened, and a report of a reminder that went missing has nothing to be checked against.
/// </summary>
/// <remarks>
/// The states are asserted rather than the offsets. The four — never-synced, inherits-default,
/// explicit and explicitly-none — are what a save decides between, and they are what has to stay
/// distinguishable; a reminder's own wording is the family's data and does not belong in a log.
/// </remarks>
public class CalendarEventServiceReminderAuditTests
{
    private static readonly Guid CalId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EventId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task UpdateAsync_RequestDoesNotTouchReminders_SaysNothingAboutThem()
    {
        var f = new Fixture();
        f.ArrangeSingleEvent(storedReminders: EventReminders.Explicit([new EventReminder("popup", 45)]));

        await f.Sut.UpdateAsync(EventId, UpdateReq(reminders: null));

        f.ReminderLines.Should().BeEmpty(
            "an edit that says nothing about reminders is the ordinary case and the one the golden "
            + "rule defaults to; logging it on every save would bury the decisions that were made");
    }

    [Fact]
    public async Task UpdateAsync_SilencesAnInheritingEvent_RecordsBothStates()
    {
        // The reported production sequence: an event following its calendar's reminders, saved as
        // carrying none of its own. A legitimate thing to ask for, and the thing nobody could
        // afterwards tell apart from a save that never touched reminders at all.
        var f = new Fixture();
        f.ArrangeSingleEvent(storedReminders: EventReminders.InheritsCalendarDefault);

        await f.Sut.UpdateAsync(EventId, UpdateReq(EventReminders.ExplicitlyNone));

        f.ReminderLines.Should().ContainSingle().Which.Should().Contain("explicitly-none")
            .And.Contain("inherits-default");
    }

    [Fact]
    public async Task UpdateAsync_WritesAnExplicitSet_RecordsHowManyWithoutTheRemindersThemselves()
    {
        var f = new Fixture();
        f.ArrangeSingleEvent(storedReminders: EventReminders.ExplicitlyNone);

        await f.Sut.UpdateAsync(
            EventId,
            UpdateReq(EventReminders.Explicit([new EventReminder("popup", 10), new EventReminder("email", 60)])));

        var line = f.ReminderLines.Should().ContainSingle().Subject;
        line.Should().Contain("explicit-with-2").And.Contain("explicitly-none");
        line.Should().NotContain("email", "a delivery method and an offset are the family's own data");
    }

    [Fact]
    public async Task UpdateAsync_EventWhoseRemindersWereNeverSynced_DoesNotCallThatNone()
    {
        // A null is "no sync has reported this event's reminders", not "this event has none". Reading
        // it as none is the mistake the whole model exists to prevent, so the audit may not make it.
        var f = new Fixture();
        f.ArrangeSingleEvent(storedReminders: null);

        await f.Sut.UpdateAsync(EventId, UpdateReq(EventReminders.InheritsCalendarDefault));

        f.ReminderLines.Should().ContainSingle().Which.Should().Contain("never-synced");
    }

    [Fact]
    public async Task UpdateAsync_RemindersWithNullOverrides_FailsBeforeAnythingReachesGoogle()
    {
        // `Overrides` is documented never-null and carries an initialiser, but it is an `init`
        // property on a type deserialised from request bodies and from an EF JSON column, and a
        // literal `"overrides": null` bypasses an initialiser. Nothing validates it away, so the
        // request does reach the service.
        //
        // What matters is not that it fails — malformed input should — but WHERE. It fails while
        // hashing the event's content, which happens before the event is patched to Google, so the
        // request cannot leave a write that succeeded behind a response that reported failure. That
        // ordering is the property worth pinning: a retry after this error cannot duplicate anything,
        // because nothing was written.
        var f = new Fixture();
        f.ArrangeSingleEvent(storedReminders: EventReminders.InheritsCalendarDefault);

        var malformed = new EventReminders { UseDefault = false, Overrides = null! };

        var act = async () => await f.Sut.UpdateAsync(EventId, UpdateReq(malformed));

        await act.Should().ThrowAsync<ArgumentNullException>();
        f.Google.Verify(
            g => g.PatchEventFieldsAsync(
                It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<EventReminders?>()),
            Times.Never,
            "the failure must come before the write, or a caller retrying it would duplicate the event");
        f.ReminderLines.Should().BeEmpty("nothing was decided about reminders, because nothing was written");
    }

    [Fact]
    public async Task UpdateAsync_ReminderWrite_RecordsTheStateTheEventHeldBeforeIt_NotGooglesAnswer()
    {
        // The client replaces the event's reminders with what Google stored, so a state read after
        // the write would report the outcome twice and lose the only thing the line is for.
        var f = new Fixture();
        f.ArrangeSingleEvent(storedReminders: EventReminders.InheritsCalendarDefault);
        f.WhatGoogleReturns = EventReminders.Explicit([new EventReminder("popup", 0)]);

        await f.Sut.UpdateAsync(EventId, UpdateReq(EventReminders.ExplicitlyNone));

        f.ReminderLines.Should().ContainSingle().Which.Should().Contain(
            "inherits-default",
            "the state before the write is what Google held when the save arrived, and nothing on the "
            + "event says so once the client has put Google's answer onto it");
    }

    private static UpdateEventRequest UpdateReq(EventReminders? reminders) =>
        new("Edited", new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero), false, "Loc", "Body",
            null, false, reminders);

    private sealed class Fixture
    {
        private readonly Mock<IGoogleCalendarClient> _google = new();

        internal Mock<IGoogleCalendarClient> Google => _google;
        private readonly Mock<ICalendarRepository> _repo = new();
        private readonly List<string> _informationLines = [];

        public readonly CalendarEventService Sut;

        /// <summary>Stands in for what Google answers a reminder write with. Null answers nothing.</summary>
        public EventReminders? WhatGoogleReturns { get; set; }

        /// <summary>
        /// Every Information line the save wrote that is about reminders. Selected by the word rather
        /// than by position, so an unrelated line added to the update path later does not break these.
        /// </summary>
        public IReadOnlyList<string> ReminderLines =>
            [.. _informationLines.Where(line => line.Contains("reminders", StringComparison.OrdinalIgnoreCase))];

        public Fixture()
        {
            var calendar = new CalendarInfo { Id = CalId, GoogleCalendarId = "cal@google.com", DisplayName = "Alice" };

            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(u => u.UserId).Returns("u-1");

            var tagParser = new Mock<IMemberTagParser>();
            tagParser.Setup(p => p.NormaliseDescription(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns((string d, IReadOnlyList<string> _) => d ?? string.Empty);

            _repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calendar]);
            _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
            _repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _google.Setup(g => g.PatchEventFieldsAsync(
                    "cal@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>(), It.IsAny<EventReminders?>()))
                .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _, EventReminders? _) =>
                {
                    // What the real client does with the response body: store what Google returned.
                    e.Reminders = WhatGoogleReturns ?? e.Reminders;
                    return e;
                });

            Sut = new CalendarEventService(
                _google.Object, _repo.Object, new Mock<ICalendarMigrationService>().Object, tagParser.Object,
                new Mock<IOutboundWriteHashCache>().Object, currentUser.Object,
                new NodaTimeRecurrenceTimeZoneFactory(), CapturingLogger());
        }

        public void ArrangeSingleEvent(EventReminders? storedReminders)
        {
            var evt = new CalendarEvent
            {
                Id = EventId,
                GoogleEventId = "gid-1",
                Title = "Before",
                Start = new DateTimeOffset(2026, 6, 10, 8, 0, 0, TimeSpan.Zero),
                End = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero),
                Description = "Body",
                OwnerCalendarInfoId = CalId,
                Reminders = storedReminders
            };
            _repo.Setup(r => r.GetEventAsync(EventId, "u-1", It.IsAny<CancellationToken>())).ReturnsAsync(evt);
        }

        /// <summary>
        /// A logger that keeps each Information line as it would reach Seq — template filled in — so
        /// the assertions read the message a person investigating would actually see.
        /// </summary>
        private ILogger<CalendarEventService> CapturingLogger()
        {
            var logger = new Mock<ILogger<CalendarEventService>>();

            logger.Setup(l => l.Log(
                    LogLevel.Information,
                    It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(new InvocationAction(invocation =>
                    _informationLines.Add(invocation.Arguments[2]?.ToString() ?? string.Empty)));

            return logger.Object;
        }
    }
}
