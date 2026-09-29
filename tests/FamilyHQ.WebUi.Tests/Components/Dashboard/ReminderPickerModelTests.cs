using FamilyHQ.Core.Models;
using FamilyHQ.Core.Validators;
using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// The reminder picker's whole logic lives in a plain class so it can be tested without rendering the
// tab — the project has no bUnit. What is pinned here is what Google's API actually does: three
// states that must never collapse into each other (collapsing any two writes the wrong thing back to
// Google), an all-day offset measured backwards from local midnight, and values Google supplied that
// the kiosk must display rather than "correct".
public class ReminderPickerModelTests
{
    private const int NineAmAsMinutes = 9 * 60;

    // A plausible calendar default: Google sends a bare array for a calendar, so the client stores it
    // as an explicit list.
    private static EventReminders CalendarDefault() => EventReminders.Explicit(
    [
        new EventReminder(EventRemindersValidator.PopupMethod, 30),
        new EventReminder(EventRemindersValidator.EmailMethod, 1440)
    ]);

    private static ReminderPickerModel Timed(EventReminders? reminders) =>
        ReminderPickerModel.From(reminders, CalendarDefault(), isAllDay: false);

    private static ReminderPickerModel AllDay(EventReminders? reminders) =>
        ReminderPickerModel.From(reminders, CalendarDefault(), isAllDay: true);

    // --- The three states, on the way in --------------------------------------------------------

    [Fact]
    public void From_WhenTheEventInheritsTheCalendarDefault_FollowsItAndHasNotChanged()
    {
        var model = Timed(EventReminders.InheritsCalendarDefault);

        model.State.Should().Be(ReminderPickerState.FollowsCalendarDefault);
        model.FollowsCalendarDefault.Should().BeTrue();
        model.Overrides.Should().BeEmpty();
        model.HasChanged.Should().BeFalse();
    }

    [Fact]
    public void From_WhenTheEventCarriesItsOwnReminders_KeepsThemInAStableDisplayOrder()
    {
        // Google returns the array in an order of its own choosing, so the picker imposes one:
        // soonest first, then by method, so the list does not reshuffle between syncs.
        var model = Timed(EventReminders.Explicit(
        [
            new EventReminder(EventRemindersValidator.PopupMethod, 1440),
            new EventReminder(EventRemindersValidator.PopupMethod, 30),
            new EventReminder(EventRemindersValidator.EmailMethod, 1440)
        ]));

        model.State.Should().Be(ReminderPickerState.Explicit);
        model.Overrides.Should().Equal(
            new EventReminder(EventRemindersValidator.PopupMethod, 30),
            new EventReminder(EventRemindersValidator.EmailMethod, 1440),
            new EventReminder(EventRemindersValidator.PopupMethod, 1440));
        model.HasChanged.Should().BeFalse();
    }

    [Fact]
    public void From_WhenTheEventHasRemindersTurnedOff_IsNoneAndNotConfusedWithFollowingTheDefault()
    {
        var model = Timed(EventReminders.ExplicitlyNone);

        model.State.Should().Be(ReminderPickerState.ExplicitlyNone);
        model.FollowsCalendarDefault.Should().BeFalse();
        model.Overrides.Should().BeEmpty();
        model.HasChanged.Should().BeFalse();
    }

    [Fact]
    public void From_WhenRemindersHaveNeverBeenSynced_FollowsTheCalendarDefault()
    {
        // Null is "we have not read this event's reminders yet", not a fourth state the picker offers.
        // A timed event with no reminders object of its own is one that follows the calendar, so that
        // is the safe reading — and it leaves HasChanged false, so an ordinary edit sends nothing.
        var model = Timed(null);

        model.State.Should().Be(ReminderPickerState.FollowsCalendarDefault);
        model.HasChanged.Should().BeFalse();
    }

    [Fact]
    public void From_WithAValueTheKioskCouldNotHaveCreated_PreservesItRatherThanNormalisingIt()
    {
        // 47 minutes matches no control the kiosk offers and "sms" is not a method it can write, but
        // both can arrive from a phone. Google is the authority on its own data: showing it unchanged
        // is the requirement, and "correcting" it here would be the same class of bug as overwriting
        // an event's time zone with a local setting.
        var fromAPhone = EventReminders.Explicit(
        [
            new EventReminder("sms", 47)
        ]);

        var model = Timed(fromAPhone);

        model.Overrides.Should().Equal(new EventReminder("sms", 47));
        model.HasChanged.Should().BeFalse();
        model.ToEventReminders().SameAs(fromAPhone).Should().BeTrue();
    }

