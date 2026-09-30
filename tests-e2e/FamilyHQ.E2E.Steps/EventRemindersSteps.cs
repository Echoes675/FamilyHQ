using FamilyHQ.E2E.Common.Helpers;
using FamilyHQ.E2E.Common.Pages;
using FamilyHQ.E2E.Steps.Hooks;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

/// <summary>
/// Drives the event modal's Reminders tab, and reads back what the kiosk actually SENT.
/// </summary>
/// <remarks>
/// Two habits here are deliberate. Reminder rows are addressed by their method and offset rather than
/// by position, because Google returns the overrides in an order of its own. And the scenarios about
/// what a save left alone assert the request body, not the screen: a save that sends back the
/// reminders it opened with and a save that says nothing about them look identical afterwards, yet
/// only the second leaves a set made in the Google Calendar app untouched.
/// </remarks>
[Binding]
public class EventRemindersSteps
{
    /// <summary>The three reminder states an event can be in, as the feature files name them.</summary>
    private const string FollowsCalendarState = "the calendar's usual reminders";
    private const string OwnState = "reminders of its own";
    private const string NoneState = "no reminders";

    // The reminder the picker's form offers to add, and the one every "of its own" state uses.
    private const int OwnReminderAmount = 2;
    private const string OwnReminderUnit = "hours";

    private readonly ScenarioContext _scenarioContext;
    private readonly DashboardPage _dashboardPage;

