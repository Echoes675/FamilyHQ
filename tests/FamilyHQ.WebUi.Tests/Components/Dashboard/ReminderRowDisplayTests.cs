using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// What a Reminders-timeline row leads with, and which day a tap on it opens. Pinned here rather than
// left to be read by eye: this view once formatted every time in raw UTC while every other view
// converted to the household zone, and it shipped through both test layers because the unit test
// pinned the wrong reading under a UTC CI and the E2E asserted only the shape of a time.
public class ReminderRowDisplayTests
{
    // Dublin, not a fixed offset built in this file: it is the zone the rest of the suite already
    // uses for the same DST transition (ReminderBucketingTests, ReminderPingCalculatorTests), and it
    // resolves in this suite without a tz-database dependency concern — see those files. Every
    // fixture below is a summer date, when Dublin is UTC+1, so every assertion genuinely exercises
    // the conversion: a value formatted in raw UTC would read exactly one hour early and these
    // fixtures would catch it rather than passing either way.
    private static readonly TimeZoneInfo Dublin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");

    [Fact]
    public void EventTime_ForATimedEvent_ConvertsToTheGivenZoneBeforeShowingA24HourTime() =>
        // 09:05+00:00 is what Npgsql hands back for a timestamptz regardless of where the event
        // actually is — 10:05 in Dublin on 15 June (UTC+1). Pinning "10:05" rather than "09:05"
        // is what makes this fail against the raw-offset reading this view once had.
        ReminderRowDisplay.EventTime(
                new DateTimeOffset(2026, 6, 15, 9, 5, 0, TimeSpan.Zero), isAllDay: false, Dublin, withDate: false)
            .Should().Be("10:05");

    [Fact]
    public void EventTime_ForAnAllDayEvent_SaysAllDayRatherThanMidnight() =>
        // An all-day event's stored start is a date boundary. Rendering it as a time would tell the
        // family the event "starts 00:00", which is not a thing that happens.
        ReminderRowDisplay.EventTime(
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), isAllDay: true, Dublin, withDate: false)
            .Should().Be("all day");

    [Fact]
    public void EventTime_WithTheDate_LeadsWithTheDayAndDateBeforeTheTime() =>
        // What the three further-out sections show, and what the near two do not: a pane spanning
        // several days renders three occurrences of one recurring series as three identical rows
        // without it.
        ReminderRowDisplay.EventTime(
                new DateTimeOffset(2026, 6, 15, 9, 5, 0, TimeSpan.Zero), isAllDay: false, Dublin, withDate: true)
            .Should().Be("Mon 15 Jun · 10:05");

    [Fact]
    public void EventTime_WithTheDateForAnAllDayEvent_KeepsReadingAllDay() =>
        ReminderRowDisplay.EventTime(
                new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), isAllDay: true, Dublin, withDate: true)
            .Should().Be("Mon 15 Jun · all day");

    [Fact]
    public void EventTime_WithTheDate_ReadsTheDayNameAndDateInTheGivenZoneNotInUtc() =>
        // 23:30 on 15 June in UTC is 00:30 on the 16th in Dublin, so the DAY NAME and the DATE are
        // both wrong if the conversion is skipped — not just the time. A row showing "Mon 15 Jun ·
        // 23:30" for this event would disagree with the section it is filed under, which
        // ReminderBucketing reads through the same conversion.
        ReminderRowDisplay.EventTime(
                new DateTimeOffset(2026, 6, 15, 23, 30, 0, TimeSpan.Zero), isAllDay: false, Dublin, withDate: true)
            .Should().Be("Tue 16 Jun · 00:30");

    [Fact]
    public void EventTime_WithTheDate_NamesTheMonthSoTwoDatesSharingADayNumberDoNotReadAlike()
    {
        // Why the format is `ddd d MMM` and not `ddd d`. This week is the one section whose span can
        // cross a month boundary, so two of its rows can sit in different months — and a reader
        // should not have to resolve a row against the pane heading above it to know which month it
        // names. Both fixtures are in Dublin's summer offset, so the times match and only the date
        // part differs.
        var september = ReminderRowDisplay.EventTime(
            new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero), isAllDay: false, Dublin, withDate: true);
        var october = ReminderRowDisplay.EventTime(
            new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero), isAllDay: false, Dublin, withDate: true);

        september.Should().Be("Fri 4 Sep · 10:00");
        october.Should().Be("Sun 4 Oct · 10:00");
    }

    [Fact]
    public void LocalDate_ForALateEveningEvent_IsTheDayTheFamilySeesNotTheUtcOne() =>
        // The drill-down target. 23:30Z on 15 June is 00:30 on the 16th in Dublin, so passing the
        // raw UTC date would open the Day view on the day BEFORE the one the row shows and the
        // section it sits under.
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
        // pair cannot drift apart by one of them being updated alone.
        var start = new DateTimeOffset(2026, 6, 15, 23, 30, 0, TimeSpan.Zero);

        var shown = ReminderRowDisplay.EventTime(start, isAllDay: false, Dublin, withDate: true);
        var opened = ReminderRowDisplay.LocalDate(start, Dublin);

        shown.Should().StartWith(
            opened.ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture),
            "the row leads with the same day a tap on it opens.");
    }
}
