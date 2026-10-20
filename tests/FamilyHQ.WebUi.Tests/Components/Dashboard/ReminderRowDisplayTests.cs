using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// What a Reminders-timeline row leads with, and which day a tap on it opens. Pinned here rather than
// left to be read by eye: this view once formatted every time in raw UTC while every other view
// converted to the household zone, and it shipped through both test layers because the unit test
// pinned the wrong reading under a UTC CI and the E2E asserted only the shape of a time.
public class ReminderRowDisplayTests
{
    // Dublin, not a fixed offset built in this file: a real zone is the only kind that changes
    // offset partway through a day, which is the behaviour the transition fixture below is here for.
    // Looking it up by IANA id is established in this suite (ReminderBucketingTests,
    // ReminderPingCalculatorTests do the same) and resolves even though CI runs
    // globalization-invariant.
    //
    // Most fixtures below are summer dates, when Dublin is UTC+1, so the assertion genuinely
    // exercises the conversion: a value formatted in raw UTC reads exactly one hour early and fails
    // rather than passing either way. Two are not, each for its own reason. A year rollover is
    // necessarily in winter, when Dublin IS UTC+0 and the conversion is a no-op, so what that
    // fixture pins is the date arithmetic alone. And the 29 March fixture straddles a transition:
    // the offset changes BETWEEN its two instants, which is the one thing no fixed offset can
    // imitate, whichever one is chosen.
    private static readonly TimeZoneInfo Dublin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");

    [Fact]
    public void EventTime_ForAnEventHappeningToday_LeadsWithTodayAndThenTheTime() =>
        // 09:05+00:00 is what Npgsql hands back for a timestamptz regardless of where the event
        // actually is — 10:05 in Dublin on 15 June (UTC+1). Pinning "10:05" rather than "09:05"
        // is what makes this fail against the raw-offset reading this view once had.
        ReminderRowDisplay.EventTime(
                new DateTimeOffset(2026, 6, 15, 9, 5, 0, TimeSpan.Zero), isAllDay: false, Dublin,
                today: new DateOnly(2026, 6, 15))
            .Should().Be("Today · 10:05");

    [Fact]
    public void EventTime_ForAnEventHappeningTomorrow_LeadsWithTomorrow() =>
        ReminderRowDisplay.EventTime(
                new DateTimeOffset(2026, 6, 16, 20, 30, 0, TimeSpan.Zero), isAllDay: false, Dublin,
                today: new DateOnly(2026, 6, 15))
            .Should().Be("Tomorrow · 21:30");

