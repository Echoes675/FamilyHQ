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

    [Fact]
    public void TryAdd_AlongsideAValueTheKioskCouldNotHaveCreated_KeepsBoth()
    {
        // Showing a phone-set value is only half of it. The family then edits the reminders — adds one
        // of their own — and the value the kiosk has no control for has to come back out the other
        // side. Because a save sends the whole override list, dropping it here would delete a reminder
        // from a phone as a side effect of adding one on the kiosk: a silent loss the family finds out
        // about by not being told about something.
        var fromAPhone = EventReminders.Explicit(
        [
            new EventReminder("sms", 47)
        ]);

        var model = Timed(fromAPhone);

        model.TryAdd(new EventReminder("popup", 30)).Should().BeTrue();

        model.Overrides.Should().BeEquivalentTo(
            new[]
            {
                new EventReminder("sms", 47),
                new EventReminder("popup", 30)
            },
            "adding a reminder is not a licence to normalise the ones already there");
        model.HasChanged.Should().BeTrue("the family did ask for the new one");
    }

    [Fact]
    public void Remove_OfADifferentReminder_LeavesAValueTheKioskCouldNotHaveCreatedAlone()
    {
        // The mirror: taking away a reminder the kiosk understands must not take the phone-set one
        // with it.
        var fromAPhone = EventReminders.Explicit(
        [
            new EventReminder("sms", 47),
            new EventReminder("popup", 30)
        ]);

        var model = Timed(fromAPhone);

        model.Remove(new EventReminder("popup", 30)).Should().BeTrue();

        model.Overrides.Should().Equal(new EventReminder("sms", 47));
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
    public void StopUsingCalendarDefault_WhenTheCalendarsDefaultsAreUnknown_CopiesNothingIn()
    {
        // There is nothing to pre-fill: inventing a reminder here would be the kiosk guessing at what
        // Google holds. The tab says so instead, which is the whole point of keeping unknown apart
        // from "the calendar has none".
        var model = ReminderPickerModel.From(
            EventReminders.InheritsCalendarDefault, calendarDefault: null, isAllDay: false);

        model.StopUsingCalendarDefault();

        model.Overrides.Should().BeEmpty();
        model.State.Should().Be(ReminderPickerState.ExplicitlyNone);
    }

    [Fact]
    public void StopUsingCalendarDefault_WhenTheCalendarReportedHavingNoDefaults_CopiesNothingIn()
    {
        // The same list, reached from the other state. Here it is the honest answer rather than an
        // absence of one: the calendar really has nothing to pre-fill, so switching inheritance off
        // changes nothing a phone would do differently.
        var model = ReminderPickerModel.From(
            EventReminders.InheritsCalendarDefault, EventReminders.Explicit([]), isAllDay: false);

        model.StopUsingCalendarDefault();

        model.Overrides.Should().BeEmpty();
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

    // The three states the tab has to tell apart. Two of them are both "nothing to list", and
    // collapsing them is the defect these tests exist for: shown "no reminders" for an event that is
    // in fact inheriting real ones, a family that saves sends useDefault:false with an empty
    // overrides array, and Google — which replaces the whole reminders object — is left holding
    // nothing. Only the state decides which sentence the tab shows; Count alone cannot.

    [Fact]
    public void From_WhenNothingHasReportedTheCalendarsDefaults_LeavesThemUnknownRatherThanEmpty()
    {
        var model = ReminderPickerModel.From(
            EventReminders.InheritsCalendarDefault, calendarDefault: null, isAllDay: false);

        model.CalendarDefault.Should().BeNull(
            "null is 'nobody has told us', which is not an answer and must not read as one");
    }

    [Fact]
    public void From_WhenTheCalendarReportedHavingNoDefaults_IsAnEmptyListRatherThanUnknown()
    {
        // What GoogleCalendarClient stores for a calendar whose `defaultReminders` array Google sent
        // empty: a real answer, and the one case where the tab may say following the calendar will
        // notify nobody.
        var model = ReminderPickerModel.From(
            EventReminders.InheritsCalendarDefault, EventReminders.Explicit([]), isAllDay: false);

        model.CalendarDefault.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void From_WhenTheCalendarHasMoreDefaultsThanGoogleAcceptsOnAnEvent_OrdersThemAndCapsTheList()
    {
        // A calendar may carry more defaults than an event is allowed overrides, and switching
        // inheritance off copies this list in — so an uncapped list would build a set Google rejects
        // outright (400 eventRemindersCountExceedsLimit). Soonest first, then by method, for the same
        // reason the event's own list is ordered: Google returns the array in an order of its own.
        var model = ReminderPickerModel.From(
            EventReminders.InheritsCalendarDefault,
            EventReminders.Explicit(
            [
                new EventReminder(EventRemindersValidator.PopupMethod, 60),
                new EventReminder(EventRemindersValidator.PopupMethod, 10),
                new EventReminder(EventRemindersValidator.EmailMethod, 10),
                new EventReminder(EventRemindersValidator.PopupMethod, 1440),
                new EventReminder(EventRemindersValidator.PopupMethod, 30),
                new EventReminder(EventRemindersValidator.PopupMethod, 20)
            ]),
            isAllDay: false);

        model.CalendarDefault.Should().Equal(
            new EventReminder(EventRemindersValidator.EmailMethod, 10),
            new EventReminder(EventRemindersValidator.PopupMethod, 10),
            new EventReminder(EventRemindersValidator.PopupMethod, 20),
            new EventReminder(EventRemindersValidator.PopupMethod, 30),
            new EventReminder(EventRemindersValidator.PopupMethod, 60));
        model.CalendarDefault.Should().HaveCount(EventRemindersValidator.MaxOverrides);
    }

    [Fact]
    public void IsAllDay_IsFixedAtConstructionSoTogglingAllDayRebuildsTheTab()
    {
        // Google discards an event's reminders when it becomes all-day and substitutes the all-day
        // shape — no conversion. Carrying the flag read-only is what forces the caller to rebuild.
        AllDay(EventReminders.ExplicitlyNone).IsAllDay.Should().BeTrue();
        Timed(EventReminders.ExplicitlyNone).IsAllDay.Should().BeFalse();
    }

    // --- The pending form value, committed on save ----------------------------------------------
    //
    // A family member who switches inheritance off, configures a reminder and saves without pressing
    // Add believes they set one, and production shows them finding out they did not. The save commits
    // the form's pending value — but ONLY when the form was touched, because it holds
    // "30 · Minutes · Notification" from the moment it appears and committing that untouched would
    // invent a reminder for somebody who switched inheritance off wanting silence.

    // A calendar that reported having no defaults, so switching inheritance off lands on the empty
    // list the production defect was reported against rather than pre-filling anything.
    private static ReminderPickerModel OnEmptyList(bool isAllDay = false)
    {
        var model = ReminderPickerModel.From(
            EventReminders.InheritsCalendarDefault, EventReminders.ExplicitlyNone, isAllDay);
        model.StopUsingCalendarDefault();
        model.Overrides.Should().BeEmpty();
        return model;
    }

    [Fact]
    public void CommitPendingFormReminder_WhenTheFormWasConfiguredAndAddWasNeverPressed_CommitsIt()
    {
        var model = OnEmptyList();

        model.Amount = 45;

        model.CommitPendingFormReminder().Should().BeTrue();
        model.State.Should().Be(ReminderPickerState.Explicit);
        model.Overrides.Should().Equal(new EventReminder(EventRemindersValidator.PopupMethod, 45));
        model.HasChanged.Should().BeTrue("the write has to carry what was committed");
        model.ToEventReminders()
            .SameAs(EventReminders.Explicit([new EventReminder(EventRemindersValidator.PopupMethod, 45)]))
            .Should().BeTrue();
    }

    [Fact]
    public void CommitPendingFormReminder_WhenInheritanceWasSwitchedOffAndNothingTouched_CommitsNothing()
    {
        // The whole safety property. Explicitly-none is a state the family can ask for, and this is
        // how they ask for it.
        var model = OnEmptyList();

        model.CommitPendingFormReminder().Should().BeFalse();

        model.State.Should().Be(ReminderPickerState.ExplicitlyNone);
        model.Overrides.Should().BeEmpty();
        model.ToEventReminders().SameAs(EventReminders.ExplicitlyNone).Should().BeTrue();
    }

    [Fact]
    public void CommitPendingFormReminder_OnAnUntouchedTabThatIsStillInheriting_SendsNothingAtAll()
    {
        // The golden rule's default: an ordinary title edit must say nothing about reminders, so a
        // save on a tab nobody opened cannot commit anything.
        var model = Timed(EventReminders.InheritsCalendarDefault);

        model.CommitPendingFormReminder().Should().BeFalse();

        model.HasChanged.Should().BeFalse();
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("unit")]
    [InlineData("method")]
    public void CommitPendingFormReminder_AfterAnyOneOfTheFormsControlsMoves_CommitsThePendingValue(string control)
    {
        var model = OnEmptyList();

        switch (control)
        {
            case "amount": model.Amount = 15; break;
            case "unit": model.Unit = ReminderUnit.Hours; break;
            default: model.Method = EventRemindersValidator.EmailMethod; break;
        }

        model.IsFormTouched.Should().BeTrue();
        model.CommitPendingFormReminder().Should().BeTrue();
        model.Overrides.Should().Equal(control switch
        {
            "amount" => new EventReminder(EventRemindersValidator.PopupMethod, 15),
            "unit" => new EventReminder(EventRemindersValidator.PopupMethod, 30 * 60),
            _ => new EventReminder(EventRemindersValidator.EmailMethod, 30)
        });
    }

    [Theory]
    [InlineData("days")]
    [InlineData("time")]
    public void CommitPendingFormReminder_OnAnAllDayEventAfterItsOwnControlsMove_CommitsThePendingValue(string control)
    {
        // The all-day form carries the same hazard for the same reason: it reads "1 day before at
        // 09:00 · Notification" before anybody touches it, and its two controls are the ones that
        // move the pending value there.
        var model = OnEmptyList(isAllDay: true);

        if (control == "days")
        {
            model.DaysBefore = 2;
        }
        else
        {
            model.TimeOfDay = new TimeOnly(18, 0);
        }

        model.CommitPendingFormReminder().Should().BeTrue();
        model.Overrides.Should().Equal(new EventReminder(
            EventRemindersValidator.PopupMethod,
            control == "days"
                ? ReminderPickerModel.ToMinutesBeforeMidnight(2, new TimeOnly(9, 0))
                : ReminderPickerModel.ToMinutesBeforeMidnight(1, new TimeOnly(18, 0))));
    }

    [Fact]
    public void CommitPendingFormReminder_WhenAControlIsMovedAndMovedBack_StillCommits()
    {
        // The documented choice: touched is sticky. Somebody who dials 45 and settles on 30 has
        // engaged with the form and is configuring a reminder, not asking for silence — reading them
        // as untouched again would discard the reminder the screen is offering, which is the defect.
        var model = OnEmptyList();

        model.Amount = 45;
        model.Amount = 30;

        model.IsFormTouched.Should().BeTrue();
        model.CommitPendingFormReminder().Should().BeTrue();
        model.Overrides.Should().Equal(new EventReminder(EventRemindersValidator.PopupMethod, 30));
    }

    [Fact]
    public void CommitPendingFormReminder_WhenAControlIsAssignedTheValueItAlreadyHolds_CommitsNothing()
    {
        // A change event can fire on a blur that altered nothing, and a no-op is not a choice.
        var model = OnEmptyList();

        model.Amount = model.Amount;
        model.Unit = model.Unit;
        model.Method = model.Method;

        model.IsFormTouched.Should().BeFalse();
        model.CommitPendingFormReminder().Should().BeFalse();
    }

    [Fact]
    public void CommitPendingFormReminder_WhenTheClampRejectedTheTypedValue_CommitsNothing()
    {
        // Typing a day-of reminder into the all-day form is refused by the floor, so the form never
        // moved. A value that could not reach the form is not a value the family chose.
        var model = OnEmptyList(isAllDay: true);

        model.DaysBefore = 0;

        model.DaysBefore.Should().Be(ReminderPickerModel.MinimumDaysBefore);
        model.IsFormTouched.Should().BeFalse();
        model.CommitPendingFormReminder().Should().BeFalse();
    }

    [Fact]
    public void CommitPendingFormReminder_WhenTheFormHoldsAReminderTheFamilyTookAway_DoesNotHandItBack()
    {
        // Adding a reminder and then removing it is the clearest the tab gets about not wanting that
        // reminder, and the form is left showing it — so committing would undo exactly that.
        var model = OnEmptyList();
        model.Amount = 45;
        model.TryAddSelectedReminder().Should().BeTrue();
        model.Remove(new EventReminder(EventRemindersValidator.PopupMethod, 45)).Should().BeTrue();

        model.CommitPendingFormReminder().Should().BeFalse();

        model.State.Should().Be(ReminderPickerState.ExplicitlyNone);
        model.Overrides.Should().BeEmpty();
    }

    [Fact]
    public void CommitPendingFormReminder_WhenADifferentReminderWasTakenAwayAndThisOneConfigured_CommitsIt()
    {
        // Taking one reminder away and configuring another is a replacement, not a request for
        // silence — and leaving the event silent is the defect this commit exists to prevent.
        var model = Timed(EventReminders.Explicit([new EventReminder(EventRemindersValidator.PopupMethod, 30)]));
        model.Remove(new EventReminder(EventRemindersValidator.PopupMethod, 30)).Should().BeTrue();
        model.Amount = 45;

        model.CommitPendingFormReminder().Should().BeTrue();

        model.Overrides.Should().Equal(new EventReminder(EventRemindersValidator.PopupMethod, 45));
    }

    [Fact]
    public void CommitPendingFormReminder_WhenTheOnlyReminderWasTakenAwayAndNothingConfigured_LeavesTheEventSilent()
    {
        // Remove on its own, with the form never touched, is how the family ask for silence.
        var model = Timed(EventReminders.Explicit([new EventReminder(EventRemindersValidator.PopupMethod, 30)]));

        model.Remove(new EventReminder(EventRemindersValidator.PopupMethod, 30)).Should().BeTrue();

        model.CommitPendingFormReminder().Should().BeFalse();
        model.State.Should().Be(ReminderPickerState.ExplicitlyNone);
        model.ToEventReminders().SameAs(EventReminders.ExplicitlyNone).Should().BeTrue();
    }

    [Fact]
    public void CommitPendingFormReminder_WhenAReminderWasAlreadyAdded_DoesNotAddTheFormsPendingOneToo()
    {
        // The list in front of the family IS their answer once they have used Add; a second reminder
        // they can see they did not add is an incidental change.
        var model = OnEmptyList();
        model.Amount = 45;
        model.TryAddSelectedReminder().Should().BeTrue();
        model.Amount = 90;

        model.CommitPendingFormReminder().Should().BeFalse();

        model.Overrides.Should().Equal(new EventReminder(EventRemindersValidator.PopupMethod, 45));
    }

    [Fact]
    public void CommitPendingFormReminder_WhenTheCalendarsDefaultsWereCopiedIn_CommitsNothingExtra()
    {
        // Switching inheritance off pre-fills the calendar's defaults, so the list is not empty and
        // the family can see exactly what the event will have.
        var model = Timed(EventReminders.InheritsCalendarDefault);
        model.StopUsingCalendarDefault();
        model.Amount = 45;

        model.CommitPendingFormReminder().Should().BeFalse();

        model.Overrides.Should().Equal(
            new EventReminder(EventRemindersValidator.PopupMethod, 30),
            new EventReminder(EventRemindersValidator.EmailMethod, 1440));
    }

    [Fact]
    public void CommitPendingFormReminder_WhileTheEventStillInheritsButTheFormWasTouched_CommitsNothing()
    {
        // Switching inheritance back on is a choice to inherit. Google answers a body carrying both
        // an inherited and an own reminder with 400 cannotUseDefaultRemindersAndSpecifyOverride, so
        // committing here would not merely be wrong, it would be rejected.
        var model = OnEmptyList();
        model.Amount = 45;
        model.UseCalendarDefault();

        model.CommitPendingFormReminder().Should().BeFalse();

        model.State.Should().Be(ReminderPickerState.FollowsCalendarDefault);
        model.HasChanged.Should().BeFalse();
    }

    [Fact]
    public void CommitPendingFormReminder_CalledTwice_CommitsOnlyOnce()
    {
        // Idempotent by construction: the first commit fills the list, and an empty list is one of
        // the three conditions. Pinned because a save that is retried must not stack duplicates.
        var model = OnEmptyList();
        model.Amount = 45;

        model.CommitPendingFormReminder().Should().BeTrue();
        model.CommitPendingFormReminder().Should().BeFalse();

        model.Overrides.Should().HaveCount(1);
    }
}
