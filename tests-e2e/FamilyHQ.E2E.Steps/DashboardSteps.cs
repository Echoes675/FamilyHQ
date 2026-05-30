using System.Threading.Tasks;
using FamilyHQ.E2E.Common.Pages;
using FamilyHQ.E2E.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

using FamilyHQ.E2E.Common.Helpers;

[Binding]
public class DashboardSteps
{
    private readonly DashboardPage _dashboardPage;
    private readonly ScenarioContext _scenarioContext;

    public DashboardSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
        var page = scenarioContext.Get<IPage>();
        _dashboardPage = new DashboardPage(page);
    }

    private string GetCalendarColour(string calendarName)
    {
        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendar = template.Calendars.Find(c => c.Summary == calendarName);
        if (calendar == null)
            throw new Exception($"Calendar '{calendarName}' not found in the current user template.");
        return calendar.BackgroundColor ?? "#9e9e9e";
    }

    [Given(@"I view the dashboard")]
    [When(@"I view the dashboard")]
    public async Task WhenIViewTheDashboard()
    {
        await _dashboardPage.NavigateAndWaitAsync();
    }

    [Then(@"I see the event ""([^""]*)"" displayed on the calendar")]
    public async Task ThenISeeTheEventDisplayedOnTheCalendar(string eventName)
    {
        // Use Playwright's built-in retry to handle the render cycle between the HTTP
        // response landing and Blazor painting the event capsules into the DOM.
        var capsule = _dashboardPage.EventCapsules.Filter(new() { HasText = eventName });
        await Assertions.Expect(capsule.First).ToBeVisibleAsync(new() { Timeout = 30000 });
    }

    [Then(@"I do not see the event ""([^""]*)"" displayed on the calendar")]
    public async Task ThenIDoNotSeeTheEventDisplayedOnTheCalendar(string eventName)
    {
        var visibleEvents = await _dashboardPage.GetVisibleEventsAsync();
        
        visibleEvents.Should().NotContain(e => e.Contains(eventName), 
            $"The event '{eventName}' should NOT be visible on the dashboard month view.");
    }

    [When(@"I create an event ""([^""]*)""")]
    public async Task WhenICreateAnEvent(string eventName)
    {
        await _dashboardPage.CreateEventAsync(eventName);
    }

    [When(@"I fill in and save the event ""([^""]*)""")]
    public async Task WhenIFillInAndSaveTheEvent(string eventName)
    {
        await _dashboardPage.FillAndSaveEventAsync(eventName);
    }

    [When(@"I rename the event ""([^""]*)"" to ""([^""]*)""")]
    public async Task WhenIRenameTheEventTo(string oldEventName, string newEventName)
    {
        await _dashboardPage.UpdateEventAsync(oldEventName, newEventName);
    }

    [When(@"I delete the event ""([^""]*)""")]
    public async Task WhenIDeleteTheEvent(string eventName)
    {
        await _dashboardPage.DeleteEventAsync(eventName);
    }

    [Given(@"I login as the user")]
    public void GivenILoginAsTheUser()
    {
        
    }

    [When(@"I click on the event ""([^""]*)""")]
    public async Task WhenIClickOnTheEvent(string eventName)
    {
        await _dashboardPage.ClickEventAsync(eventName);
    }

    [Then(@"I see the event details for ""([^""]*)""")]
    public async Task ThenISeeTheEventDetailsFor(string eventName)
    {
        var eventDetails = await _dashboardPage.GetEventDetailsAsync();
        eventDetails.Should().Contain(eventName, 
            $"The event details should contain '{eventName}'.");
    }

    [When(@"I navigate to the next month")]
    public async Task WhenINavigateToTheNextMonth()
    {
        await _dashboardPage.NavigateToNextMonthAsync();
    }

    [When(@"I navigate the month view to show a date in (\d+) days")]
    public async Task WhenINavigateTheMonthViewToShowDateInDays(int days)
    {
        await _dashboardPage.NavigateToShowDateIfNeededAsync(DateTime.Today.AddDays(days));
    }

    [Given(@"I change the event ""([^""]*)"" to calendar ""([^""]*)""")]
    public async Task GivenIChangeTheEventToCalendar(string eventName, string calendarName)
    {
        await _dashboardPage.ChangeEventCalendarAsync(eventName, calendarName);
    }

    [When(@"I create an event ""([^""]*)"" in calendars ""([^""]*)"" and ""([^""]*)""")]
    public async Task WhenICreateAnEventInCalendars(string eventName, string calendarName1, string calendarName2)
    {
        await _dashboardPage.CreateEventInCalendarsAsync(eventName, calendarName1, calendarName2);
    }

    [When(@"I create an event ""([^""]*)"" in calendar ""([^""]*)"" with description ""([^""]*)""")]
    public async Task WhenICreateAnEventInCalendarWithDescription(string eventName, string calendarName, string description)
    {
        await _dashboardPage.CreateEventWithDescriptionInCalendarAsync(eventName, description, calendarName);
    }

    [When(@"I create an event ""([^""]*)"" in calendar ""([^""]*)""")]
    public async Task WhenICreateAnEventInCalendar(string eventName, string calendarName)
    {
        await _dashboardPage.CreateEventInCalendarAsync(eventName, calendarName);
    }

    // --- FHQ-32: create modal must not silently default the calendar selection ---

    [When(@"I open the create-event modal")]
    public async Task WhenIOpenTheCreateEventModal()
    {
        await _dashboardPage.OpenCreateEventModalAsync();
    }

    [Then(@"the create-event modal does not offer the shared ""([^""]*)"" chip")]
    public async Task ThenTheCreateEventModalDoesNotOfferTheSharedChip(string calendarName)
    {
        // Web-first: the chip set can still be settling as the modal renders. ToHaveCountAsync
        // auto-retries against the live DOM rather than reading the count once (FHQ-41).
        await Assertions.Expect(_dashboardPage.ModalChipsFor(calendarName))
            .ToHaveCountAsync(0, new() { Timeout = 30000 });
    }

    [When(@"I attempt to save the event ""([^""]*)"" with no calendar selected")]
    public async Task WhenIAttemptToSaveTheEventWithNoCalendarSelected(string eventName)
    {
        await _dashboardPage.AttemptSaveWithoutCalendarAsync(eventName);
    }

    [Then(@"the create-event modal stays open with a calendar validation error")]
    public async Task ThenTheCreateEventModalStaysOpenWithACalendarValidationError()
    {
        var blocked = await _dashboardPage.ModalShowsCalendarValidationErrorAsync();
        blocked.Should().BeTrue(
            "saving with no calendar selected must be blocked, leaving the modal open with a calendar validation error.");
    }

    [When(@"I cancel the create-event modal")]
    public async Task WhenICancelTheCreateEventModal()
    {
        await _dashboardPage.CancelEventModalAsync();
    }

    [When(@"I open the event ""([^""]*)"" for editing")]
    public async Task WhenIOpenTheEventForEditing(string eventName)
    {
        await _dashboardPage.OpenEventForEditingAsync(eventName);
    }

    [When(@"I add the calendar ""([^""]*)"" chip to the event")]
    public async Task WhenIAddTheCalendarChipToTheEvent(string calendarName)
    {
        await _dashboardPage.AddCalendarChipToEventAsync(calendarName);
    }

    [When(@"I remove the calendar ""([^""]*)"" chip from the event")]
    public async Task WhenIRemoveTheCalendarChipFromTheEvent(string calendarName)
    {
        await _dashboardPage.RemoveCalendarChipFromEventAsync(calendarName);
    }

    [Then(@"I see the event ""([^""]*)"" displayed on the calendar in ""([^""]*)"" colour")]
    public async Task ThenISeeTheEventDisplayedInCalendarColour(string eventName, string calendarName)
    {
        var colour = GetCalendarColour(calendarName);
        // Poll: creating/editing an event triggers an app re-fetch, and the resulting echo webhook
        // is now drained asynchronously by CalendarSyncWorker (FHQ-37), which broadcasts
        // EventsUpdated a beat later and re-renders the grid. A single read can land before the
        // capsule paints or inside that re-render window (TOCTOU, cf. intermittent-issues #4/#6).
        // Use shared Polling.UntilAsync to tolerate transient false until the deadline (FHQ-41).
        await Polling.UntilAsync(
            () => _dashboardPage.IsEventDisplayedInCalendarColourAsync(eventName, colour),
            $"the event '{eventName}' should appear as a capsule coloured '{colour}'.",
            timeoutMs: 15000);
    }

    [Then(@"I do not see a ""([^""]*)"" capsule for ""([^""]*)"" on the calendar")]
    public async Task ThenIDoNotSeeACalendarCapsuleForEventOnTheCalendar(string calendarName, string eventName)
    {
        var colour = GetCalendarColour(calendarName);
        // Poll: removing a calendar chip triggers an app re-fetch whose echo webhook is drained
        // asynchronously by CalendarSyncWorker (FHQ-37), re-rendering the grid a beat later. A
        // single read can land before the coloured capsule has been removed (TOCTOU). Poll the
        // compound "no capsule in this colour" condition until the deadline (FHQ-41).
        await Polling.UntilAsync(
            () => _dashboardPage.NoEventCapsuleWithCalendarColourAsync(eventName, calendarName, colour),
            $"the event '{eventName}' should NOT appear as a capsule coloured '{colour}' ({calendarName}).",
            timeoutMs: 15000);
    }

    [Then(@"the last active calendar chip has no remove button")]
    public async Task ThenTheLastActiveCalendarChipHasNoRemoveButton()
    {
        // Poll the compound "exactly one active chip AND it has no remove button" invariant: the
        // chip set re-renders as chips are removed, so a single read can race the re-render (FHQ-41).
        await Polling.UntilAsync(
            () => _dashboardPage.LastActiveChipHasNoRemoveButtonAsync(),
            "the sole remaining active calendar chip must not expose a remove button.",
            timeoutMs: 10000);
    }

    [StepDefinition(@"I switch to the Day View tab")]
    public async Task WhenISwitchToTheDayViewTab()
    {
        await _dashboardPage.SwitchToDayViewAsync();
    }

    [StepDefinition(@"I switch to the Month View tab")]
    public async Task WhenISwitchToTheMonthViewTab()
    {
        await _dashboardPage.SwitchToMonthViewAsync();
    }

    [When(@"I switch to the Agenda View tab")]
    public async Task WhenISwitchToTheAgendaViewTab()
    {
        await _dashboardPage.SwitchToAgendaViewAsync();
    }

    [StepDefinition(@"I select the date ""([^""]*)"" using the day picker")]
    public async Task WhenISelectTheDateUsingTheDayPicker(string dateExpr)
    {
        var yyyyMmDd = DateExpressionResolver.Resolve(dateExpr);
        await _dashboardPage.OpenDayPickerAndGoAsync(yyyyMmDd);
    }

    [When(@"^I click the ""\+n more"" link for tomorrow$")]
    public async Task WhenIClickTheMoreEventsLinkForTomorrow()
    {
        var dateYyyyMmDd = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
        await _dashboardPage.ClickMoreEventsLinkAsync(dateYyyyMmDd);
    }

    [When(@"I click an empty grid slot for calendar ""([^""]*)"" at ""([^""]*)""")]
    public async Task WhenIClickAnEmptyGridSlotForCalendarAt(string calendarName, string timeString)
    {
        await _dashboardPage.ClickDayGridSlotAsync(calendarName, timeString);
    }

    [Then(@"I see the Day View Container")]
    public async Task ThenISeeTheDayViewContainer()
    {
        await Assertions.Expect(_dashboardPage.DayViewContainer).ToBeVisibleAsync();
    }

    [Then(@"I see the Month View Table")]
    public async Task ThenISeeTheMonthViewTable()
    {
        await Assertions.Expect(_dashboardPage.MonthTable).ToBeVisibleAsync();
    }

    [Then(@"there are (\d+) calendar columns in the day view")]
    public async Task ThenThereAreCalendarColumnsInTheDayView(int count)
    {
        // Web-first: the day-view header columns render as the view paints; ToHaveCountAsync
        // auto-retries against the live DOM rather than counting once (FHQ-41).
        await Assertions.Expect(_dashboardPage.CalendarHeaderColumns)
            .ToHaveCountAsync(count, new() { Timeout = 30000 });
    }

    [Then(@"I see the all-day event ""([^""]*)"" displayed at the top of the Day View")]
    public async Task ThenISeeTheAllDayEventDisplayedAtTheTopOfTheDayView(string eventName)
    {
        await _dashboardPage.WaitForAllDayEventVisibleAsync(eventName);
    }

    [Then(@"I see the timed event ""([^""]*)"" displayed in the Day View grid")]
    public async Task ThenISeeTheTimedEventDisplayedInTheDayViewGrid(string eventName)
    {
        await _dashboardPage.WaitForTimedEventVisibleAsync(eventName);
    }

    [Then(@"the event ""([^""]*)"" has a height representing (\d+) minutes")]
    public async Task ThenTheEventHasAHeightRepresentingMinutes(string eventName, int durationMinutes)
    {
        var height = await _dashboardPage.GetTimedEventHeightAsync(eventName);
        height.Should().BeApproximately(durationMinutes, 5.0, $"The event '{eventName}' should be roughly {durationMinutes}px tall based on its duration.");
    }

    [Then(@"the current time line is visible")]
    public async Task ThenTheCurrentTimeLineIsVisible()
    {
        var visible = await _dashboardPage.IsCurrentTimeLineVisibleAsync();
        visible.Should().BeTrue();
    }

    [Then(@"the day picker shows today's date")]
    public async Task ThenTheDayPickerShowsTodaysDate()
    {
        // Blazor WASM formats via CurrentCulture backed by the browser's ICU data, which
        // for en-GB produces "Wednesday, 8 April 2026".  Server-side .NET on Linux (the CI
        // runner) uses a different ICU build whose en-GB LongDatePattern omits the comma
        // ("Wednesday 8 April 2026").  Rather than reproduce the browser's exact format,
        // assert on the semantic content: weekday name, day number, month name and year.
        var today   = DateTime.Today;
        var culture = new System.Globalization.CultureInfo("en-GB");
        var weekday = today.ToString("dddd", culture);
        var month   = today.ToString("MMMM", culture);
        var day     = today.Day.ToString();
        var year    = today.Year.ToString();

        // Poll the compound "label contains weekday AND day AND month AND year" condition: the
        // picker button text re-renders after the day-picker navigation, so a single read can
        // race the re-render (FHQ-41). Re-read the live text each poll iteration.
        await Polling.UntilAsync(
            async () =>
            {
                var actual = (await _dashboardPage.GetDayPickerButtonTextAsync()).Trim();
                return actual.Contains(weekday) && actual.Contains(day)
                    && actual.Contains(month) && actual.Contains(year);
            },
            $"the day picker should show today's date (weekday '{weekday}', day '{day}', month '{month}', year '{year}').",
            timeoutMs: 10000);
    }

    [When(@"I enter day view reorder mode")]
    public async Task WhenIEnterDayViewReorderMode()
    {
        var page = _scenarioContext.Get<IPage>();
        await page.GetByTestId("day-reorder-btn").ClickAsync();
        await page.Locator("[data-testid^='day-reorder-right-']").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    [Then(@"the ""([^""]*)"" column has no left arrow in day view reorder mode")]
    public async Task ThenTheColumnHasNoLeftArrowInDayViewReorderMode(string calendarName)
    {
        var page = _scenarioContext.Get<IPage>();
        var header = page.Locator("[data-testid^='day-calendar-header-']")
                         .Filter(new() { HasText = calendarName });
        var testId = await header.First.GetAttributeAsync("data-testid")
                     ?? throw new InvalidOperationException($"No day header found for '{calendarName}'.");
        var guid = testId.Replace("day-calendar-header-", "");
        var leftArrow = page.GetByTestId($"day-reorder-left-{guid}");
        await Assertions.Expect(leftArrow).ToHaveCountAsync(0, new() { Timeout = 30000 });
    }

    [Then(@"the ""([^""]*)"" column has no right arrow in day view reorder mode")]
    public async Task ThenTheColumnHasNoRightArrowInDayViewReorderMode(string calendarName)
    {
        var page = _scenarioContext.Get<IPage>();
        var header = page.Locator("[data-testid^='day-calendar-header-']")
                         .Filter(new() { HasText = calendarName });
        var testId = await header.First.GetAttributeAsync("data-testid")
                     ?? throw new InvalidOperationException($"No day header found for '{calendarName}'.");
        var guid = testId.Replace("day-calendar-header-", "");
        var rightArrow = page.GetByTestId($"day-reorder-right-{guid}");
        await Assertions.Expect(rightArrow).ToHaveCountAsync(0, new() { Timeout = 30000 });
    }
}
