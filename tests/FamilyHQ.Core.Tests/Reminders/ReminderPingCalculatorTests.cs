using FamilyHQ.Core.Models;
using FamilyHQ.Core.Reminders;
using FluentAssertions;

namespace FamilyHQ.Core.Tests.Reminders;

/// <summary>
/// The arithmetic behind every row of the reminders timeline. Two readings of an all-day reminder are
/// possible and only one matches Google: an all-day offset is counted back from local midnight on the
/// event's first day, not from an instant the event "starts" at. The naive reading is tested against
/// explicitly, because it is the one someone will reintroduce.
/// </summary>
public class ReminderPingCalculatorTests
{
    private static readonly TimeZoneInfo Dublin =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");

    private static EventReminders Explicit(params (string Method, int Minutes)[] overrides) =>
        EventReminders.Explicit(overrides.Select(o => new EventReminder(o.Method, o.Minutes)));

    [Fact]
    public void Compute_ForATimedEvent_CountsBackFromTheStart()
    {
        var start = new DateTimeOffset(2026, 3, 10, 14, 0, 0, TimeSpan.FromHours(0));

        var pings = ReminderPingCalculator.Compute(
            start, isAllDay: false, Explicit(("popup", 30)), calendarDefaults: null, Dublin);

        pings.Should().ContainSingle();
        pings[0].TriggerAt.Should().Be(start.AddMinutes(-30));
        pings[0].Method.Should().Be("popup");
        pings[0].Minutes.Should().Be(30);
        pings[0].IsDefault.Should().BeFalse();
    }

    [Fact]
    public void Compute_ForAnAllDayEvent_CountsBackFromLocalMidnightNotFromTheStartInstant()
    {
        // Google stores an all-day reminder as minutes before the event day's LOCAL midnight:
        // "1 day before at 17:00" is 420. The stored start instant for an all-day event is a bare
        // date, so a naive "start - minutes" happens to agree whenever the zone offset is zero —
        // which is why this test uses a date in Irish summer time, where it does not.
        var allDayStart = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);

        var pings = ReminderPingCalculator.Compute(
            allDayStart, isAllDay: true, Explicit(("popup", 420)), calendarDefaults: null, Dublin);

        // Local midnight on 15 July in Dublin is 2026-07-14T23:00Z (UTC+1). 420 minutes before that
        // is 2026-07-14T16:00Z, i.e. 17:00 local on the 14th — "the day before at 5pm".
        pings.Should().ContainSingle();
        pings[0].TriggerAt.Should().Be(new DateTimeOffset(2026, 7, 14, 16, 0, 0, TimeSpan.Zero));

        // The naive reading would give 2026-07-14T17:00Z. If this assertion ever fails, the
        // calculator has been "simplified" back to minutes-before-start.
        pings[0].TriggerAt.Should().NotBe(allDayStart.AddMinutes(-420));
    }

    [Fact]
    public void Compute_WhenTheEventInheritsAndTheCalendarHasDefaults_UsesThemAndFlagsThem()
    {
        var start = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);

        var pings = ReminderPingCalculator.Compute(
            start, isAllDay: false, EventReminders.InheritsCalendarDefault, Explicit(("popup", 45)), Dublin);

        pings.Should().ContainSingle();
        pings[0].TriggerAt.Should().Be(start.AddMinutes(-45));
        pings[0].IsDefault.Should().BeTrue("the row carries a 'default' tag, so the view has to know");
    }

    [Fact]
    public void Compute_WhenRemindersWereNeverSynced_ProducesNothing()
    {
        // Null is "not synced yet", NOT "no reminders" — and it is never rendered as "unknown".
        var pings = ReminderPingCalculator.Compute(
            new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero),
            isAllDay: false, eventReminders: null, Explicit(("popup", 45)), Dublin);

        pings.Should().BeEmpty(
            "a null reminders object means the event has not been synced for reminders yet. Falling "
            + "back to the calendar's defaults here would invent a ping the phone will not make");
    }

    [Fact]
    public void Compute_WhenRemindersWereExplicitlyRemoved_ProducesNothing()
    {
        var pings = ReminderPingCalculator.Compute(
            new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero),
            isAllDay: false, EventReminders.ExplicitlyNone, Explicit(("popup", 45)), Dublin);

        pings.Should().BeEmpty("the family removed them; the calendar's defaults do not come back");
    }

    [Fact]
    public void Compute_WhenTheEventInheritsAndTheCalendarHasNoDefaults_ProducesNothing()
    {
        var pings = ReminderPingCalculator.Compute(
            new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero),
            isAllDay: false, EventReminders.InheritsCalendarDefault, EventReminders.ExplicitlyNone, Dublin);

        pings.Should().BeEmpty("nothing will ping, so nothing is shown");
    }

    [Fact]
    public void Compute_WithSeveralReminders_ProducesOnePingEach()
    {
        var start = new DateTimeOffset(2026, 3, 10, 14, 0, 0, TimeSpan.Zero);

        var pings = ReminderPingCalculator.Compute(
            start, isAllDay: false, Explicit(("popup", 10), ("email", 60)), null, Dublin);

        pings.Should().HaveCount(2, "an event with three reminders appears three times on the timeline");
        pings.Select(p => p.Minutes).Should().BeEquivalentTo(new[] { 10, 60 });
    }

    [Fact]
    public void Compute_PreservesAMethodTheKioskCouldNotHaveCreated()
    {
        // Google is the authority on its own data. A method FamilyHQ cannot write still has to be
        // shown rather than filtered out or renamed.
        var start = new DateTimeOffset(2026, 3, 10, 14, 0, 0, TimeSpan.Zero);

        var pings = ReminderPingCalculator.Compute(
            start, isAllDay: false, Explicit(("sms", 47)), null, Dublin);

        pings.Should().ContainSingle();
        pings[0].Method.Should().Be("sms");
        pings[0].Minutes.Should().Be(47);
    }

    [Fact]
    public void Compute_AcrossASpringForwardTransition_UsesTheOffsetInEffectOnTheEventDay()
    {
        // Irish clocks go forward on 29 March 2026. An all-day event on the 30th has local midnight
        // at 2026-03-29T23:00Z (UTC+1), not 2026-03-30T00:00Z.
        var allDayStart = new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.Zero);

        var pings = ReminderPingCalculator.Compute(
            allDayStart, isAllDay: true, Explicit(("popup", 60)), null, Dublin);

        pings[0].TriggerAt.Should().Be(new DateTimeOffset(2026, 3, 29, 22, 0, 0, TimeSpan.Zero));
    }
}