    // --- Moving between the states -------------------------------------------------------------

    [Fact]
    public void StopUsingCalendarDefault_CopiesTheCalendarsOwnRemindersInAsEditableEntries()
    {
        // The Google Calendar app pre-fills the defaults when you switch inheritance off, rather than
        // dropping the family onto an empty list they did not ask for.
        var model = Timed(EventReminders.InheritsCalendarDefault);

        model.StopUsingCalendarDefault();

        model.FollowsCalendarDefault.Should().BeFalse();
        model.State.Should().Be(ReminderPickerState.Explicit);
        model.Overrides.Should().Equal(
            new EventReminder(EventRemindersValidator.PopupMethod, 30),
            new EventReminder(EventRemindersValidator.EmailMethod, 1440));
        model.HasChanged.Should().BeTrue();
    }

    [Fact]
    public void StopUsingCalendarDefault_WhenTheCalendarHasNoDefaultsStored_LeavesAnEmptyList()
    {
        var model = ReminderPickerModel.From(
            EventReminders.InheritsCalendarDefault, calendarDefault: null, isAllDay: false);

        model.StopUsingCalendarDefault();

        model.State.Should().Be(ReminderPickerState.ExplicitlyNone);
    }

    [Fact]
    public void UseCalendarDefault_AfterEdits_RestoresInheritanceAndDiscardsThem()
    {
        var model = Timed(EventReminders.InheritsCalendarDefault);
        model.StopUsingCalendarDefault();
        model.TryAdd(new EventReminder(EventRemindersValidator.PopupMethod, 5)).Should().BeTrue();

        model.UseCalendarDefault();

        model.State.Should().Be(ReminderPickerState.FollowsCalendarDefault);
        model.Overrides.Should().BeEmpty();
        model.HasChanged.Should().BeFalse();
    }

    [Fact]
    public void Remove_WhenItTakesTheLastReminder_LandsOnNoneRatherThanTheCalendarDefault()
    {
        var model = Timed(EventReminders.Explicit([new EventReminder(EventRemindersValidator.PopupMethod, 30)]));

        model.Remove(new EventReminder(EventRemindersValidator.PopupMethod, 30)).Should().BeTrue();

        model.State.Should().Be(ReminderPickerState.ExplicitlyNone);
        model.FollowsCalendarDefault.Should().BeFalse();
        model.HasChanged.Should().BeTrue();
    }

    // --- HasChanged: the single gate on whether reminders are sent at all -----------------------

    [Fact]
    public void HasChanged_WhenAnEditIsUndone_IsFalseAgainSoANoOpVisitSendsNothing()
    {
        var opened = EventReminders.Explicit([new EventReminder(EventRemindersValidator.PopupMethod, 30)]);
        var model = Timed(opened);
        var added = new EventReminder(EventRemindersValidator.EmailMethod, 60);

        model.TryAdd(added).Should().BeTrue();
        model.HasChanged.Should().BeTrue();

        model.Remove(added).Should().BeTrue();

        model.HasChanged.Should().BeFalse();
    }

    [Fact]
    public void HasChanged_WhenOnlyTheFormControlsMove_IsFalseUntilSomethingIsAdded()
    {
        var model = Timed(EventReminders.Explicit([new EventReminder(EventRemindersValidator.PopupMethod, 30)]));

        model.Amount = 15;
        model.Unit = ReminderUnit.Hours;
        model.DaysBefore = 3;
        model.TimeOfDay = new TimeOnly(18, 0);
        model.Method = EventRemindersValidator.EmailMethod;

        model.HasChanged.Should().BeFalse();
    }

    [Fact]
    public void HasChanged_WhenTheSameRemindersArriveInADifferentOrder_IsFalse()
    {
        // Google reorders the array, so an order-sensitive comparison would make every reopen look
        // like an edit and rewrite reminders on an unrelated save.
        var opened = EventReminders.Explicit(
        [
            new EventReminder(EventRemindersValidator.EmailMethod, 1440),
            new EventReminder(EventRemindersValidator.PopupMethod, 30)
        ]);

        Timed(opened).HasChanged.Should().BeFalse();
    }

    // --- Limits the picker must make unreachable rather than leave to the server ----------------

