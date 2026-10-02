using FamilyHQ.Core.Validators;
using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// The wording the Reminders timeline shows for one ping. Pinned here, not left to be read by eye,
// for the same reason ReminderDescriptionTests pins the edit modal's wording — a reminder shown with
// the wrong lead time or the wrong start is a production bug that nothing here would catch by itself.
public class ReminderRowDisplayTests
{
    [Fact]
    public void Lead_UnderAnHour_ReadsInMinutes() =>
        ReminderRowDisplay.Lead(30).Should().Be("30 min before");

    [Fact]
    public void Lead_OnAWholeHour_ReadsInHours() =>
        ReminderRowDisplay.Lead(120).Should().Be("2 hrs before");

    [Fact]
    public void Lead_OnASingleHour_IsSingular() =>
        ReminderRowDisplay.Lead(60).Should().Be("1 hr before");

    [Fact]
    public void Lead_OnAWholeDay_ReadsInDays() =>
        ReminderRowDisplay.Lead(1440).Should().Be("1 day before");

    [Fact]
    public void Lead_OnAnAwkwardValueTheKioskCannotCreate_StillReads() =>
        ReminderRowDisplay.Lead(47).Should().Be("47 min before");

    // Not in the pinned set, but the same cascade ReminderDescription.Timing uses supports weeks —
    // leaving it unverified here would be exactly the "read by eye" this test class exists to avoid.
    [Theory]
    [InlineData(10080, "1 wk before")]
    [InlineData(20160, "2 wks before")]
    public void Lead_OnAWholeWeek_ReadsInWeeks(int minutes, string expected) =>
        ReminderRowDisplay.Lead(minutes).Should().Be(expected);

    [Fact]
    public void Lead_AtTheMomentTheEventStarts_ReadsAsTheStart() =>
        // Not reachable from the kiosk's own form, but Google accepts a reminder at the start itself.
        ReminderRowDisplay.Lead(0).Should().Be("at the start");

    [Fact]
    public void Lead_AfterTheEventStarts_SaysAfterRatherThanNegative() =>
        // Google's schema declares no minimum, and a phone can set one of these even though the
        // kiosk's own form cannot.
        ReminderRowDisplay.Lead(-15).Should().Be("15 min after start");

    [Fact]
    public void StartsAt_ForATimedEvent_ShowsA24HourTime() =>
        ReminderRowDisplay.StartsAt(new DateTimeOffset(2026, 3, 10, 9, 5, 0, TimeSpan.Zero), isAllDay: false)
            .Should().Be("starts 09:05");

    [Fact]
    public void StartsAt_ForAnAllDayEvent_SaysAllDayRatherThanMidnight()
    {
        // An all-day event's stored start is a date boundary. Rendering it as a time would tell the
        // family the event "starts 00:00", which is not a thing that happens.
        ReminderRowDisplay.StartsAt(new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), isAllDay: true)
            .Should().Be("all day");
    }

    [Theory]
    [InlineData(EventRemindersValidator.PopupMethod, "🔔")]
    [InlineData(EventRemindersValidator.EmailMethod, "✉")]
    public void MethodIcon_ForAMethodGoogleNames_ShowsItsOwnGlyph(string method, string expected) =>
        ReminderRowDisplay.MethodIcon(method).Should().Be(expected);

    [Fact]
    public void MethodIcon_ForAMethodTheKioskCannotCreate_FallsBackWithoutThrowing() =>
        ReminderRowDisplay.MethodIcon("sms").Should().Be("•");
}
