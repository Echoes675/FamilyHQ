using System.Globalization;
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

    /// <summary>
    /// The fields an edit that touched only the reminders must send back with the same text. Each is
    /// carried by both the create and the update contracts, and each is set by the create this
    /// scenario makes — a field left null by the create could not be observed to survive.
    /// </summary>
    private static readonly string[] UnchangedTextFields = ["title", "isAllDay", "description"];

    /// <summary>
    /// The boundaries, compared as instants rather than as text.
    /// <para>
    /// They cannot be compared as text: the data layer converts every <c>DateTimeOffset</c> to UTC on
    /// the way into PostgreSQL, so the value the modal reads back carries a zero offset where the
    /// create carried the browser's. The two strings differ while describing the same moment, and the
    /// moment is what the field means. A text comparison here would fail for a reason that has nothing
    /// to do with the edit — and the obvious "fix" would be to drop the assertion.
    /// </para>
    /// </summary>
    private static readonly string[] UnchangedInstantFields = ["start", "end"];

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

    // The same, with a note typed in as well. The note exists so the round-trip assertion below has a
    // nullable field with a real value in it: comparing two nulls proves nothing.
    [Given(@"the event ""([^""]*)"" in ""([^""]*)"" has a reminder (\d+) (minutes|hours) before and the note ""([^""]*)""")]
    public async Task GivenTheEventHasAReminderAndANote(
        string title, string calendarName, int amount, string unit, string note)
    {
        await _dashboardPage.BeginCreatingEventTitledAsync(title, calendarName);
        await _dashboardPage.FillOpenEventDescriptionAsync(note);
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

    /// <summary>
    /// The golden rule in the direction that gets missed: a change to the reminders alone must leave
    /// every other field of the request as it was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comparison is the create's own body against the update's, field by field, rather than
    /// against values written into the test. That is the round-trip principle applied to the
    /// assertion itself: a literal expectation would only prove the update agrees with the test, while
    /// this proves it agrees with what the event was actually created as.
    /// </para>
    /// <para>
    /// Each field is required to be present in the create before being compared, so the step cannot
    /// pass by finding nothing on either side. It takes the scenario's <b>first</b> create as the
    /// baseline, so a scenario that creates two events before editing one would compare against the
    /// wrong one — give that case its own step rather than reusing this. The unmapped fields Google holds and FamilyHQ does not
    /// model — an event's colour, for one — are deliberately absent: the Simulator never stores them,
    /// so their survival is not observable here and is asserted against real Google in the preprod
    /// suite instead.
    /// </para>
    /// </remarks>
    [Then(@"the event was saved carrying everything else exactly as it opened")]
    public void ThenTheEventWasSavedCarryingEverythingElseExactlyAsItOpened()
    {
        var writes = Recorder.Writes;

        var create = writes.FirstOrDefault(write => write.Method == "POST")
                     ?? throw new InvalidOperationException(
                         "This scenario compares the reminder edit against the create that preceded it, "
                         + "and no create was recorded. Without it there is nothing to compare, so the "
                         + "step stops here rather than passing vacuously.");

        var update = Recorder.Last;
        update.Method.Should().Be(
            "PUT",
            "the last write this scenario made should be the reminder edit, and an edit to an existing "
            + "event is a PUT");

        foreach (var name in UnchangedTextFields)
        {
            var before = Required(create, name);

            update.Field(name).Should().Be(
                before,
                $"'{name}' was not edited, so the save must send back what the event already had. A "
                + "reminder change that rewrites another field alongside it has broken the rule however "
                + "correct the reminder change was — and the family sees the damage in the Google "
                + "Calendar app rather than here");
        }

        foreach (var name in UnchangedInstantFields)
        {
            var before = InstantOf(Required(create, name), name);

            InstantOf(Required(update, name), name).Should().Be(
                before,
                $"'{name}' was not edited, so the save must describe the same moment the event already "
                + "had. A reminder change that moves the event is the worst outcome available here: the "
                + "family would be alerted correctly, for the wrong time");
        }
    }

    /// <summary>
    /// <paramref name="write"/>'s value for <paramref name="name"/>, or a failure. Required rather than
    /// optional so the comparison above cannot pass by finding nothing on either side.
    /// </summary>
    private static string Required(EventWrite write, string name) =>
        write.Field(name)
        ?? throw new InvalidOperationException(
            $"The {write.Method} this scenario recorded carried no '{name}', so its survival cannot be "
            + "asserted. A null on both sides would let the comparison pass while proving nothing, so "
            + "the step stops here instead.");

    /// <summary>The moment <paramref name="value"/> describes, whatever offset it was written with.</summary>
    private static DateTimeOffset InstantOf(string value, string name) =>
        DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"'{name}' was sent as \"{value}\", which is not a timestamp this step can read. The "
                + "boundaries are compared as instants, so an unparseable value is a change in the "
                + "request's shape rather than something to compare as text.");

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
