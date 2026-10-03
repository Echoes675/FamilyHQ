using FamilyHQ.Core.Validators;
using FamilyHQ.WebUi.Components.Dashboard;
using FamilyHQ.WebUi.ViewModels;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// RemindersView.razor delegates its display decisions here rather than keeping them in @code,
// because there is no bUnit in this repo to exercise a .razor file's own code directly — the same
// reason ReminderRowDisplay sits beside ReminderRow instead of living in its markup.
public class RemindersViewLogicTests
{
    private static UpcomingReminderEventViewModel Row(int index) => new(
        EventId: Guid.Empty,
        EventTitle: $"Event {index}",
        EventStart: new DateTimeOffset(2026, 3, 10, 9, 30, 0, TimeSpan.Zero).AddMinutes(index),
        EventIsAllDay: false,
        ReminderCount: 1,
        NextReminderAt: new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero).AddMinutes(index),
        NextReminderMinutes: 30,
        NextReminderMethod: EventRemindersValidator.PopupMethod,
        Members: Array.Empty<ReminderMemberViewModel>());

    private static UpcomingReminderEventViewModel RowStarting(
        string title, DateTimeOffset eventStart, DateTimeOffset nextReminderAt) => new(
        EventId: Guid.Empty,
        EventTitle: title,
        EventStart: eventStart,
        EventIsAllDay: false,
        ReminderCount: 1,
        NextReminderAt: nextReminderAt,
        NextReminderMinutes: (int)(eventStart - nextReminderAt).TotalMinutes,
        NextReminderMethod: EventRemindersValidator.PopupMethod,
        Members: Array.Empty<ReminderMemberViewModel>());

    private static ReminderSection<UpcomingReminderEventViewModel> SectionWith(int rowCount) =>
        new(ReminderSectionKey.Today, "Today", Enumerable.Range(0, rowCount).Select(Row).ToList());

    private static ReminderSection<UpcomingReminderEventViewModel> EmptySection(ReminderSectionKey key) =>
        new(key, key.ToString(), Array.Empty<UpcomingReminderEventViewModel>());

    private static IReadOnlyList<UpcomingReminderEventViewModel> Rows(
        IReadOnlyList<ReminderSection<UpcomingReminderEventViewModel>> sections, ReminderSectionKey key) =>
        sections.Single(s => s.Key == key).Rows;

    [Fact]
    public void Sections_FileEachRowByItsEventStart_NotByItsNextReminder()
    {
        // The one assertion that holds the whole feature up. Both rows are built so that their event
        // start and their next reminder fall in DIFFERENT sections, which is what makes the selector
        // observable at all: with a reminder on the same day as its event, filing by either value
        // gives the same answer and this test would pass on a broken implementation.
        //
        // Today is Tuesday 10 March 2026; a Monday-start week ends Sunday the 15th.
        var today = new DateOnly(2026, 3, 10);

        // Dentist happens today, but was reminded about a week ago — an instant before the timeline's
        // own near edge, so filing by the reminder would drop the row from the view entirely.
        var dentist = RowStarting(
            "Dentist", new DateTimeOffset(2026, 3, 10, 18, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 3, 18, 0, 0, TimeSpan.Zero));

        // Checkup happens on the 17th, which is This month (past the end of this week), and is
        // reminded about tomorrow — the section it must NOT be filed under.
        var checkup = RowStarting(
            "Checkup", new DateTimeOffset(2026, 3, 17, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero));

        var sections = RemindersViewLogic.Sections(
            new[] { dentist, checkup }, today, DayOfWeek.Monday, TimeZoneInfo.Utc);

        Rows(sections, ReminderSectionKey.Today).Should().Equal(
            [dentist], "the event happens today, whenever its reminders happened to fire");
        Rows(sections, ReminderSectionKey.ThisMonth).Should().Equal(
            [checkup], "the event happens on the 17th, which is past the end of this week");
        Rows(sections, ReminderSectionKey.Tomorrow).Should().BeEmpty(
            "tomorrow is when Checkup's reminder fires, which is not what files a row");
    }

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
