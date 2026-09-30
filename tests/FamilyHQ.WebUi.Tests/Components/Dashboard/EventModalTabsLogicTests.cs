using FamilyHQ.Core.Models;
using FamilyHQ.Core.Validators;
using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// FHQ-199: the event modal is tabbed, so a tab has to SHOW its state — the reason Save is
// disabled must never hide on a tab nobody is looking at. These are the two rules that decide
// what the Repeat tab and the footer say; they are pure so they can be tested without rendering
// EventModal (the project has no bUnit).
public class EventModalTabsLogicTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RepeatBadge_WithNoRule_IsNull(string? rule)
    {
        EventModalTabsLogic.RepeatBadge(rule).Should().BeNull();
    }

    [Theory]
    [InlineData("RRULE:FREQ=DAILY", "Daily")]
    [InlineData("RRULE:FREQ=WEEKLY;BYDAY=TU", "Weekly")]
    [InlineData("FREQ=MONTHLY;INTERVAL=2", "Monthly")]
    [InlineData("rrule:freq=yearly", "Yearly")]
    public void RepeatBadge_WithAParsableRule_IsTheFrequencyName(string rule, string expected)
    {
        EventModalTabsLogic.RepeatBadge(rule).Should().Be(expected);
    }

    [Fact]
    public void RepeatBadge_WithAnUnreadableRule_FallsBackRatherThanThrowing()
    {
        // A synced event can carry a rule the parser rejects. This does not save the modal's
        // render from throwing overall — RecurrencePickerModel.FromRecurrenceRule parses the same
        // rule with no catch in the same render — but it does mean the badge rule itself never
        // throws, so it is safe to call from any render path, now or if a future caller needs it
        // outside the one render that already fails on the picker.
        EventModalTabsLogic.RepeatBadge("RRULE:INTERVAL=2")
            .Should().Be(EventModalTabsLogic.UnreadableRuleBadge);
    }

    [Fact]
    public void SaveBlockedHint_WhenRepeatIsIncomplete_ExplainsWhySaveIsDisabled()
    {
        EventModalTabsLogic.SaveBlockedHint(recurrenceComplete: false)
            .Should().Be(EventModalTabsLogic.SaveBlockedByRepeatHint);
    }

    [Fact]
    public void SaveBlockedHint_WhenRepeatIsComplete_IsNull()
    {
        EventModalTabsLogic.SaveBlockedHint(recurrenceComplete: true).Should().BeNull();
    }

    // --- The Reminders tab's badge ---------------------------------------------------------------

    private static EventReminders TwoCalendarDefaults() => EventReminders.Explicit(
    [
        new EventReminder(EventRemindersValidator.PopupMethod, 30),
        new EventReminder(EventRemindersValidator.EmailMethod, 1440)
    ]);

    [Fact]
    public void RemindersBadge_WithNoPicker_IsNull()
    {
        // The badge is read on every render of the tab bar, including before a picker exists.
        EventModalTabsLogic.RemindersBadge(null).Should().BeNull();
    }

    [Fact]
    public void RemindersBadge_WhenTheEventFollowsTheCalendar_SaysDefault()
    {
        var picker = ReminderPickerModel.From(
            EventReminders.InheritsCalendarDefault, TwoCalendarDefaults(), isAllDay: false);

        EventModalTabsLogic.RemindersBadge(picker).Should().Be(EventModalTabsLogic.RemindersDefaultBadge);
    }

    [Fact]
    public void RemindersBadge_WhenTheEventCarriesItsOwn_CountsThem()
    {
        var picker = ReminderPickerModel.From(
            EventReminders.Explicit(
            [
                new EventReminder(EventRemindersValidator.PopupMethod, 10),
                new EventReminder(EventRemindersValidator.PopupMethod, 60),
                new EventReminder(EventRemindersValidator.EmailMethod, 1440)
            ]),
            TwoCalendarDefaults(),
            isAllDay: false);

        EventModalTabsLogic.RemindersBadge(picker).Should().Be("3");
    }

    [Fact]
    public void RemindersBadge_WhenATimedEventHasNoneOfItsOwn_SaysNone()
    {
        var picker = ReminderPickerModel.From(
            EventReminders.ExplicitlyNone, TwoCalendarDefaults(), isAllDay: false);

        EventModalTabsLogic.RemindersBadge(picker).Should().Be(EventModalTabsLogic.RemindersNoneBadge);
    }

    [Fact]
    public void RemindersBadge_WhenAnAllDayEventHasNoneOfItsOwn_DoesNotSayTheEventHasNone()
    {
        // Google stores a reminder set for the day itself and never returns it, so an all-day event
        // with nothing in the API's answer may still notify the family. The badge may say what this
        // screen holds; it may not say the event has no reminders.
        var picker = ReminderPickerModel.From(
            EventReminders.ExplicitlyNone, TwoCalendarDefaults(), isAllDay: true);

        EventModalTabsLogic.RemindersBadge(picker)
            .Should().Be(EventModalTabsLogic.RemindersNoneHereBadge)
            .And.NotBe(EventModalTabsLogic.RemindersNoneBadge);
    }
}
