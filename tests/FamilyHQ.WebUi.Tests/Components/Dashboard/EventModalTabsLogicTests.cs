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
}
