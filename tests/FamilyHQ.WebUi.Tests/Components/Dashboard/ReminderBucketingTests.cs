using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

/// <summary>
/// Filing a row into exactly one of the two panels. Every case in here is a date on which the naive
/// reading of "this month" sends a row to the wrong panel, or loses it altogether — which on a wall
/// display means tomorrow's event vanishing on the one day of the month the family most needs it.
/// The instant each row is filed by is the caller's choice (the timeline passes the event's own
/// start); these fixtures pass the instant itself, so what they pin is the day arithmetic and nothing
/// else.
/// </summary>
public class ReminderBucketingTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static DateTimeOffset At(int year, int month, int day, int hour = 9) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<ReminderSection<DateTimeOffset>> File(
        DateOnly today, params DateTimeOffset[] instants) =>
        ReminderBucketing.File(instants, i => i, today, Utc);

    private static IReadOnlyList<DateTimeOffset> Rows(
        IReadOnlyList<ReminderSection<DateTimeOffset>> sections, ReminderSectionKey key) =>
        sections.Single(s => s.Key == key).Rows;

    [Fact]
    public void File_AlwaysReturnsBothPanelsInOrder()
    {
        // Both panels, always, because two readers need them present rather than because the layout
        // does: app.css places each panel by an explicit grid-column keyed on its slug, so a missing
        // one leaves the other where it is. RemindersViewLogic.IsEntirelyEmpty can only tell
        // "nothing at all" from "nothing in one month" by asking both, and the per-panel "Nothing"
        // line needs a panel to render against. Collapsing an EMPTY panel is the view's business,
        // not this class's to pre-empt by omitting it.
        var sections = File(new DateOnly(2026, 3, 10));

        sections.Select(s => s.Key).Should().Equal(
            ReminderSectionKey.ThisMonth,
            ReminderSectionKey.NextMonth);
    }

    [Fact]
    public void File_PutsEachRowInExactlyOnePanel()
    {
        var today = new DateOnly(2026, 3, 10);
        var instants = new[]
        {
            At(2026, 3, 10), At(2026, 3, 11), At(2026, 3, 13), At(2026, 3, 25), At(2026, 4, 8)
        };

        var sections = File(today, instants);

        sections.SelectMany(s => s.Rows).Should().HaveCount(instants.Length);
        sections.SelectMany(s => s.Rows).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void File_ARowHappeningToday_LandsInThisMonth()
    {
        // The whole point of the two-panel model: "This month" is inclusive of today. Under the
        // five-section model today had a section of its own, so "This month" started after the
        // current week ended — and on 51 days of a year there was no day left for it to claim.
        var sections = File(new DateOnly(2026, 3, 10), At(2026, 3, 10));

        Rows(sections, ReminderSectionKey.ThisMonth).Should().Equal(At(2026, 3, 10));
        Rows(sections, ReminderSectionKey.NextMonth).Should().BeEmpty();
    }

    [Fact]
    public void File_ARowOnTheLastDayOfThisMonth_LandsInThisMonth()
    {
        // The inclusive far edge of the left panel.
        var sections = File(new DateOnly(2026, 3, 10), At(2026, 3, 31));

        Rows(sections, ReminderSectionKey.ThisMonth).Should().Equal(At(2026, 3, 31));
        Rows(sections, ReminderSectionKey.NextMonth).Should().BeEmpty();
    }

    [Fact]
    public void File_ARowOnTheFirstDayOfNextMonth_LandsInNextMonth()
    {
        // The right panel's near edge, one day past the left panel's far edge — the boundary both
        // panels have to agree on, asserted from the other side.
        var sections = File(new DateOnly(2026, 3, 10), At(2026, 4, 1));

        Rows(sections, ReminderSectionKey.NextMonth).Should().Equal(At(2026, 4, 1));
        Rows(sections, ReminderSectionKey.ThisMonth).Should().BeEmpty();
    }

    [Fact]
    public void File_ARowOnTheLastDayOfNextMonth_LandsInNextMonth()
    {
        // The inclusive far edge of the right panel, and the far edge of the server's window too.
        // One day later is a row no panel can hold, which is asserted from the outside by
        // File_ARowOnTheFirstDayOfTheMonthAfterNext_IsDropped.
        var sections = File(new DateOnly(2026, 3, 10), At(2026, 4, 30));

        Rows(sections, ReminderSectionKey.NextMonth).Should().Equal(At(2026, 4, 30));
        Rows(sections, ReminderSectionKey.ThisMonth).Should().BeEmpty();
    }

    [Fact]
    public void File_OnTheLastDayOfAMonth_TomorrowLandsInNextMonthAndIsStillKept()
    {
        // 31 March: tomorrow is 1 April, so the row the family needs to see first is filed into the
        // right-hand panel. That is correct — the panel is a calendar fact — and it is precisely the
        // day on which a boundary off by one would drop tomorrow's event entirely.
        //
        // What this pins is the PANEL and nothing else; it never asks what the row says. The other
        // half of the month-end case — that the row still reads "Tomorrow" once filed over there —
        // is ReminderRowDisplay's, and is pinned by
        // ReminderRowDisplayTests.EventTime_OnTheLastDayOfAMonth_StillReadsTomorrowForTheFirstOfTheNext.
        // Both halves are needed: a correct panel with a row reading "Wed 1 Apr" is the same failure
        // to the family as a missing row.
        var sections = File(new DateOnly(2026, 3, 31), At(2026, 4, 1));

        Rows(sections, ReminderSectionKey.NextMonth).Should().Equal(At(2026, 4, 1));
        Rows(sections, ReminderSectionKey.ThisMonth).Should().BeEmpty();
    }

    [Fact]
    public void File_ARowOnTheFirstDayOfTheMonthAfterNext_IsDropped()
    {
        // The exclusive far edge: one day past the right panel's last day, which is the adjacent
        // case the fixture above asserts from the inside. Both are needed — the far edge is a span
        // of months, so widening it (AddMonths(1) → AddMonths(2)) leaves every other fixture here
        // passing and is caught only by a row sitting immediately outside it.
        var sections = File(new DateOnly(2026, 3, 10), At(2026, 5, 1));

        sections.SelectMany(s => s.Rows).Should().BeEmpty();
    }

    [Fact]
    public void File_ARowBeyondNextMonth_IsDropped()
    {
        // Well beyond the window the server returned, rather than one day past it. Dropping it is
        // correct; rendering it in the last panel would quietly misfile it.
        var sections = File(new DateOnly(2026, 3, 10), At(2026, 9, 1));

        sections.SelectMany(s => s.Rows).Should().BeEmpty();
    }

    [Fact]
    public void File_ARowBeforeToday_IsDroppedRatherThanForcedIntoThisMonth()
    {
        // Not a defensive case for something that cannot happen: the server's window opens at local
        // midnight in the zone FamilyHQ has STORED, while this filing reads the kiosk's own, and the
        // two diverge whenever the stored zone was chosen explicitly or came from a saved location
        // (see ReminderBucketing.File's remarks). A past row shown among upcoming ones is a worse
        // failure than a missing one: the family would act on it.
        var sections = File(new DateOnly(2026, 3, 10), At(2026, 3, 9));

        sections.SelectMany(s => s.Rows).Should().BeEmpty();
    }

    [Fact]
    public void File_ForAnInstantLateOnTheMonthsLastEvening_FilesByTheZoneLocalDayNotTheUtcDay()
    {
        // Dublin is UTC+1 from 29 March 2026, so 23:30Z on the 31st is already 00:30 on 1 April
        // there. Read in UTC the row would be filed as the last day of March — the left panel — and
        // the family would look for tomorrow's event in the wrong column.
        var dublin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");
        var lateOnTheLastEvening = new DateTimeOffset(2026, 3, 31, 23, 30, 0, TimeSpan.Zero);

        var sections = ReminderBucketing.File(
            new[] { lateOnTheLastEvening }, i => i, new DateOnly(2026, 3, 31), dublin);

        sections.Single(s => s.Key == ReminderSectionKey.ThisMonth).Rows
            .Should().BeEmpty("in Dublin that instant is already April");
        sections.Single(s => s.Key == ReminderSectionKey.NextMonth).Rows
            .Should().Equal(lateOnTheLastEvening);
    }

    [Fact]
    public void File_SortsRowsWithinAPanelByTheInstantTheyAreFiledBy()
    {
        // Whatever the selector reads, which for the reminders timeline is each event's own start —
        // so a panel reads top to bottom in the order its events actually happen. It matters more
        // now than it did: a panel spans a whole month, and the first six rows of it are what the
        // collapsed view shows.
        var sections = File(
            new DateOnly(2026, 3, 10),
            At(2026, 3, 25, 17), At(2026, 3, 10, 8), At(2026, 3, 12, 12));

        Rows(sections, ReminderSectionKey.ThisMonth).Should().Equal(
            At(2026, 3, 10, 8), At(2026, 3, 12, 12), At(2026, 3, 25, 17));
    }
}