    public EventRemindersSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
        _dashboardPage = new DashboardPage(scenarioContext.Get<IPage>());
    }

    private EventWriteRecorder Recorder =>
        _scenarioContext.Get<EventWriteRecorder>(EventWriteHooks.RecorderKey);

    // ── Creating events in a known reminder state ────────────────────────────

    // Switching inheritance off copies the calendar's usual reminders in as editable entries, as the
    // Google Calendar app pre-fills them — so the event ends up with those AND the one added here.
    [Given(@"the event ""([^""]*)"" in ""([^""]*)"" has a reminder (\d+) (minutes|hours) before")]
    [When(@"I create the event ""([^""]*)"" in ""([^""]*)"" with a reminder (\d+) (minutes|hours) before")]
    public async Task CreateEventWithItsOwnReminder(
        string title, string calendarName, int amount, string unit)
    {
        await _dashboardPage.BeginCreatingEventTitledAsync(title, calendarName);
        await _dashboardPage.SetReminderInheritanceAsync(follow: false);
        await _dashboardPage.AddTimedReminderAsync(amount, unit);
        await _dashboardPage.SaveOpenEventAsync();
    }

    // The three states are spelled out rather than captured with a wildcard: a wildcard would also
    // match "with a reminder 2 hours before" above, and two patterns matching one step is an
    // ambiguous-step failure at runtime.
    [When(@"I create the event ""([^""]*)"" in ""([^""]*)"" with (the calendar's usual reminders|reminders of its own|no reminders)")]
    public async Task WhenICreateTheEventWithState(string title, string calendarName, string state)
    {
        await _dashboardPage.BeginCreatingEventTitledAsync(title, calendarName);
        await ApplyReminderStateAsync(state);
        await _dashboardPage.SaveOpenEventAsync();
    }

    // A plain create: the Reminders tab is never opened, so the create says nothing about reminders
    // and Google applies the calendar's usual ones itself.
    [Given(@"the event ""([^""]*)"" in ""([^""]*)"" follows the calendar's usual reminders")]
    public async Task GivenTheEventFollowsTheCalendarsUsualReminders(string title, string calendarName)
    {
        await _dashboardPage.CreateEventInCalendarAsync(title, calendarName);
    }

    [Given(@"the all-day event ""([^""]*)"" exists in ""([^""]*)""")]
    public async Task GivenTheAllDayEventExistsIn(string title, string calendarName)
    {
        await _dashboardPage.CreateAllDayEventInCalendarAsync(title, calendarName);
    }

    [When(@"I change the event ""([^""]*)"" to (the calendar's usual reminders|reminders of its own|no reminders)")]
    public async Task WhenIChangeTheEventTo(string title, string state)
    {
        await _dashboardPage.OpenEventForEditingAsync(title);
        await ApplyReminderStateAsync(state);
        await _dashboardPage.SaveOpenEventAsync();
    }

    // ── Working the tab in an already-open modal ─────────────────────────────

    [When(@"I give the event reminders of its own")]
    public async Task WhenIGiveTheEventRemindersOfItsOwn()
    {
        await _dashboardPage.SetReminderInheritanceAsync(follow: false);
    }

    // Inheritance has to go off first: the Add form is not offered while the event follows the
    // calendar, because Google refuses a write asking for the defaults and for specific reminders
    // at once.
    [When(@"I give the event a reminder (\d+) (minutes|hours) before")]
    public async Task WhenIGiveTheEventAReminderBefore(int amount, string unit)
    {
        await _dashboardPage.SetReminderInheritanceAsync(follow: false);
        await _dashboardPage.AddTimedReminderAsync(amount, unit);
    }

    [When(@"I give the event a reminder (\d+) (minutes|hours) before and take it away again")]
    public async Task WhenIGiveTheEventAReminderAndTakeItAwayAgain(int amount, string unit)
    {
        await WhenIGiveTheEventAReminderBefore(amount, unit);
        await _dashboardPage.RemoveReminderAsync(MinutesFor(amount, unit));
    }

    [When(@"I switch the event to all day")]
    public async Task WhenISwitchTheEventToAllDay()
    {
        await _dashboardPage.ToggleAllDayAsync();
    }

    [When(@"I save the event")]
    public async Task WhenISaveTheEvent()
    {
        await _dashboardPage.SaveOpenEventAsync();
    }

    [When(@"I ask for a reminder (\d+) days before")]
    public async Task WhenIAskForAReminderDaysBefore(int days)
    {
        await _dashboardPage.AskForReminderDaysBeforeAsync(days);
    }

    // ── What the tab shows ───────────────────────────────────────────────────

    [Then(@"the event has reminders (\d+) minutes and (\d+) hours before")]
    public async Task ThenTheEventHasRemindersMinutesAndHoursBefore(int minutes, int hours)
    {
        await _dashboardPage.ShowRemindersTabAsync();
        await _dashboardPage.AssertReminderCountAsync(2);
        await _dashboardPage.AssertReminderPresentAsync(minutes);
        await _dashboardPage.AssertReminderPresentAsync(MinutesFor(hours, "hours"));
    }

    [Then(@"the event ""([^""]*)"" still has a reminder (\d+) (minutes|hours) before")]
    public async Task ThenTheEventStillHasAReminderBefore(string title, int amount, string unit)
    {
        await _dashboardPage.OpenEventForEditingAsync(title);
        await _dashboardPage.ShowRemindersTabAsync();
        await _dashboardPage.AssertReminderPresentAsync(MinutesFor(amount, unit));
    }

    [Then(@"the Reminders tab is labelled ""([^""]*)""")]
    public async Task ThenTheRemindersTabIsLabelled(string expected)
    {
        await _dashboardPage.AssertRemindersBadgeAsync(expected);
    }

    [Then(@"the Reminders tab says the reminders were started again")]
    public async Task ThenTheRemindersTabSaysTheRemindersWereStartedAgain()
    {
        await _dashboardPage.AssertRemindersResetNoticeVisibleAsync();
    }

    [Then(@"the reminder holds at (\d+) day before and will not go lower")]
    public async Task ThenTheReminderHoldsAtDayBefore(int days)
    {
        await _dashboardPage.AssertReminderDaysBeforeHoldsAtAsync(days);
    }

    [Then(@"I am warned that the change replaces the reminders on every occurrence")]
    public async Task ThenIAmWarnedThatTheChangeReplacesEveryOccurrencesReminders()
    {
        await _dashboardPage.AssertScopePromptWarnsAboutRemindersAsync();
    }

    // ── What the kiosk sent ──────────────────────────────────────────────────

    [Then(@"the event was saved without mentioning reminders")]
    public void ThenTheEventWasSavedWithoutMentioningReminders()
    {
        var write = Recorder.Last;

        write.Method.Should().BeOneOf(
            ["POST", "PUT"],
            "the scenario's last write to the events API should be the save under test.");
        write.Reminders.Should().BeNull(
            "a save that did not change the Reminders tab must say nothing about reminders, or it "
            + "would replace whatever Google holds — including a set made in the Google Calendar app.");
    }

    [Then(@"the event was saved asking for the calendar's usual reminders")]
    public void ThenTheEventWasSavedAskingForTheCalendarsUsualReminders()
    {
        var write = Recorder.Last;

        write.Method.Should().Be("PUT", "the scenario's last write should be the save under test.");
        write.Reminders.Should().NotBeNull(
            "switching All day discards the event's own reminders, which is a change and must be sent.");
        write.Reminders!.UseDefault.Should().BeTrue(
            "the save hands the choice back to Google rather than inventing a reminder here.");
        write.Reminders.Overrides.Should().BeEmpty(
            "Google refuses a write that asks for the calendar's usual reminders and for specific "
            + "ones at the same time.");
    }

    [Then(@"the event was deleted without mentioning reminders")]
    public void ThenTheEventWasDeletedWithoutMentioningReminders()
    {
        var write = Recorder.Last;

        write.Method.Should().Be("DELETE", "the scenario's last write should be the delete under test.");
        write.Reminders.Should().BeNull("a delete never writes reminders, whatever the tab was showing.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts the open modal's Reminders tab into one of Google's three states, so a scenario can move
    /// between any two of them.
    /// </summary>
    private async Task ApplyReminderStateAsync(string state)
    {
        switch (state)
        {
            case FollowsCalendarState:
                await _dashboardPage.SetReminderInheritanceAsync(follow: true);
                break;

            case OwnState:
                // Cleared first so the state means one known reminder whichever state it came from:
                // switching inheritance off copies the calendar's usual reminders in.
                await _dashboardPage.SetReminderInheritanceAsync(follow: false);
                await _dashboardPage.RemoveEveryReminderAsync();
                await _dashboardPage.AddTimedReminderAsync(OwnReminderAmount, OwnReminderUnit);
                break;

            case NoneState:
                await _dashboardPage.SetReminderInheritanceAsync(follow: false);
                await _dashboardPage.RemoveEveryReminderAsync();
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(state), state,
                    $"Not a reminder state. Use \"{FollowsCalendarState}\", \"{OwnState}\" or \"{NoneState}\".");
        }
    }

    private static int MinutesFor(int amount, string unit) => unit switch
    {
        "minutes" => amount,
        "hours" => amount * 60,
        _ => throw new ArgumentOutOfRangeException(
            nameof(unit), unit, "Not a unit the feature files use (minutes, hours).")
    };
}
