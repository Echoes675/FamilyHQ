using FamilyHQ.Core.Validators;
using FamilyHQ.WebUi.Components.Dashboard;
using FamilyHQ.WebUi.ViewModels;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// RemindersView.razor delegates its display decisions here rather than keeping them in @code,
// because there is no bUnit in this repo to exercise a .razor file's own code directly — the same
// reason ReminderRowDisplay sits beside ReminderPingRow instead of living in its markup.
public class RemindersViewLogicTests
{
    private static UpcomingReminderViewModel Row(int index) => new(
        TriggerAt: new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero).AddMinutes(index),
        Method: EventRemindersValidator.PopupMethod,
        Minutes: 30,
        IsDefault: false,
        EventId: Guid.Empty,
        EventTitle: $"Event {index}",
        EventStart: new DateTimeOffset(2026, 3, 10, 9, 30, 0, TimeSpan.Zero),
        EventIsAllDay: false,
        Members: Array.Empty<ReminderMemberViewModel>());

    private static ReminderSection<UpcomingReminderViewModel> SectionWith(int rowCount) =>
        new(ReminderSectionKey.Today, "Today", Enumerable.Range(0, rowCount).Select(Row).ToList());

    private static ReminderSection<UpcomingReminderViewModel> EmptySection(ReminderSectionKey key) =>
        new(key, key.ToString(), Array.Empty<UpcomingReminderViewModel>());

    [Fact]
    public void IsEntirelyEmpty_WhenEverySectionHasNoRows_IsTrue()
    {
        var sections = new[] { EmptySection(ReminderSectionKey.Today), EmptySection(ReminderSectionKey.Tomorrow) };

        RemindersViewLogic.IsEntirelyEmpty(sections).Should().BeTrue();
    }

    [Fact]
    public void IsEntirelyEmpty_WhenOneSectionHasARow_IsFalse()
    {
        var sections = new[] { EmptySection(ReminderSectionKey.Today), SectionWith(1) };

        RemindersViewLogic.IsEntirelyEmpty(sections).Should().BeFalse();
    }

    [Fact]
    public void Preview_AtExactlyTheCap_ReturnsEveryRowCollapsed()
    {
        // The cap itself must not trigger "Show all" — only going past it should.
        var section = SectionWith(RemindersViewLogic.PreviewRows);

        RemindersViewLogic.Preview(section, expanded: false).Should().HaveCount(RemindersViewLogic.PreviewRows);
    }

    [Fact]
    public void Preview_OneOverTheCap_StillCapsWhenCollapsed()
    {
        var section = SectionWith(RemindersViewLogic.PreviewRows + 1);

        RemindersViewLogic.Preview(section, expanded: false).Should().HaveCount(RemindersViewLogic.PreviewRows);
    }

    [Fact]
    public void Preview_OneOverTheCap_ReturnsEveryRowWhenExpanded()
    {
        var section = SectionWith(RemindersViewLogic.PreviewRows + 1);

        RemindersViewLogic.Preview(section, expanded: true)
            .Should().HaveCount(RemindersViewLogic.PreviewRows + 1);
    }

    [Theory]
    [InlineData(ReminderSectionKey.Today, "today")]
    [InlineData(ReminderSectionKey.Tomorrow, "tomorrow")]
    [InlineData(ReminderSectionKey.ThisWeek, "this-week")]
    [InlineData(ReminderSectionKey.ThisMonth, "this-month")]
    [InlineData(ReminderSectionKey.NextMonth, "next-month")]
    public void SectionSlug_ForEveryKnownSection_ReadsAsItsKebabCaseName(ReminderSectionKey key, string expected) =>
        RemindersViewLogic.SectionSlug(key).Should().Be(expected);

    [Fact]
    public void SectionSlug_ForAValueOutsideTheKnownFive_ThrowsRatherThanFallingThrough()
    {
        // A cast to an undeclared value, not a sixth enum member — the point is that a key this
        // switch does not name is refused outright, not quietly placed in some default column where
        // it would both mislabel the section and turn an E2E failure into "selector not found".
        var act = () => RemindersViewLogic.SectionSlug((ReminderSectionKey)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