    [Fact]
    public void EventTime_OnTheLastDayOfAMonth_StillReadsTomorrowForTheFirstOfTheNext() =>
        // The month-end case, deterministically. "Tomorrow" is read off the row's own date against
        // today, never off the panel holding it — and on a month's last day those two disagree:
        // ReminderBucketing files this row under "Next month" while the label still has to say
        // "Tomorrow". A label computed by comparing day numbers within a month instead of comparing
        // whole dates passes every other fixture in this file and reads "Wed 1 Apr" here, on the one
        // day of the month the family can least afford to lose tomorrow's row to a bare date.
        ReminderRowDisplay.EventTime(
                new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.Zero), isAllDay: false, Dublin,
                today: new DateOnly(2026, 3, 31))
            .Should().Be("Tomorrow · 09:00");

    [Fact]
    public void EventTime_OnTheLastDayOfAYear_StillReadsTomorrowForTheFirstOfTheNext() =>
        // The same boundary one level up, and not covered by the fixture above: 31 December to 1
        // January rolls the YEAR, so a label reasoning in day-of-year (90 → 91 is fine on 31 March,
        // 365 → 1 is not) passes that one and fails this one. Dublin is UTC+0 in January, so this
        // fixture pins the date arithmetic rather than the conversion — the conversion is pinned by
        // the summer fixtures either side of it.
        ReminderRowDisplay.EventTime(
                new DateTimeOffset(2027, 1, 1, 9, 0, 0, TimeSpan.Zero), isAllDay: false, Dublin,
                today: new DateOnly(2026, 12, 31))
            .Should().Be("Tomorrow · 09:00");

    [Fact]
    public void EventTime_ForTwoInstantsOnADaylightSavingTransitionDay_ReadsEachInTheOffsetThenInForce()
    {
        // 29 March 2026 is the day Irish clocks go forward: Dublin is UTC+0 until 01:00Z and UTC+1
        // from then on, so the offset in force is not a property of the DAY, only of the instant.
        // Both fixtures read "00:30" and that is the point — the first is 00:30 on the 29th because
        // the offset was still zero, the second is 00:30 on the 30th because by then it was +1. A
        // fixed-offset stand-in for the zone cannot produce both: +0 makes the second read
        // "Today · 23:30" (the UTC day, the bug this view once shipped), +1 makes the first read
        // "Today · 01:30".
        //
        // There is no live defect here — ConvertTime(DateTimeOffset, …) and DateOnly.AddDays are
        // both transition-safe — but nothing else in this suite sits on a transition at all, and a
        // boundary with no fixture on it is a boundary the next rewrite is free to get wrong.
        var beforeTheClocksGoForward = ReminderRowDisplay.EventTime(
            new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero), isAllDay: false, Dublin,
            today: new DateOnly(2026, 3, 29));
        var afterTheClocksGoForward = ReminderRowDisplay.EventTime(
            new DateTimeOffset(2026, 3, 29, 23, 30, 0, TimeSpan.Zero), isAllDay: false, Dublin,
            today: new DateOnly(2026, 3, 29));

        beforeTheClocksGoForward.Should().Be("Today · 00:30");
        afterTheClocksGoForward.Should().Be("Tomorrow · 00:30");
    }

    [Fact]
    public void EventTime_ForAnEventFurtherOut_LeadsWithTheDayAndDate() =>
        // Two days out and beyond there is no relative word a family reads at a glance, so the row
        // names the day outright.
        ReminderRowDisplay.EventTime(
                new DateTimeOffset(2026, 10, 7, 7, 0, 0, TimeSpan.Zero), isAllDay: false, Dublin,
                today: new DateOnly(2026, 10, 5))
            .Should().Be("Wed 7 Oct · 08:00");

    [Fact]
    public void EventTime_ForAnAllDayEventToday_SaysAllDayRatherThanMidnight() =>
        // An all-day event's stored start is a date boundary. Rendering it as a time would tell the
        // family the event "starts 00:00", which is not a thing that happens.
        ReminderRowDisplay.EventTime(
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), isAllDay: true, Dublin,
                today: new DateOnly(2026, 6, 15))
            .Should().Be("Today · all day");

    [Fact]
    public void EventTime_ForAnAllDayEventFurtherOut_KeepsReadingAllDayAfterTheDate() =>
        ReminderRowDisplay.EventTime(
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), isAllDay: true, Dublin,
                today: new DateOnly(2026, 6, 1))
            .Should().Be("Mon 15 Jun · all day");

    [Fact]
    public void EventTime_ForALateEveningUtcInstantThatIsAlreadyTodayLocally_ReadsToday() =>
        // 23:30 on 14 June in UTC is 00:30 on the 15th in Dublin. Skipping the conversion would not
        // just shift the time by an hour: the row would read "Sun 14 Jun · 23:30" for an event the
        // family is about to leave the house for, and would disagree with the panel it is filed
        // under, which ReminderBucketing reads through the same conversion.
        ReminderRowDisplay.EventTime(
                new DateTimeOffset(2026, 6, 14, 23, 30, 0, TimeSpan.Zero), isAllDay: false, Dublin,
                today: new DateOnly(2026, 6, 15))
            .Should().Be("Today · 00:30");

    [Fact]
    public void EventTime_ForADateBeforeToday_FallsBackToTheDatedFormRatherThanSayingYesterday() =>
        // No panel can hold a past row — the server's window opens at local midnight and
        // ReminderBucketing drops anything earlier — so "Yesterday" would be a label for a row that
        // cannot be rendered. Inventing one is a lie waiting for a window change to expose it; the
        // dated form is true whatever the date turns out to be.
        ReminderRowDisplay.EventTime(
                new DateTimeOffset(2026, 6, 14, 9, 5, 0, TimeSpan.Zero), isAllDay: false, Dublin,
                today: new DateOnly(2026, 6, 15))
            .Should().Be("Sun 14 Jun · 10:05");

    [Fact]
    public void EventTime_ForAnEventFurtherOut_ReadsTheDayNameAndDateInTheGivenZoneNotInUtc() =>
        // 23:30 on 15 June in UTC is 00:30 on the 16th in Dublin, so the DAY NAME and the DATE are
        // both wrong if the conversion is skipped — not just the time.
        ReminderRowDisplay.EventTime(
                new DateTimeOffset(2026, 6, 15, 23, 30, 0, TimeSpan.Zero), isAllDay: false, Dublin,
                today: new DateOnly(2026, 6, 1))
            .Should().Be("Tue 16 Jun · 00:30");

    [Fact]
    public void EventTime_ForAnEventFurtherOut_NamesTheMonthSoTwoDatesSharingADayNumberDoNotReadAlike()
    {
        // Why the format is `ddd d MMM` and not `ddd d`. "This month" and "Next month" sit side by
        // side, so the last row of one and the first row of the other are one glance apart: "Wed 30"
        // and "Thu 1" say nothing about which month either is in. A row that reads correctly only
        // once the reader has resolved it against the panel heading above is a row that reads wrongly
        // at a glance, which is the only way this view is ever read. Both fixtures are in Dublin's
        // summer offset, so the times match and only the date part differs.
        var september = ReminderRowDisplay.EventTime(
            new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero), isAllDay: false, Dublin,
            today: new DateOnly(2026, 9, 1));
        var october = ReminderRowDisplay.EventTime(
            new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero), isAllDay: false, Dublin,
            today: new DateOnly(2026, 9, 1));

        september.Should().Be("Fri 4 Sep · 10:00");
        october.Should().Be("Sun 4 Oct · 10:00");
    }

    [Fact]
    public void LocalDate_ForALateEveningEvent_IsTheDayTheFamilySeesNotTheUtcOne() =>
        // The drill-down target. 23:30Z on 15 June is 00:30 on the 16th in Dublin, so passing the
        // raw UTC date would open the Day view on the day BEFORE the one the row shows and the
        // panel it sits under.
        ReminderRowDisplay.LocalDate(new DateTimeOffset(2026, 6, 15, 23, 30, 0, TimeSpan.Zero), Dublin)
            .Should().Be(new DateTime(2026, 6, 16));

    [Fact]
    public void LocalDate_CarriesNoTimeOfDay() =>
        // SwitchToView takes a DateTime and compares its month and year against the loaded month,
        // then stores it as _selectedDate; a stray time component would serve no purpose there.
        ReminderRowDisplay.LocalDate(new DateTimeOffset(2026, 6, 15, 9, 5, 0, TimeSpan.Zero), Dublin)
            .Should().Be(new DateTime(2026, 6, 15));

    [Fact]
    public void LocalDate_AgreesWithTheDateTheRowShows()
    {
        // The one invariant tying the two members together: a tap must open the day printed on the
        // row. Asserted against the same instant rather than two hand-picked expectations, so the
        // pair cannot drift apart by one of them being updated alone. `today` is far enough away
        // that the row carries the dated form rather than "Today" or "Tomorrow", which is the only
        // form there is a date on the row to compare against at all.
        var start = new DateTimeOffset(2026, 6, 15, 23, 30, 0, TimeSpan.Zero);

        var shown = ReminderRowDisplay.EventTime(
            start, isAllDay: false, Dublin, today: new DateOnly(2026, 6, 1));
        var opened = ReminderRowDisplay.LocalDate(start, Dublin);

        shown.Should().StartWith(
            opened.ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture),
            "the row leads with the same day a tap on it opens.");
    }
}
