using FamilyHQ.WebUi.Components.Dashboard;
using FamilyHQ.WebUi.ViewModels;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// RemindersView.razor delegates its display decisions here rather than keeping them in @code,
// because there is no bUnit in this repo to exercise a .razor file's own code directly — the same
// reason ReminderRowDisplay sits beside ReminderRow instead of living in its markup.
public class RemindersViewLogicTests
{
    // Dublin, so the zone-conversion cases below are genuinely exercised: it is UTC+1 on every
    // summer fixture here, which is what lets a UTC reading of the same instant land on a different
    // day. The same choice, for the same reason, as ReminderRowDisplayTests and ReminderBucketingTests.
    private static readonly TimeZoneInfo Dublin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");

    private static UpcomingReminderEventViewModel Row(int index) => new(
        EventId: Guid.Empty,
        EventTitle: $"Event {index}",
        EventStart: new DateTimeOffset(2026, 3, 10, 9, 30, 0, TimeSpan.Zero).AddMinutes(index),
        EventIsAllDay: false,
        Members: Array.Empty<ReminderMemberViewModel>());

    private static UpcomingReminderEventViewModel RowStarting(
        string title, DateTimeOffset eventStart, bool isAllDay = false) => new(
        EventId: Guid.Empty,
        EventTitle: title,
        EventStart: eventStart,
        EventIsAllDay: isAllDay,
        Members: Array.Empty<ReminderMemberViewModel>());

    private static ReminderSection<UpcomingReminderEventViewModel> SectionWith(int rowCount) =>
        new(ReminderSectionKey.ThisMonth, "This month", Enumerable.Range(0, rowCount).Select(Row).ToList());

    private static ReminderSection<UpcomingReminderEventViewModel> EmptySection(ReminderSectionKey key) =>
        new(key, key.ToString(), Array.Empty<UpcomingReminderEventViewModel>());

    private static IReadOnlyList<UpcomingReminderEventViewModel> Rows(
        IReadOnlyList<ReminderSection<UpcomingReminderEventViewModel>> sections, ReminderSectionKey key) =>
        sections.Single(s => s.Key == key).Rows;

    [Fact]
    public void Sections_FileEachRowByItsEventStart()
    {
        // Today is 10 March 2026. The two rows sit either side of the month boundary, so this pins
        // the whole composition — today, the zone and the selector handed to ReminderBucketing —
        // and not merely that a list comes back.
        var today = new DateOnly(2026, 3, 10);

        var dentist = RowStarting("Dentist", new DateTimeOffset(2026, 3, 10, 18, 0, 0, TimeSpan.Zero));
        var checkup = RowStarting("Checkup", new DateTimeOffset(2026, 4, 8, 9, 0, 0, TimeSpan.Zero));

        var sections = RemindersViewLogic.Sections(
            new[] { dentist, checkup }, today, TimeZoneInfo.Utc);

        Rows(sections, ReminderSectionKey.ThisMonth).Should().Equal(
            [dentist], "the event happens today, and This month now includes today");
        Rows(sections, ReminderSectionKey.NextMonth).Should().Equal(
            [checkup], "the event happens in April");
    }

    [Fact]
    public void Sections_ReturnsExactlyTheTwoPanelsInOrder()
    {
        // Left to right, which is also the order the CSS grid places them in by slug.
        var sections = RemindersViewLogic.Sections(
            Array.Empty<UpcomingReminderEventViewModel>(), new DateOnly(2026, 3, 10), TimeZoneInfo.Utc);

        sections.Select(s => s.Key).Should().Equal(
            ReminderSectionKey.ThisMonth, ReminderSectionKey.NextMonth);
    }

    [Fact]
    public void IsEntirelyEmpty_WhenEverySectionHasNoRows_IsTrue()
    {
        var sections = new[] { EmptySection(ReminderSectionKey.ThisMonth), EmptySection(ReminderSectionKey.NextMonth) };

        RemindersViewLogic.IsEntirelyEmpty(sections).Should().BeTrue();
    }

    [Fact]
    public void IsEntirelyEmpty_WhenOneSectionHasARow_IsFalse()
    {
        var sections = new[] { EmptySection(ReminderSectionKey.NextMonth), SectionWith(1) };

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

    [Fact]
    public void TodayCount_CountsOnlyTheRowsWhoseEventHappensToday()
    {
        // The badge's number. It has to agree with the rows reading "Today" underneath it, so it is
        // read off the same value by the same conversion — not off a reminder's own trigger instant,
        // which for a "1 day before" reminder sits on a different day from its event entirely.
        var today = new DateOnly(2026, 6, 15);
        var rows = new[]
        {
            RowStarting("Dentist", new DateTimeOffset(2026, 6, 15, 8, 0, 0, TimeSpan.Zero)),
            RowStarting("Swimming", new DateTimeOffset(2026, 6, 15, 17, 30, 0, TimeSpan.Zero)),
            RowStarting("Parents evening", new DateTimeOffset(2026, 6, 16, 18, 0, 0, TimeSpan.Zero)),
            RowStarting("Car service", new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero))
        };

        RemindersViewLogic.TodayCount(rows, today, TimeZoneInfo.Utc).Should().Be(2);
    }

    [Fact]
    public void TodayCount_CountsAnAllDayEventHappeningToday()
    {
        // An all-day event is still something falling today, and the row for it reads "Today · all
        // day". A count that skipped it would contradict a row the family can see.
        var today = new DateOnly(2026, 6, 15);
        var rows = new[]
        {
            RowStarting("Bin day", new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), isAllDay: true)
        };

        RemindersViewLogic.TodayCount(rows, today, TimeZoneInfo.Utc).Should().Be(1);
    }

    [Fact]
    public void TodayCount_ReadsEachRowsDayInTheHouseholdZoneNotInUtc()
    {
        // 23:30Z on 14 June is 00:30 on the 15th in Dublin. Counted in UTC this row would not be
        // today's, while the row itself would read "Today" — the badge and the list disagreeing is
        // the one failure this function exists to prevent.
        var rows = new[]
        {
            RowStarting("Early flight", new DateTimeOffset(2026, 6, 14, 23, 30, 0, TimeSpan.Zero))
        };

        RemindersViewLogic.TodayCount(rows, new DateOnly(2026, 6, 15), Dublin).Should().Be(1);
    }

    [Fact]
    public void TodayCount_WithNoRowsAtAll_IsZero() =>
        // The badge's absence is its signal, so zero has to be a real answer rather than a state the
        // caller has to special-case before asking.
        RemindersViewLogic.TodayCount(
                Array.Empty<UpcomingReminderEventViewModel>(), new DateOnly(2026, 6, 15), TimeZoneInfo.Utc)
            .Should().Be(0);

    [Theory]
    [InlineData(ReminderSectionKey.ThisMonth, "this-month")]
    [InlineData(ReminderSectionKey.NextMonth, "next-month")]
    public void SectionSlug_ForEveryKnownSection_ReadsAsItsKebabCaseName(ReminderSectionKey key, string expected) =>
        RemindersViewLogic.SectionSlug(key).Should().Be(expected);

    [Fact]
    public void SectionSlug_ForAValueOutsideTheKnownTwo_ThrowsRatherThanFallingThrough()
    {
        // A cast to an undeclared value, not a third enum member — the point is that a key this
        // switch does not name is refused outright, not quietly placed in some default column where
        // it would both mislabel the panel and turn an E2E failure into "selector not found".
        var act = () => RemindersViewLogic.SectionSlug((ReminderSectionKey)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
