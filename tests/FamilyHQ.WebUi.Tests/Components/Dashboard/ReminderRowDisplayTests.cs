using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// What a Reminders-timeline row leads with. There is no zone here any more and no instant: the
// server decides which day an event is on and sends the date and the time of day, so all this class
// does is choose between "Today", "Tomorrow" and the dated form, and render "all day" for a null
// time. The conversion this file used to pin now lives in EventWallClockTests, where one rule is
// stated once; what is pinned here is the date arithmetic, which is where the month-end and year-end
// cases bite.
public class ReminderRowDisplayTests
{
    [Fact]
    public void EventTime_ForAnEventHappeningToday_LeadsWithTodayAndThenTheTime() =>
        ReminderRowDisplay.EventTime(new DateOnly(2026, 6, 15), new TimeOnly(10, 5), today: new DateOnly(2026, 6, 15))
            .Should().Be("Today · 10:05");

    [Fact]
    public void EventTime_ForAnEventHappeningTomorrow_LeadsWithTomorrow() =>
        ReminderRowDisplay.EventTime(new DateOnly(2026, 6, 16), new TimeOnly(21, 30), today: new DateOnly(2026, 6, 15))
            .Should().Be("Tomorrow · 21:30");

    [Fact]
    public void EventTime_OnTheLastDayOfAMonth_StillReadsTomorrowForTheFirstOfTheNext() =>
        // The day the family can least afford to read as a bare date, and the one a cheaper
        // comparison (a day number, a day of year) gets wrong. It is also filed under "Next month" on
        // this date, and the label must not care.
        ReminderRowDisplay.EventTime(new DateOnly(2026, 4, 1), new TimeOnly(8, 0), today: new DateOnly(2026, 3, 31))
            .Should().Be("Tomorrow · 08:00");

    [Fact]
    public void EventTime_OnTheLastDayOfAYear_StillReadsTomorrowForTheFirstOfTheNext() =>
        // 365 → 1 is the case day-of-year arithmetic gets wrong while 90 → 91 passes.
        ReminderRowDisplay.EventTime(new DateOnly(2027, 1, 1), new TimeOnly(9, 0), today: new DateOnly(2026, 12, 31))
            .Should().Be("Tomorrow · 09:00");

    [Fact]
    public void EventTime_ForAnEventFurtherOut_LeadsWithTheDayAndDate() =>
        // Two days out there is no relative word a family reads at a glance, so the row names the day.
        ReminderRowDisplay.EventTime(new DateOnly(2026, 10, 7), new TimeOnly(8, 0), today: new DateOnly(2026, 10, 5))
            .Should().Be("Wed 7 Oct · 08:00");

    [Fact]
    public void EventTime_ForAnEventFurtherOut_NamesTheMonthSoTwoDatesSharingADayNumberDoNotReadAlike()
    {
        // "This month" and "Next month" sit side by side, so the last row of one and the first row of
        // the other are one glance apart. "Wed 30" beside "Thu 1" says nothing about which month
        // either is in, and a row that reads correctly only once the reader has resolved it against
        // the heading above reads wrongly at a glance — which is the only way this view is read.
        var endOfThisMonth = ReminderRowDisplay.EventTime(
            new DateOnly(2026, 9, 30), new TimeOnly(8, 0), today: new DateOnly(2026, 9, 1));
        var startOfNextMonth = ReminderRowDisplay.EventTime(
            new DateOnly(2026, 10, 1), new TimeOnly(8, 0), today: new DateOnly(2026, 9, 1));

        endOfThisMonth.Should().Be("Wed 30 Sep · 08:00");
        startOfNextMonth.Should().Be("Thu 1 Oct · 08:00");
    }

    [Fact]
    public void EventTime_ForAnAllDayEventToday_SaysAllDayRatherThanMidnight() =>
        // A null time is the all-day signal. Rendering it as a time would tell the family the event
        // "starts 00:00", which is the stored boundary read as a clock reading.
        ReminderRowDisplay.EventTime(new DateOnly(2026, 6, 15), null, today: new DateOnly(2026, 6, 15))
            .Should().Be("Today · all day");

    [Fact]
    public void EventTime_ForAnAllDayEventFurtherOut_KeepsReadingAllDayAfterTheDate() =>
        ReminderRowDisplay.EventTime(new DateOnly(2026, 10, 7), null, today: new DateOnly(2026, 10, 5))
            .Should().Be("Wed 7 Oct · all day");

    [Fact]
    public void EventTime_ForMidnightItself_ReadsAsATimeAndNotAsAllDay() =>
        // 00:00 and "no time at all" are different facts and must read differently: a timed event
        // genuinely starting at midnight exists, and this is the pair of cases a nullable time exists
        // to keep apart.
        ReminderRowDisplay.EventTime(new DateOnly(2026, 6, 15), new TimeOnly(0, 0), today: new DateOnly(2026, 6, 15))
            .Should().Be("Today · 00:00");

    [Fact]
    public void EventTime_ForADateBeforeToday_FallsBackToTheDatedFormRatherThanSayingYesterday() =>
        // There is deliberately no "Yesterday": ReminderBucketing drops a past row, so the label
        // would name a row that cannot be rendered. The dated form stays true whatever the date is.
        ReminderRowDisplay.EventTime(new DateOnly(2026, 10, 4), new TimeOnly(8, 0), today: new DateOnly(2026, 10, 5))
            .Should().Be("Sun 4 Oct · 08:00");
}
