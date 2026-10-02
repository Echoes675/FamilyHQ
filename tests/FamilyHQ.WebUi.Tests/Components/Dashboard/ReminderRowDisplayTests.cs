using FamilyHQ.Core.Validators;
using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// The wording the Reminders timeline shows for one row. Pinned here, not left to be read by eye,
// for the same reason ReminderDescriptionTests pins the edit modal's wording — a reminder shown with
// the wrong lead time or the wrong start is a production bug that nothing here would catch by itself.
public class ReminderRowDisplayTests
{
    // Dublin, not a fixed offset built in this file: it is the zone the rest of the suite already
    // uses for the same DST transition (ReminderBucketingTests, ReminderPingCalculatorTests), and it
    // resolves in this suite without a tz-database dependency concern — see those files. BST (UTC+1)
    // on 15 June means every EventTime value below genuinely exercises the conversion: an EventTime
    // that went back to formatting the raw +00:00 value would read exactly one hour early, and these
    // fixtures would catch that rather than passing either way.
    private static readonly TimeZoneInfo Dublin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");

    [Fact]
    public void Lead_UnderAnHour_ReadsInMinutes() =>
        ReminderRowDisplay.Lead(30, isAllDay: false).Should().Be("30 min before");

    [Fact]
    public void Lead_OnAWholeHour_ReadsInHours() =>
        ReminderRowDisplay.Lead(120, isAllDay: false).Should().Be("2 hrs before");

    [Fact]
    public void Lead_OnASingleHour_IsSingular() =>
        ReminderRowDisplay.Lead(60, isAllDay: false).Should().Be("1 hr before");

    [Fact]
    public void Lead_OnAWholeDay_ReadsInDays() =>
        ReminderRowDisplay.Lead(1440, isAllDay: false).Should().Be("1 day before");

    [Fact]
    public void Lead_OnAnAwkwardValueTheKioskCannotCreate_StillReads() =>
        ReminderRowDisplay.Lead(47, isAllDay: false).Should().Be("47 min before");

    // Not in the pinned set, but the same cascade ReminderDescription.Timing uses supports weeks —
    // leaving it unverified here would be exactly the "read by eye" this test class exists to avoid.
    [Theory]
    [InlineData(10080, "1 wk before")]
    [InlineData(20160, "2 wks before")]
    public void Lead_OnAWholeWeek_ReadsInWeeks(int minutes, string expected) =>
        ReminderRowDisplay.Lead(minutes, isAllDay: false).Should().Be(expected);

    [Fact]
    public void Lead_AtTheMomentTheEventStarts_ReadsAsTheStart() =>
        // Not reachable from the kiosk's own form, but Google accepts a reminder at the start itself.
        ReminderRowDisplay.Lead(0, isAllDay: false).Should().Be("at the start");

    [Fact]
    public void Lead_AfterTheEventStarts_SaysAfterRatherThanNegative() =>
        // Google's schema declares no minimum, and a phone can set one of these even though the
        // kiosk's own form cannot.
        ReminderRowDisplay.Lead(-15, isAllDay: false).Should().Be("15 min after start");

    [Theory]
    [InlineData(900, "1 day before at 09:00")]
    [InlineData(1860, "2 days before at 17:00")]
    [InlineData(30, "1 day before at 23:30")]
    public void Lead_ForAnAllDayEvent_ReadsAsTheDayAndTimeItFires(int minutes, string expected) =>
        // Same fixtures as ReminderDescriptionTests.Timing_ForAnAllDayEvent_ReadsAsTheDayAndTimeItFires
        // — Lead delegates to Timing for an all-day event, so this pins that it actually does rather
        // than merely compiling against it.
        ReminderRowDisplay.Lead(minutes, isAllDay: true).Should().Be(expected);

    [Fact]
    public void Lead_ForAnAllDayEvent_DoesNotMisreadTheOffsetAsHoursBeforeStart() =>
        // The regression this fix exists for: a single-argument Lead(420) reads "7 hrs before"
        // (420 / 60), dividing an all-day offset the way a timed one would. The value actually
        // means "1 day before at 17:00" — 420 minutes before local midnight on the event's first
        // day. Confirmed by hand before the fix: ReminderRowDisplay.Lead(420) (no isAllDay
        // parameter, as it existed before this change) returned "7 hrs before".
        ReminderRowDisplay.Lead(420, isAllDay: true).Should().Be("1 day before at 17:00");