    [Fact]
    public void TryAdd_WhenFiveRemindersAreAlreadySet_IsRefused()
    {
        // A sixth override is the one thing Google rejects outright
        // (400 eventRemindersCountExceedsLimit), so the picker must not offer it.
        var model = Timed(EventReminders.Explicit(
        [
            new EventReminder(EventRemindersValidator.PopupMethod, 5),
            new EventReminder(EventRemindersValidator.PopupMethod, 10),
            new EventReminder(EventRemindersValidator.PopupMethod, 15),
            new EventReminder(EventRemindersValidator.PopupMethod, 20),
            new EventReminder(EventRemindersValidator.PopupMethod, 25)
        ]));

        model.IsFull.Should().BeTrue();
        model.TryAdd(new EventReminder(EventRemindersValidator.PopupMethod, 30)).Should().BeFalse();
        model.Overrides.Should().HaveCount(EventRemindersValidator.MaxOverrides);
        model.HasChanged.Should().BeFalse();
    }

    [Fact]
    public void TryAdd_WithAMethodAndMinutesAlreadySet_IsRefusedRatherThanSilentlyCollapsed()
    {
        // Google de-duplicates silently with a 200. Collapsing here too would make a tap on Add do
        // nothing visible, which is indistinguishable from a broken button — so refuse and let the
        // tab say the reminder is already set.
        var model = Timed(EventReminders.Explicit([new EventReminder(EventRemindersValidator.PopupMethod, 30)]));

        model.TryAdd(new EventReminder(EventRemindersValidator.PopupMethod, 30)).Should().BeFalse();

        model.Overrides.Should().HaveCount(1);
        model.HasChanged.Should().BeFalse();
    }

    [Theory]
    [InlineData("sms")]
    [InlineData("Popup")]
    [InlineData("")]
    public void TryAdd_WithAMethodTheKioskCannotWrite_IsRefused(string method)
    {
        // Google drops a method it does not recognise, with a 200, leaving the event with NO
        // reminder while the request looked successful. The kiosk's own refusal is the only thing
        // between the family and a reminder that was never stored. Case matters: Google's values are
        // "popup" and "email" exactly.
        var model = Timed(EventReminders.ExplicitlyNone);

        model.TryAdd(new EventReminder(method, 30)).Should().BeFalse();

        model.Overrides.Should().BeEmpty();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(EventRemindersValidator.MaxMinutes + 1)]
    public void TryAdd_WithMinutesOutsideWhatGoogleStores_IsRefused(int minutes)
    {
        // Google clamps rather than rejecting: a negative becomes 0 and anything above the ceiling
        // becomes the ceiling, both with a 200. Refusing means the family is never shown a reminder
        // Google rewrote behind their back.
        var model = Timed(EventReminders.ExplicitlyNone);

        model.TryAdd(new EventReminder(EventRemindersValidator.PopupMethod, minutes)).Should().BeFalse();

        model.Overrides.Should().BeEmpty();
    }

    [Fact]
    public void TryAdd_WhileStillFollowingTheCalendarDefault_Throws()
    {
        // An event either follows the calendar or replaces it — Google answers a body carrying both
        // with 400 cannotUseDefaultRemindersAndSpecifyOverride. Inheritance has to be switched off
        // first, which is also what pre-fills the defaults, so reaching here is a caller bug.
        var model = Timed(EventReminders.InheritsCalendarDefault);

        var act = () => model.TryAdd(new EventReminder(EventRemindersValidator.PopupMethod, 30));

        act.Should().Throw<InvalidOperationException>();
    }

    // --- The all-day form: a negative offset is unreachable, not merely rejected ----------------

    [Fact]
    public void TryAddSelectedReminder_OnAnAllDayEventOneDayBeforeAtNine_Stores900Minutes()
    {
        var model = AllDay(EventReminders.ExplicitlyNone);
        model.DaysBefore = 1;
        model.TimeOfDay = new TimeOnly(9, 0);

        model.TryAddSelectedReminder().Should().BeTrue();

        // Google measures an all-day reminder backwards from local midnight on the first day:
        // 24 * 60 - 9 * 60.
        model.Overrides.Should().Equal(new EventReminder(EventRemindersValidator.PopupMethod, 900));
    }

    [Fact]
    public void TryAddSelectedReminder_OnAnAllDayEventTwoDaysBeforeAtFive_Stores1860Minutes()
    {
        var model = AllDay(EventReminders.ExplicitlyNone);
        model.DaysBefore = 2;
        model.TimeOfDay = new TimeOnly(17, 0);

        model.TryAddSelectedReminder().Should().BeTrue();

        model.Overrides.Should().Equal(new EventReminder(EventRemindersValidator.PopupMethod, 1860));
    }

