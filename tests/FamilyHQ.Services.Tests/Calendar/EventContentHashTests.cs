using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class EventContentHashTests
{
    private static readonly DateTimeOffset Start = new(2026, 4, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End   = new(2026, 4, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compute_SameInputs_ReturnsSameHash()
    {
        var h1 = EventContentHash.Compute("Title", Start, End, false, "desc");
        var h2 = EventContentHash.Compute("Title", Start, End, false, "desc");
        h1.Should().Be(h2);
    }

    [Fact]
    public void Compute_DifferentTitle_ReturnsDifferentHash()
    {
        var h1 = EventContentHash.Compute("Title A", Start, End, false, null);
        var h2 = EventContentHash.Compute("Title B", Start, End, false, null);
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Compute_DifferentStartTime_ReturnsDifferentHash()
    {
        var later = Start.AddHours(1);
        var h1 = EventContentHash.Compute("T", Start, End, false, null);
        var h2 = EventContentHash.Compute("T", later, End, false, null);
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Compute_NullVsEmptyDescription_AreEquivalent()
    {
        var h1 = EventContentHash.Compute("T", Start, End, false, null);
        var h2 = EventContentHash.Compute("T", Start, End, false, "");
        h1.Should().Be(h2);
    }

    [Fact]
    public void Compute_ReturnsNonEmptyLowercaseHexString()
    {
        var hash = EventContentHash.Compute("T", Start, End, false, null);
        hash.Should().NotBeNullOrEmpty();
        hash.Should().MatchRegex("^[0-9a-f]+$");
    }

    /// <summary>
    /// Pins the exact digest an event with no reminder intent produces. The value is stamped into
    /// <c>extendedProperties.private</c> on every write and is the self-echo guard's key, so the
    /// digest for the no-reminder shape is a published format, not an implementation detail: change
    /// it and the guard stops recognising writes stamped by the previous build, which surfaces as
    /// the kiosk's own edits echoing back as inbound changes.
    /// </summary>
    [Fact]
    public void Compute_WithNoReminderIntent_ProducesTheDigestAlreadyStampedOnProductionEvents()
    {
        EventContentHash.Compute("T", Start, End, false, null)
            .Should().Be("f577ff93d76cefc4b7f958de21cef606f7dd34fdf88e5951f5fdf8028030db97");
    }

    [Fact]
    public void Compute_StartInDifferentTimezone_SameUtcMoment_ReturnsSameHash()
    {
        var startUtc   = new DateTimeOffset(2026, 4, 6, 9, 0, 0, TimeSpan.Zero);
        var startLocal = new DateTimeOffset(2026, 4, 6, 10, 0, 0, TimeSpan.FromHours(1)); // same UTC moment
        var h1 = EventContentHash.Compute("T", startUtc,   End, false, null);
        var h2 = EventContentHash.Compute("T", startLocal, End, false, null);
        h1.Should().Be(h2);
    }

    // ── Reminder intent ───────────────────────────────────────────────────────

    [Fact]
    public void Compute_OmittingReminderIntent_MatchesPassingItExplicitlyAsNothing()
    {
        // The two must agree, or a write that does not touch reminders would be stamped differently
        // depending on which overload the call site happened to use.
        EventContentHash.Compute("T", Start, End, false, "d")
            .Should().Be(EventContentHash.Compute("T", Start, End, false, "d", null));
    }

    [Fact]
    public void Compute_ReminderWrite_DiffersFromTheSameEventWithNoReminderWrite()
    {
        // A reminder-only change must produce its own stamp, or the self-echo guard has nothing that
        // distinguishes this write from the last one on the same event.
        var withoutReminders = EventContentHash.Compute("T", Start, End, false, null);
        var withReminders = EventContentHash.Compute("T", Start, End, false, null,
            EventReminders.Explicit([new EventReminder("popup", 10)]));

        withReminders.Should().NotBe(withoutReminders);
    }

    [Fact]
    public void Compute_DifferentRemindersOnAnOtherwiseIdenticalEvent_ReturnDifferentHashes()
    {
        var tenMinutes = EventContentHash.Compute("T", Start, End, false, null,
            EventReminders.Explicit([new EventReminder("popup", 10)]));
        var sixtyMinutes = EventContentHash.Compute("T", Start, End, false, null,
            EventReminders.Explicit([new EventReminder("popup", 60)]));

        tenMinutes.Should().NotBe(sixtyMinutes);
    }

    [Fact]
    public void Compute_SameRemindersInADifferentOrder_ReturnsTheSameHash()
    {
        // Google does not preserve the order of overrides — send [10, 60] and it returns [60, 10].
        // An order-sensitive stamp would make every sync of an unchanged event look like a change.
        var ascending = EventContentHash.Compute("T", Start, End, false, null,
            EventReminders.Explicit([new EventReminder("popup", 10), new EventReminder("email", 60)]));
        var descending = EventContentHash.Compute("T", Start, End, false, null,
            EventReminders.Explicit([new EventReminder("email", 60), new EventReminder("popup", 10)]));

        ascending.Should().Be(descending);
    }

    [Fact]
    public void Compute_InheritsCalendarDefault_DiffersFromExplicitlyNone()
    {
        // "Follow the calendar's defaults" and "no reminders at all" are different instructions to
        // Google, so two writes carrying them must not share a stamp.
        var inherits = EventContentHash.Compute("T", Start, End, false, null,
            EventReminders.InheritsCalendarDefault);
        var none = EventContentHash.Compute("T", Start, End, false, null,
            EventReminders.ExplicitlyNone);

        inherits.Should().NotBe(none);
    }

    [Fact]
    public void Compute_ExplicitlyNone_DiffersFromNoReminderWriteAtAll()
    {
        // "The user asked for no reminders" is a write; "the user did not touch reminders" is not.
        var none = EventContentHash.Compute("T", Start, End, false, null, EventReminders.ExplicitlyNone);

        none.Should().NotBe(EventContentHash.Compute("T", Start, End, false, null));
    }

    [Fact]
    public void Compute_DuplicateOverrides_DifferFromASingleOne()
    {
        // Google de-duplicates, but the stamp describes what was SENT; collapsing duplicates here
        // would make the sent body and its stamp disagree.
        var once = EventContentHash.Compute("T", Start, End, false, null,
            EventReminders.Explicit([new EventReminder("popup", 10)]));
        var twice = EventContentHash.Compute("T", Start, End, false, null,
            EventReminders.Explicit([new EventReminder("popup", 10), new EventReminder("popup", 10)]));

        once.Should().NotBe(twice);
    }
}
