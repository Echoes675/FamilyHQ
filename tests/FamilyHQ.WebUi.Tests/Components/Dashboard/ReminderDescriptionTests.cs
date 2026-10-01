using FamilyHQ.Core.Models;
using FamilyHQ.Core.Validators;
using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// The wording the Reminders tab shows for a stored reminder. What is pinned here is that every value
// Google can hold reads truthfully — including the ones the kiosk's own form cannot produce, because
// a phone can set them and the family still has to be able to see them.
public class ReminderDescriptionTests
{
    [Theory]
    [InlineData(0, "As the event starts")]
    [InlineData(10, "10 minutes before")]
    [InlineData(1, "1 minute before")]
    [InlineData(47, "47 minutes before")]
    [InlineData(60, "1 hour before")]
    [InlineData(90, "90 minutes before")]
    [InlineData(1440, "1 day before")]
    [InlineData(10080, "1 week before")]
    [InlineData(40320, "4 weeks before")]
    public void Timing_ForATimedEvent_UsesTheLargestUnitThatDividesEvenly(int minutes, string expected)
    {
        ReminderDescription.Timing(minutes, isAllDay: false).Should().Be(expected);
    }

    [Theory]
    [InlineData(900, "1 day before at 09:00")]
    [InlineData(1860, "2 days before at 17:00")]
    [InlineData(30, "1 day before at 23:30")]
    public void Timing_ForAnAllDayEvent_ReadsAsTheDayAndTimeItFires(int minutes, string expected)
    {
        // 30 minutes on an all-day event is a notification at 23:30 the night before, which is what
        // Google materialises from a 30-minute calendar default. It must not read as "30 minutes
        // before" anything.
        ReminderDescription.Timing(minutes, isAllDay: true).Should().Be(expected);
    }

    [Fact]
    public void Timing_ForAnAllDayEventAtTheDaysMidnight_SaysSoRatherThanZeroDaysBefore()
    {
        // Google clamps a negative offset to 0 and stores it. The form cannot reach this, but a value
        // that arrived this way notifies the family at midnight and has to read that way.
        ReminderDescription.Timing(0, isAllDay: true).Should().Be("At midnight as the day begins");
    }

    [Fact]
    public void Timing_ForAReminderAfterTheStart_SaysAfterRatherThanNegative()
    {
        ReminderDescription.Timing(-15, isAllDay: false).Should().Be("15 minutes after the event starts");
    }

    [Theory]
    [InlineData(EventRemindersValidator.PopupMethod, ReminderDescription.PopupLabel)]
    [InlineData(EventRemindersValidator.EmailMethod, ReminderDescription.EmailLabel)]
    public void MethodLabel_ForAMethodGoogleNames_UsesTheFamiliarWord(string method, string expected)
    {
        ReminderDescription.MethodLabel(method).Should().Be(expected);
    }

    [Fact]
    public void MethodLabel_ForAMethodTheKioskCannotCreate_ShowsItAsGoogleSentIt()
    {
        // A phone can set a method Google's own write path silently drops. Showing it verbatim is the
        // only honest option: the kiosk neither invents a word for it nor hides the reminder.
        ReminderDescription.MethodLabel("sms").Should().Be("sms");
    }

    [Fact]
    public void For_CombinesWhenItFiresWithHowItIsDelivered()
    {
        ReminderDescription
            .For(new EventReminder(EventRemindersValidator.EmailMethod, 1440), isAllDay: false)
            .Should().Be($"1 day before · {ReminderDescription.EmailLabel}");
    }
}
