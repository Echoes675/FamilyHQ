using FamilyHQ.WebUi.Components.Dashboard;
using FamilyHQ.WebUi.ViewModels;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// RemindersView.razor delegates its display decisions here rather than keeping them in @code,
// because there is no bUnit in this repo to exercise a .razor file's own code directly — the same
// reason ReminderRowDisplay sits beside ReminderRow instead of living in its markup.
//
// No zone appears in this file any more: a row carries the day the server decided it is on, so
// nothing here converts anything. The conversion is pinned in EventWallClockTests, once.
public class RemindersViewLogicTests
{
    private static readonly Guid DentistId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CheckupId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static UpcomingReminderEventViewModel Row(string title, DateOnly date, TimeOnly? time) =>
        new(Guid.Parse($"00000000-0000-0000-0000-{title.Length:D12}"), title, date, time, []);

    private static UpcomingReminderEventViewModel FillerRow(int index) => new(
        EventId: Guid.Empty,
        EventTitle: $"Event {index}",
        EventDate: new DateOnly(2026, 3, 10),
        EventTimeOfDay: new TimeOnly(9, 30).AddMinutes(index),
        Members: Array.Empty<ReminderMemberViewModel>());

    private static ReminderSection<UpcomingReminderEventViewModel> SectionWith(int rowCount) =>
        new(ReminderSectionKey.ThisMonth, "This month", Enumerable.Range(0, rowCount).Select(FillerRow).ToList());

    private static ReminderSection<UpcomingReminderEventViewModel> EmptySection(ReminderSectionKey key) =>
        new(key, key.ToString(), Array.Empty<UpcomingReminderEventViewModel>());

    private static IReadOnlyList<UpcomingReminderEventViewModel> Rows(
        IReadOnlyList<ReminderSection<UpcomingReminderEventViewModel>> sections, ReminderSectionKey key) =>
        sections.Single(s => s.Key == key).Rows;

    [Fact]
    public void Sections_FileEachRowByItsOwnDay()
    {
        // Today is 10 March 2026. The two rows sit either side of the month boundary, so this pins
        // the whole composition — today and the selector handed to ReminderBucketing — and not
        // merely that a list comes back. Explicit ids because the two titles are the same length,
        // and the helper derives its id from that.
        var today = new DateOnly(2026, 3, 10);

        var dentist = new UpcomingReminderEventViewModel(
            DentistId, "Dentist", new DateOnly(2026, 3, 10), new TimeOnly(18, 0), []);
        var checkup = new UpcomingReminderEventViewModel(
            CheckupId, "Checkup", new DateOnly(2026, 4, 8), new TimeOnly(9, 0), []);

        var sections = RemindersViewLogic.Sections(new[] { dentist, checkup }, today);

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
            Array.Empty<UpcomingReminderEventViewModel>(), new DateOnly(2026, 3, 10));

