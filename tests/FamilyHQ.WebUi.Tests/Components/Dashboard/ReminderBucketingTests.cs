using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

/// <summary>
/// Filing a row into exactly one of the two panels. Every case in here is a date on which the naive
/// reading of "this month" sends a row to the wrong panel, or loses it altogether — which on a wall
/// display means tomorrow's event vanishing on the one day of the month the family most needs it.
/// Which day a row is filed by is the caller's choice (the timeline passes the day the server put
/// the event on); these fixtures pass the date itself, so what they pin is the day arithmetic and
/// nothing else.
/// </summary>
public class ReminderBucketingTests
{
    // A row is just a date now: the server decided which day each event is on, so this class files by
    // a date and compares dates. The zone and the instant are gone from it — the conversion they
    // existed for happens once, server-side, and is pinned in EventWallClockTests.
    private static IReadOnlyList<ReminderSection<DateOnly>> File(DateOnly today, params DateOnly[] rows) =>
        ReminderBucketing.File(rows, r => r, today);

    private static IReadOnlyList<DateOnly> Rows(
        IReadOnlyList<ReminderSection<DateOnly>> sections, ReminderSectionKey key) =>
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
        var dates = new[]
        {
            new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 11), new DateOnly(2026, 3, 13),
            new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 8)
        };

        var sections = File(today, dates);

        sections.SelectMany(s => s.Rows).Should().HaveCount(dates.Length);
        sections.SelectMany(s => s.Rows).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void File_ARowHappeningToday_LandsInThisMonth()
    {
        // The whole point of the two-panel model: "This month" is inclusive of today. Under the
        // five-section model today had a section of its own, so "This month" started after the
        // current week ended — and on 51 days of a year there was no day left for it to claim.
        var sections = File(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 10));

        Rows(sections, ReminderSectionKey.ThisMonth).Should().Equal(new DateOnly(2026, 3, 10));
        Rows(sections, ReminderSectionKey.NextMonth).Should().BeEmpty();
    }

    [Fact]
    public void File_ARowOnTheLastDayOfThisMonth_LandsInThisMonth()
    {
        // The inclusive far edge of the left panel.
        var sections = File(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 31));

        Rows(sections, ReminderSectionKey.ThisMonth).Should().Equal(new DateOnly(2026, 3, 31));
        Rows(sections, ReminderSectionKey.NextMonth).Should().BeEmpty();
    }

    [Fact]
    public void File_ARowOnTheFirstDayOfNextMonth_LandsInNextMonth()
    {
        // The right panel's near edge, one day past the left panel's far edge — the boundary both
        // panels have to agree on, asserted from the other side.
        var sections = File(new DateOnly(2026, 3, 10), new DateOnly(2026, 4, 1));

        Rows(sections, ReminderSectionKey.NextMonth).Should().Equal(new DateOnly(2026, 4, 1));
        Rows(sections, ReminderSectionKey.ThisMonth).Should().BeEmpty();
    }

    [Fact]
    public void File_ARowOnTheLastDayOfNextMonth_LandsInNextMonth()
    {
        // The inclusive far edge of the right panel, and the far edge of the server's window too.
        // One day later is a row no panel can hold, which is asserted from the outside by
        // File_ARowOnTheFirstDayOfTheMonthAfterNext_IsDropped.
        var sections = File(new DateOnly(2026, 3, 10), new DateOnly(2026, 4, 30));

        Rows(sections, ReminderSectionKey.NextMonth).Should().Equal(new DateOnly(2026, 4, 30));
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
        var sections = File(new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 1));

        Rows(sections, ReminderSectionKey.NextMonth).Should().Equal(new DateOnly(2026, 4, 1));
        Rows(sections, ReminderSectionKey.ThisMonth).Should().BeEmpty();
    }

    [Fact]
    public void File_ARowOnTheFirstDayOfTheMonthAfterNext_IsDropped()
    {
        // The exclusive far edge: one day past the right panel's last day, which is the adjacent
        // case the fixture above asserts from the inside. Both are needed — the far edge is a span
        // of months, so widening it (AddMonths(1) → AddMonths(2)) leaves every other fixture here
        // passing and is caught only by a row sitting immediately outside it.
        var sections = File(new DateOnly(2026, 3, 10), new DateOnly(2026, 5, 1));

        sections.SelectMany(s => s.Rows).Should().BeEmpty();
    }

    [Fact]
    public void File_ARowBeyondNextMonth_IsDropped()
    {
        // Well beyond the window the server returned, rather than one day past it. Dropping it is
        // correct; rendering it in the last panel would quietly misfile it.
        var sections = File(new DateOnly(2026, 3, 10), new DateOnly(2026, 9, 1));

        sections.SelectMany(s => s.Rows).Should().BeEmpty();
    }

    [Fact]
    public void File_ARowBeforeToday_IsDroppedRatherThanForcedIntoThisMonth()
    {
        // Not a defensive case for something that cannot happen, though the reason has changed: the
        // server now sends both the date it filed each row against and the date it considers today,
        // so there is no second zone left to disagree in. What can still produce a past row is the
        // kiosk's own day being shifted forward in a lower environment, where the dev clock bridge
        // advances whole days without the server's clock moving. A past row shown among upcoming
        // ones is a worse failure than a missing one: the family would act on it.
        var sections = File(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 9));

        sections.SelectMany(s => s.Rows).Should().BeEmpty();
    }

    // The zone-local reading this suite used to pin here — an instant late on a month's last evening
    // being filed by the household day rather than the UTC one — is now
    // EventWallClockTests.Of_ForATimedEventJustAfterLocalMidnight_ReadsTheHouseholdDayNotTheUtcOne,
    // server-side, where the conversion happens once. It is named rather than simply dropped because
    // that boundary was a real defect, and the only record it ever was is the test for it.

    [Fact]
    public void File_PreservesTheOrderItWasGiven()
    {
        // This class no longer sorts. It cannot: it sees one date per row and the order within a day
        // depends on a time it is not given. The server sends rows in render order and
        // RemindersViewLogic.InDayOrder restates that order where the row type is known, so a sort
        // here would be a third opinion about it — and a wrong one, because it would put an all-day
        // row after a timed one on the same date.
        var today = new DateOnly(2026, 10, 5);
        var sections = File(today, today.AddDays(3), today, today.AddDays(1));

        // Equal, not ContainInOrder: the claim is that the panel holds exactly these rows in exactly
        // this order, and ContainInOrder would also accept a panel that had duplicated one of them.
        sections[0].Rows.Should().Equal(today.AddDays(3), today, today.AddDays(1));
    }
}