    [Fact]
    public void DaysBefore_WhenTheFormOpens_StartsAtOneDayBefore()
    {
        AllDay(EventReminders.ExplicitlyNone).DaysBefore
            .Should().Be(ReminderPickerModel.MinimumDaysBefore);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void DaysBefore_SetBelowOneDay_ClampsToOneDay(int attempted)
    {
        // "0 days before at 09:00" would be minutes:-540, which Google does not reject — it clamps
        // it to 0 and wakes the family at midnight. The control simply cannot hold the value.
        var model = AllDay(EventReminders.ExplicitlyNone);

        model.DaysBefore = attempted;

        model.DaysBefore.Should().Be(ReminderPickerModel.MinimumDaysBefore);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(int.MaxValue)]
    public void DaysBefore_SetBeyondWhatGoogleStores_ClampsToTheCeiling(int attempted)
    {
        var model = AllDay(EventReminders.ExplicitlyNone);

        model.DaysBefore = attempted;

        model.DaysBefore.Should().Be(ReminderPickerModel.MaximumDaysBefore);
    }

    [Fact]
    public void ToMinutesBeforeMidnight_OverEveryReachableDayAndTime_IsAlwaysAPositiveOffset()
    {
        // The proof that "on the day" is unreachable by construction rather than merely validated.
        // The reachable input space is exactly this: DaysBefore is clamped to 1..MaximumDaysBefore,
        // and a TimeOnly cannot hold a whole day, so every combination is covered here.
        var offsets = (
            from days in Enumerable.Range(
                ReminderPickerModel.MinimumDaysBefore,
                ReminderPickerModel.MaximumDaysBefore - ReminderPickerModel.MinimumDaysBefore + 1)
            from minuteOfDay in Enumerable.Range(0, ReminderPickerModel.MinutesPerDay)
            select ReminderPickerModel.ToMinutesBeforeMidnight(
                days, new TimeOnly(minuteOfDay / 60, minuteOfDay % 60))).ToList();

        offsets.Min().Should().BeGreaterThan(0);
        offsets.Max().Should().BeLessThanOrEqualTo(EventRemindersValidator.MaxMinutes);
    }

    [Theory]
    [InlineData(900, 1, 9, 0)]
    [InlineData(1860, 2, 17, 0)]
    [InlineData(1440, 1, 0, 0)]
    [InlineData(30, 1, 23, 30)]
    public void ToDaysBeforeAndTime_ForAStoredOffset_ShowsTheDayAndTimeGoogleMeant(
        int minutes, int expectedDays, int expectedHour, int expectedMinute)
    {
        // 30 minutes on an all-day event is a ping at 23:30 the night before, not "half an hour
        // before" anything the family would recognise — which is exactly why the form never shows
        // bare minutes for an all-day event.
        var (days, timeOfDay) = ReminderPickerModel.ToDaysBeforeAndTime(minutes);

        days.Should().Be(expectedDays);
        timeOfDay.Should().Be(new TimeOnly(expectedHour, expectedMinute));
    }

    [Fact]
    public void ToDaysBeforeAndTime_RoundTripsWhatTheFormProduced()
    {
        var minutes = ReminderPickerModel.ToMinutesBeforeMidnight(3, new TimeOnly(7, 45));

        ReminderPickerModel.ToDaysBeforeAndTime(minutes).Should().Be((3, new TimeOnly(7, 45)));
    }

    // --- The timed form: amount and unit -------------------------------------------------------

    [Theory]
    [InlineData(10, ReminderUnit.Minutes, 10)]
    [InlineData(2, ReminderUnit.Hours, 120)]
    [InlineData(3, ReminderUnit.Days, 4320)]
    [InlineData(1, ReminderUnit.Weeks, 10080)]
    [InlineData(0, ReminderUnit.Minutes, 0)]
    public void ToMinutes_ForEachUnit_MatchesGooglesMinutesBeforeTheStart(
        int amount, ReminderUnit unit, int expected)
    {
        // 0 is a legitimate value for a timed event: a reminder at the moment it starts.
        ReminderPickerModel.ToMinutes(amount, unit).Should().Be(expected);
    }

    [Fact]
    public void TryAddSelectedReminder_OnATimedEvent_UsesTheAmountUnitAndMethod()
    {
        var model = Timed(EventReminders.ExplicitlyNone);
        model.Amount = 2;
        model.Unit = ReminderUnit.Hours;
        model.Method = EventRemindersValidator.EmailMethod;

        model.TryAddSelectedReminder().Should().BeTrue();

        model.Overrides.Should().Equal(new EventReminder(EventRemindersValidator.EmailMethod, 120));
    }

    [Theory]
    [InlineData(ReminderUnit.Minutes, EventRemindersValidator.MaxMinutes)]
    [InlineData(ReminderUnit.Hours, 672)]
    [InlineData(ReminderUnit.Days, 28)]
    [InlineData(ReminderUnit.Weeks, 4)]
    public void Amount_SetBeyondWhatTheUnitAllows_ClampsToTheCeiling(ReminderUnit unit, int expected)
    {
        var model = Timed(EventReminders.ExplicitlyNone);
        model.Unit = unit;

        model.Amount = int.MaxValue;

        model.Amount.Should().Be(expected);
    }

    [Fact]
    public void Unit_ChangedToACoarserOne_ReClampsTheAmountSoItStaysStorable()
    {
        // 40320 minutes is the ceiling exactly; 40320 weeks is nowhere near it. Re-clamping on the
        // unit change is what keeps the pair reachable-only-if-valid.
        var model = Timed(EventReminders.ExplicitlyNone);
        model.Amount = EventRemindersValidator.MaxMinutes;

        model.Unit = ReminderUnit.Weeks;

        model.Amount.Should().Be(4);
    }

    [Fact]
    public void Amount_SetBelowZero_ClampsToZero()
    {
        var model = Timed(EventReminders.ExplicitlyNone);

        model.Amount = -5;

        model.Amount.Should().Be(0);
    }

    // --- Back out to Google's shape ------------------------------------------------------------

    [Fact]
    public void ToEventReminders_WhenFollowingTheCalendarDefault_RoundTripsTheInheritingShape()
    {
        var result = Timed(EventReminders.InheritsCalendarDefault).ToEventReminders();

        result.UseDefault.Should().BeTrue();
        result.Overrides.Should().BeEmpty();
        result.SameAs(EventReminders.InheritsCalendarDefault).Should().BeTrue();
    }

    [Fact]
    public void ToEventReminders_WhenTheEventCarriesItsOwn_RoundTripsThemAsASet()
    {
        var opened = EventReminders.Explicit(
        [
            new EventReminder(EventRemindersValidator.PopupMethod, 30),
            new EventReminder(EventRemindersValidator.EmailMethod, 1440)
        ]);

        var result = Timed(opened).ToEventReminders();

        result.UseDefault.Should().BeFalse();
        result.SameAs(opened).Should().BeTrue();
    }

    [Fact]
    public void ToEventReminders_WhenRemindersAreTurnedOff_RoundTripsTheNoneShape()
    {
        var result = Timed(EventReminders.ExplicitlyNone).ToEventReminders();

        result.UseDefault.Should().BeFalse();
        result.Overrides.Should().BeEmpty();
        result.SameAs(EventReminders.ExplicitlyNone).Should().BeTrue();
    }

    [Fact]
    public void ToEventReminders_IsNotAffectedByLaterEditsToTheModel()
    {
        // The request body is built once and handed to the API client; a later keystroke in the tab
        // must not reach back into it.
        var model = Timed(EventReminders.ExplicitlyNone);
        model.TryAdd(new EventReminder(EventRemindersValidator.PopupMethod, 30)).Should().BeTrue();

        var result = model.ToEventReminders();
        model.TryAdd(new EventReminder(EventRemindersValidator.PopupMethod, 60)).Should().BeTrue();

        result.Overrides.Should().Equal(new EventReminder(EventRemindersValidator.PopupMethod, 30));
    }

    // --- What the tab needs to show about the calendar's own defaults ---------------------------

    [Fact]
    public void CalendarDefault_IsExposedSoTheTabCanSayWhatInheritingActuallyMeans()
    {
        var model = Timed(EventReminders.InheritsCalendarDefault);

        model.CalendarDefault.Should().Equal(
            new EventReminder(EventRemindersValidator.PopupMethod, 30),
            new EventReminder(EventRemindersValidator.EmailMethod, 1440));
    }

    [Fact]
    public void IsAllDay_IsFixedAtConstructionSoTogglingAllDayRebuildsTheTab()
    {
        // Google discards an event's reminders when it becomes all-day and substitutes the all-day
        // shape — no conversion. Carrying the flag read-only is what forces the caller to rebuild.
        AllDay(EventReminders.ExplicitlyNone).IsAllDay.Should().BeTrue();
        Timed(EventReminders.ExplicitlyNone).IsAllDay.Should().BeFalse();
    }
}
