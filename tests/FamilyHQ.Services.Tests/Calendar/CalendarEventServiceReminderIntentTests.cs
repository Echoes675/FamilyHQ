using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Time;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// The request's reminder intent must reach the Google client unchanged, and nothing may invent it.
/// Absent on the request means absent on the wire, which is what keeps an ordinary edit from
/// rewriting a reminder set made in the Google Calendar app.
/// </summary>
public class CalendarEventServiceReminderIntentTests
{
    private static readonly Guid CalId   = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public async Task UpdateAsync_RequestDoesNotTouchReminders_PassesNoReminderIntentToGoogle()
    {
        var f = new Fixture();
        f.ArrangeSingleEvent(storedReminders: EventReminders.Explicit([new EventReminder("popup", 45)]));

        await f.Sut.UpdateAsync(EventId, UpdateReq(reminders: null));

        f.SentReminders.Should().BeNull(
            "the event's own stored reminders must not be mistaken for an instruction to write them");
    }

    [Fact]
    public async Task UpdateAsync_RequestCarriesReminders_PassesExactlyThoseToGoogle()
    {
        var f = new Fixture();
        f.ArrangeSingleEvent(storedReminders: EventReminders.InheritsCalendarDefault);

        var asked = EventReminders.Explicit([new EventReminder("popup", 10), new EventReminder("email", 60)]);
        await f.Sut.UpdateAsync(EventId, UpdateReq(asked));

        f.SentReminders!.SameAs(asked).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_RequestAsksForNoReminders_PassesTheExplicitlyNoneShape_NotNull()
    {
        // "The user cleared the reminders" is a write; only an untouched request sends nothing.
        var f = new Fixture();
        f.ArrangeSingleEvent(storedReminders: EventReminders.Explicit([new EventReminder("popup", 45)]));

        await f.Sut.UpdateAsync(EventId, UpdateReq(EventReminders.ExplicitlyNone));

        f.SentReminders.Should().NotBeNull();
        f.SentReminders!.SameAs(EventReminders.ExplicitlyNone).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_ReminderWrite_PersistsWhatGoogleReturned_NotWhatWasAskedFor()
    {
        // Google rewrites what it is sent and answers with what it stored. The client puts that onto
        // the event, and the service must save the event as the client left it.
        var f = new Fixture();
        f.ArrangeSingleEvent(storedReminders: null);
        var googleStored = EventReminders.Explicit([new EventReminder("popup", 0)]);
        f.WhatGoogleReturns = googleStored;

        await f.Sut.UpdateAsync(EventId, UpdateReq(EventReminders.Explicit([new EventReminder("popup", -540)])));

        f.Saved!.Reminders!.SameAs(googleStored).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_ReminderWrite_IsStampedDifferentlyFromTheSameEditWithoutIt()
    {
        // The stamp is what the self-echo guard matches on, so a reminder-only change has to produce
        // its own — otherwise Google's echo of it is indistinguishable from the previous write.
        var withReminders = new Fixture();
        withReminders.ArrangeSingleEvent(storedReminders: null);
        await withReminders.Sut.UpdateAsync(EventId, UpdateReq(EventReminders.Explicit([new EventReminder("popup", 10)])));

        var without = new Fixture();
        without.ArrangeSingleEvent(storedReminders: null);
        await without.Sut.UpdateAsync(EventId, UpdateReq(reminders: null));

        withReminders.SentHash.Should().NotBe(without.SentHash);
    }

    [Fact]
    public async Task UpdateAsync_OrdinaryEdit_IsStampedExactlyAsItWasBeforeRemindersExisted()
    {
        // The digest for a no-reminder write is a published format: events already in production
        // carry it, and a change would make the guard stop recognising FamilyHQ's own writes.
        var f = new Fixture();
        var evt = f.ArrangeSingleEvent(storedReminders: EventReminders.Explicit([new EventReminder("popup", 45)]));

        var request = UpdateReq(reminders: null);
        await f.Sut.UpdateAsync(EventId, request);

        f.SentHash.Should().Be(EventContentHash.Compute(
            request.Title, request.Start, request.End, request.IsAllDay, evt.Description));
    }

    [Fact]
    public async Task CreateAsync_RequestCarriesReminders_PassesThemToGoogle()
    {
        var f = new Fixture();
        f.ArrangeCreate();

        var asked = EventReminders.Explicit([new EventReminder("popup", 30)]);
        await f.Sut.CreateAsync(new CreateEventRequest(
            [CalId], "New", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            false, null, null, null, asked));

        f.SentReminders!.SameAs(asked).Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_RequestDoesNotTouchReminders_PassesNothing_SoGoogleAppliesItsOwnDefault()
    {
        var f = new Fixture();
        f.ArrangeCreate();

        await f.Sut.CreateAsync(new CreateEventRequest(
            [CalId], "New", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null));

        f.SentReminders.Should().BeNull();
    }

    private static UpdateEventRequest UpdateReq(EventReminders? reminders) =>
        new("Edited", new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero), false, "Loc", "Body",
            null, false, reminders);

    private sealed class Fixture
    {
        private readonly Mock<IGoogleCalendarClient> _google = new();
        private readonly Mock<ICalendarRepository> _repo = new();

        public readonly CalendarEventService Sut;

        /// <summary>What the client was handed as the write's reminder intent, if anything.</summary>
        public EventReminders? SentReminders { get; private set; }

        /// <summary>The content hash stamped onto the write.</summary>
        public string? SentHash { get; private set; }

        /// <summary>The event as it reached the repository for saving.</summary>
        public CalendarEvent? Saved { get; private set; }

        /// <summary>Stands in for what Google answers a reminder write with. Null answers nothing.</summary>
        public EventReminders? WhatGoogleReturns { get; set; }

        public Fixture()
        {
            var calendar = new CalendarInfo { Id = CalId, GoogleCalendarId = "cal@google.com", DisplayName = "Alice" };

            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(u => u.UserId).Returns("u-1");

            var tagParser = new Mock<IMemberTagParser>();
            tagParser.Setup(p => p.NormaliseDescription(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns((string d, IReadOnlyList<string> _) => d ?? string.Empty);
            tagParser.Setup(p => p.StripMemberTag(It.IsAny<string>()))
                .Returns((string d) => d ?? string.Empty);

            _repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calendar]);
            _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
            _repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
                .Callback((CalendarEvent e, CancellationToken _) => Saved = e)
                .Returns(Task.CompletedTask);
            _repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
                .Callback((CalendarEvent e, CancellationToken _) => Saved = e)
                .Returns(Task.CompletedTask);

            _google.Setup(g => g.PatchEventFieldsAsync(
                    "cal@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<EventReminders?>()))
                .ReturnsAsync((string _, CalendarEvent e, string hash, CancellationToken _, EventReminders? reminders) =>
                {
                    SentReminders = reminders;
                    SentHash = hash;
                    // What the real client does with the response body: store what Google returned.
                    e.Reminders = WhatGoogleReturns ?? e.Reminders;
                    return e;
                });

            _google.Setup(g => g.CreateEventAsync(
                    "cal@google.com", It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<EventReminders?>()))
                .ReturnsAsync((string _, CalendarEvent e, string hash, CancellationToken _, EventReminders? reminders) =>
                {
                    SentReminders = reminders;
                    SentHash = hash;
                    e.GoogleEventId = "new-gid";
                    e.Reminders = WhatGoogleReturns ?? e.Reminders;
                    return e;
                });

            Sut = new CalendarEventService(
                _google.Object, _repo.Object, new Mock<ICalendarMigrationService>().Object, tagParser.Object,
                new Mock<IOutboundWriteHashCache>().Object, currentUser.Object,
                new NodaTimeRecurrenceTimeZoneFactory(), new Mock<ILogger<CalendarEventService>>().Object);
        }

        public CalendarEvent ArrangeSingleEvent(EventReminders? storedReminders)
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
            return evt;
        }

        public void ArrangeCreate() =>
            _repo.Setup(r => r.GetSharedCalendarAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((CalendarInfo?)null);
    }
}