        sections.Select(s => s.Key).Should().Equal(
            ReminderSectionKey.ThisMonth, ReminderSectionKey.NextMonth);
    }

    [Fact]
    public void InDayOrder_PutsAnAllDayRowBeforeEveryTimedRowOnTheSameDay()
    {
        var day = new DateOnly(2026, 6, 17);
        var rows = new[]
        {
            Row("Half Past Midnight", day, new TimeOnly(0, 30)),
            Row("Bin Day", day, null),
            Row("Two O'Clock", day, new TimeOnly(2, 0))
        };

        RemindersViewLogic.InDayOrder(rows).Select(r => r.EventTitle)
            .Should().ContainInOrder("Bin Day", "Half Past Midnight", "Two O'Clock");
    }

    [Fact]
    public void InDayOrder_BreaksATieOnTheSameDayAndTimeByTitle()
    {
        var day = new DateOnly(2026, 6, 17);
        var rows = new[] { Row("Zoe's Recital", day, new TimeOnly(9, 0)), Row("Alex's Match", day, new TimeOnly(9, 0)) };

        RemindersViewLogic.InDayOrder(rows).Select(r => r.EventTitle)
            .Should().ContainInOrder("Alex's Match", "Zoe's Recital");
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
        // read off the same value by the same comparison — not off a reminder's own trigger instant,
        // which for a "1 day before" reminder sits on a different day from its event entirely.
        var today = new DateOnly(2026, 6, 15);
        var rows = new[]
        {
            Row("Dentist", new DateOnly(2026, 6, 15), new TimeOnly(8, 0)),
            Row("Swimming", new DateOnly(2026, 6, 15), new TimeOnly(17, 30)),
            Row("Parents evening", new DateOnly(2026, 6, 16), new TimeOnly(18, 0)),
            Row("Car service", new DateOnly(2026, 7, 2), new TimeOnly(9, 0))
        };

        RemindersViewLogic.TodayCount(rows, today).Should().Be(2);
    }

    [Fact]
    public void TodayCount_CountsAnAllDayEventHappeningToday() =>
        // An all-day event is still something falling today, and the row for it reads "Today · all
        // day". A count that skipped it would contradict a row the family can see.
        RemindersViewLogic.TodayCount(
                [Row("Bank Holiday", new DateOnly(2026, 6, 15), null)], new DateOnly(2026, 6, 15))
            .Should().Be(1);

    // The household-zone reading this suite used to pin here — a late-evening UTC instant counted as
    // today because it is already today locally — is now
    // EventWallClockTests.Of_ForATimedEventJustAfterLocalMidnight_ReadsTheHouseholdDayNotTheUtcOne,
    // server-side. There is no zone left on this side to read a row's day in.

    [Fact]
    public void TodayCount_WithNoRowsAtAll_IsZero() =>
        // The badge's absence is its signal, so zero has to be a real answer rather than a state the
        // caller has to special-case before asking.
        RemindersViewLogic.TodayCount(
                Array.Empty<UpcomingReminderEventViewModel>(), new DateOnly(2026, 6, 15))
            .Should().Be(0);

    [Fact]
    public void EffectiveToday_BeforeTheFirstFetch_FallsBackToTheKiosksOwnDate() =>
        // A default DateOnly would be 0001-01-01: every row files into neither panel and the view
        // says "No reminders coming up" while rows sit in memory.
        RemindersViewLogic.EffectiveToday(null, dayOffset: 0, kioskToday: new DateOnly(2026, 6, 15))
            .Should().Be(new DateOnly(2026, 6, 15));

    [Fact]
    public void EffectiveToday_BeforeTheFirstFetchWithTheDevClockAdvanced_DoesNotShiftTheKiosksDateASecondTime() =>
        // The kiosk's date is read off a clock that ALREADY carries the offset, so the fallback must
        // not apply it again. Without this case the offset-shifting fixture below is satisfied by an
        // implementation that shifts both branches, and in a lower environment the panels and the
        // badge would move two days for one — the very class of disagreement being removed here.
        RemindersViewLogic.EffectiveToday(null, dayOffset: 1, kioskToday: new DateOnly(2026, 6, 16))
            .Should().Be(new DateOnly(2026, 6, 16));

    [Fact]
    public void EffectiveToday_WithAServerAnchor_IgnoresTheKiosksOwnDate() =>
        // The whole point: the kiosk's zone is not the household's once a zone has been chosen
        // explicitly, and the server's answer wins.
        RemindersViewLogic.EffectiveToday(new DateOnly(2026, 6, 15), dayOffset: 0, kioskToday: new DateOnly(2026, 6, 16))
            .Should().Be(new DateOnly(2026, 6, 15));

    [Fact]
    public void EffectiveToday_WithTheDevClockAdvanced_ShiftsTheServersAnchorNotTheKiosksDate() =>
        // How the eight clock-bridge E2E scenarios keep working: the kiosk still moves its own day,
        // relative to the day the server named.
        RemindersViewLogic.EffectiveToday(new DateOnly(2026, 6, 15), dayOffset: 1, kioskToday: new DateOnly(2026, 6, 16))
            .Should().Be(new DateOnly(2026, 6, 16));

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