    [Fact]
    public void Lead_ForAnAllDayEventAtTheDaysMidnight_SaysSoRatherThanZeroDaysBefore() =>
        // Google clamps a negative all-day offset to 0; Timing already reads that as midnight
        // rather than "0 days before", and Lead has to carry that through unchanged.
        ReminderRowDisplay.Lead(0, isAllDay: true).Should().Be("At midnight as the day begins");

    [Fact]
    public void EventTime_ForATimedEvent_ConvertsToTheGivenZoneBeforeShowingA24HourTime() =>
        // 09:05+00:00 is what Npgsql hands back for a timestamptz regardless of where the event
        // actually is — 10:05 in Dublin on 15 June (BST, UTC+1). Pinning "10:05" rather than "09:05"
        // is what makes this fail against the raw-offset reading this fixes.
        ReminderRowDisplay.EventTime(new DateTimeOffset(2026, 6, 15, 9, 5, 0, TimeSpan.Zero), isAllDay: false, Dublin)
            .Should().Be("10:05");

    [Fact]
    public void EventTime_ForAnAllDayEvent_SaysAllDayRatherThanMidnight()
    {
        // An all-day event's stored start is a date boundary. Rendering it as a time would tell the
        // family the event "starts 00:00", which is not a thing that happens. The zone is irrelevant
        // to this branch — passed anyway because the parameter is required — so Dublin here is just
        // "some zone", not a fixture that matters to the assertion.
        ReminderRowDisplay.EventTime(new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), isAllDay: true, Dublin)
            .Should().Be("all day");
    }

    [Fact]
    public void ReminderSummary_WithOneReminder_IsSingularAndNamesItsLead() =>
        ReminderRowDisplay.ReminderSummary(reminderCount: 1, nextReminderMinutes: 120, isAllDay: false)
            .Should().Be("1 reminder · next 2 hrs before");

    [Fact]
    public void ReminderSummary_WithSeveralReminders_IsPluralAndNamesTheSoonestOnesLead() =>
        // The soonest to fire is whichever reminder the caller passes as "next" — ReminderSummary
        // itself does not pick it, it only renders it (RemindersController.RowFor does the picking).
        ReminderRowDisplay.ReminderSummary(reminderCount: 3, nextReminderMinutes: 30, isAllDay: false)
            .Should().Be("3 reminders · next 30 min before");

    [Fact]
    public void ReminderSummary_ForAnAllDayEventsNextReminder_UsesTheAllDayWording() =>
        // Delegates to Lead, which reads an all-day offset as days-before-at-a-time rather than as
        // raw minutes — see Lead's own fixtures for why 420 means "1 day before at 17:00".
        ReminderRowDisplay.ReminderSummary(reminderCount: 2, nextReminderMinutes: 420, isAllDay: true)
            .Should().Be("2 reminders · next 1 day before at 17:00");

    [Fact]
    public void ReminderSummary_WithEveryReminderAlreadySent_SaysSoRatherThanNamingALead() =>
        // The ordinary state of a row on the day of its own event. The row still belongs on the
        // timeline — it is filed by the event's start, not by a pending notification — so the summary
        // reports the reminders as sent rather than leaving the sentence half-finished.
        ReminderRowDisplay.ReminderSummary(reminderCount: 3, nextReminderMinutes: null, isAllDay: false)
            .Should().Be("3 reminders · all sent");

    [Fact]
    public void ReminderSummary_WithOneReminderAlreadySent_StaysSingular() =>
        ReminderRowDisplay.ReminderSummary(reminderCount: 1, nextReminderMinutes: null, isAllDay: true)
            .Should().Be("1 reminder · all sent");

    [Theory]
    [InlineData(EventRemindersValidator.PopupMethod, "🔔")]
    [InlineData(EventRemindersValidator.EmailMethod, "✉")]
    public void MethodIcon_ForAMethodGoogleNames_ShowsItsOwnGlyph(string method, string expected) =>
        ReminderRowDisplay.MethodIcon(method).Should().Be(expected);

    [Fact]
    public void MethodIcon_ForAMethodTheKioskCannotCreate_FallsBackWithoutThrowing() =>
        ReminderRowDisplay.MethodIcon("sms").Should().Be("•");
}
