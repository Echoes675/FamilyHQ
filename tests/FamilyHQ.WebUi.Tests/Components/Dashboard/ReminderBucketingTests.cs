using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

/// <summary>
/// Filing a row into exactly one section. Every case in here is a date on which the naive reading of
/// "this week" or "this month" sends a row to the wrong column — which on a wall display means
/// tomorrow's event appearing under "Next month". The instant each row is filed by is the caller's
/// choice (the timeline passes the event's own start); these fixtures pass the instant itself, so
/// what they pin is the day arithmetic and nothing else.
/// </summary>
public class ReminderBucketingTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static DateTimeOffset At(int year, int month, int day, int hour = 9) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<ReminderSection<DateTimeOffset>> File(
        DateOnly today, DayOfWeek weekStart, params DateTimeOffset[] instants) =>
        ReminderBucketing.File(instants, i => i, today, weekStart, Utc);

    private static IReadOnlyList<DateTimeOffset> Rows(
        IReadOnlyList<ReminderSection<DateTimeOffset>> sections, ReminderSectionKey key) =>
        sections.Single(s => s.Key == key).Rows;

    [Fact]
    public void File_AlwaysReturnsAllFiveSectionsInOrder()
    {
        // The view lays out a fixed two-column shape; an absent section would move the others.
        // An EMPTY section is the view's business to collapse, not this class's to omit.
        var sections = File(new DateOnly(2026, 3, 10), DayOfWeek.Monday);

        sections.Select(s => s.Key).Should().Equal(
            ReminderSectionKey.Today,
            ReminderSectionKey.Tomorrow,
            ReminderSectionKey.ThisWeek,
            ReminderSectionKey.ThisMonth,
            ReminderSectionKey.NextMonth);
    }

    [Fact]
    public void File_PutsEachRowInExactlyOneSection()
    {
        var today = new DateOnly(2026, 3, 10);     // a Tuesday
        var instants = new[]
        {
            At(2026, 3, 10), At(2026, 3, 11), At(2026, 3, 13), At(2026, 3, 25), At(2026, 4, 8)
        };

        var sections = File(today, DayOfWeek.Monday, instants);

        sections.SelectMany(s => s.Rows).Should().HaveCount(instants.Length);
        sections.SelectMany(s => s.Rows).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void File_ThisWeekNeverRepeatsTodayOrTomorrow()
    {
        // First match wins, so "this week" starts the day after tomorrow.
        var today = new DateOnly(2026, 3, 10);     // Tuesday; Monday-start week ends Sunday the 15th
        var sections = File(today, DayOfWeek.Monday, At(2026, 3, 10), At(2026, 3, 11), At(2026, 3, 12));

        Rows(sections, ReminderSectionKey.Today).Should().Equal(At(2026, 3, 10));
        Rows(sections, ReminderSectionKey.Tomorrow).Should().Equal(At(2026, 3, 11));
        Rows(sections, ReminderSectionKey.ThisWeek).Should().Equal(At(2026, 3, 12));
    }

    [Fact]
    public void File_OnTheLastDayOfAWeek_TomorrowIsStillTomorrowAndNotNextMonth()
    {
        // Sunday 15 March, Monday-start: "this week" is already over, so without a Tomorrow section
        // Monday's row would fall through to This month and render in the far column.
        var sections = File(new DateOnly(2026, 3, 15), DayOfWeek.Monday, At(2026, 3, 16));

        Rows(sections, ReminderSectionKey.Tomorrow).Should().Equal(At(2026, 3, 16));
        Rows(sections, ReminderSectionKey.ThisMonth).Should().BeEmpty();
    }

    [Fact]
    public void File_OnTheLastDayOfAMonth_TomorrowBelongsToTomorrowNotNextMonth()
    {
        // 31 March: tomorrow is 1 April, which is "next month" by the calendar and "tomorrow" to a
        // person. The person wins — Tomorrow is matched first.
        var sections = File(new DateOnly(2026, 3, 31), DayOfWeek.Monday, At(2026, 4, 1));

        Rows(sections, ReminderSectionKey.Tomorrow).Should().Equal(At(2026, 4, 1));
        Rows(sections, ReminderSectionKey.NextMonth).Should().BeEmpty();
    }

    [Fact]
    public void File_WhenThisWeekSpansAMonthBoundary_TheRestOfTheWeekIsStillThisWeek()
    {
        // Wednesday 29 April, Monday-start week runs to Sunday 3 May. Friday 1 May is in the next
        // calendar month but the same week; "this week" is what the family reads it as.
        var sections = File(new DateOnly(2026, 4, 29), DayOfWeek.Monday, At(2026, 5, 1));

        Rows(sections, ReminderSectionKey.ThisWeek).Should().Equal(At(2026, 5, 1));
        Rows(sections, ReminderSectionKey.NextMonth).Should().BeEmpty();
    }

    [Fact]
    public void File_WithASundayStartWeek_FilesDifferentlyFromAMondayStartWeek()
    {
        // The week start is an input, not a constant, and this is the case that proves the parameter
        // is actually consulted. Today is Sunday 15 March 2026:
        //   Monday-start -> the week containing it is Mon 9 to Sun 15. It ends TODAY, so Wednesday
        //                   the 18th is outside the week and falls through to This month.
        //   Sunday-start -> the week containing it is Sun 15 to Sat 21. The 18th is inside it.
        // One date, one row, opposite answers.
        var today = new DateOnly(2026, 3, 15);
        var row = At(2026, 3, 18);                // the following Wednesday

        var mondayStart = File(today, DayOfWeek.Monday, row);
        Rows(mondayStart, ReminderSectionKey.ThisWeek).Should().BeEmpty(
            "a Monday-start week containing Sunday the 15th ends on the 15th");
        Rows(mondayStart, ReminderSectionKey.ThisMonth).Should().Equal(row);

        var sundayStart = File(today, DayOfWeek.Sunday, row);
        Rows(sundayStart, ReminderSectionKey.ThisWeek).Should().Equal(
            [row], "a Sunday-start week containing Sunday the 15th runs to Saturday the 21st");
        Rows(sundayStart, ReminderSectionKey.ThisMonth).Should().BeEmpty();
    }

    [Fact]
    public void File_AcrossADaylightSavingTransition_FilesByTheLocalDayNotTheUtcDay()
    {
        // Irish clocks go forward at 01:00 on 29 March 2026. An instant of 23:30Z on the 28th is
        // 23:30 local; 23:30Z on the 29th is 00:30 local on the 30th — a different day.
        var dublin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");
        var lateOnTheTransitionDay = new DateTimeOffset(2026, 3, 29, 23, 30, 0, TimeSpan.Zero);

        var sections = ReminderBucketing.File(
            new[] { lateOnTheTransitionDay }, i => i,
            new DateOnly(2026, 3, 29), DayOfWeek.Monday, dublin);

        sections.Single(s => s.Key == ReminderSectionKey.Today).Rows
            .Should().BeEmpty("in Dublin that instant is already the 30th");
        sections.Single(s => s.Key == ReminderSectionKey.Tomorrow).Rows
            .Should().Equal(lateOnTheTransitionDay);
    }

    [Fact]
    public void File_DropsARowOutsideEverySection()
    {
        // Beyond the window the server returned. Dropping it is correct; rendering it in the last
        // section would quietly misfile it.
        var sections = File(new DateOnly(2026, 3, 10), DayOfWeek.Monday, At(2026, 9, 1));

        sections.SelectMany(s => s.Rows).Should().BeEmpty();
    }

    [Fact]
    public void File_SortsRowsWithinASectionByTheInstantTheyAreFiledBy()
    {
        // Whatever the selector reads, which for the reminders timeline is each event's own start —
        // so a section reads top to bottom in the order its events actually happen.
        var sections = File(
            new DateOnly(2026, 3, 10), DayOfWeek.Monday,
            At(2026, 3, 10, 17), At(2026, 3, 10, 8), At(2026, 3, 10, 12));

        Rows(sections, ReminderSectionKey.Today).Should().Equal(
            At(2026, 3, 10, 8), At(2026, 3, 10, 12), At(2026, 3, 10, 17));
    }
}
