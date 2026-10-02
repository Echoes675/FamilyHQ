using FamilyHQ.E2E.Common.Helpers;
using FamilyHQ.E2E.Common.Pages;
using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

/// <summary>
/// Drives the Reminders VIEW — the fourth dashboard tab's standalone timeline of events that still
/// have a reminder due — as distinct from the event modal's own Reminders tab, which
/// <see cref="EventRemindersSteps"/> already covers.
/// </summary>
/// <remarks>
/// Every assertion here addresses a row by its value (event id + next-reminder instant + default
/// flag), never by position, for the same reason the modal's Reminders tab rows do: this view sorts
/// by EVENT START, which depends on seeding order rather than on anything Google guarantees, so a
/// row's position on screen is not a stable identifier a scenario can key off.
/// </remarks>
[Binding]
public class RemindersViewSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly DashboardPage _dashboardPage;
    private readonly SimulatorApiClient _simulatorApi;

    public RemindersViewSteps(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _dashboardPage = new DashboardPage(scenarioContext.Get<IPage>());
        _simulatorApi = simulatorApi;
    }

    // ── Seeding a reminder that is guaranteed not to have fired yet ─────────────────────────────
    // RemindersController still excludes an event once EVERY one of its reminders has already fired —
    // a real wall-clock comparison none of this suite's other seeding helpers have to consider,
    // because a Month/Day/Agenda assertion only cares which CALENDAR DAY an event falls on, never what
    // time of day "now" happens to be. A reminder seeded at a fixed clock time (e.g. "09:00 today") has
    // already fired, and the event it belongs to is correctly ABSENT from this view, for any run that
    // happens to execute after that time of day — which is most of the day. Seeding relative to "now"
    // instead keeps the reminder in the future by construction, wherever in the day the suite actually
    // runs. (The event's own start can land a few minutes into tomorrow if "now" is close enough to
    // midnight; that's harmless here, because the row is filed by the EVENT's own start date, which
    // moves with it.)
    [Given(@"the user has a timed event ""([^""]*)"" starting in (\d+) minutes in ""([^""]*)""")]
    public async Task GivenTheUserHasATimedEventStartingInMinutesInCalendar(
        string eventName, int minutes, string calendarName)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName)
                       ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found.");

        var start = BrowserClock.Now.AddMinutes(minutes);

        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendar.Id,
            Summary = eventName,
            StartTime = BrowserClock.ToUtcInstant(start),
            EndTime = BrowserClock.ToUtcInstant(start.AddHours(1)),
            IsAllDay = false
        });

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    // ── Entering / leaving ───────────────────────────────────────────────────

    [When(@"I show the reminders view")]
    public async Task WhenIShowTheRemindersView()
    {
        await _dashboardPage.ShowRemindersViewAsync();
    }

    [Then(@"the dashboard is showing the month view")]
    public async Task ThenTheDashboardIsShowingTheMonthView()
    {
        await Assertions.Expect(_dashboardPage.MonthTable).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    [Then(@"the reminders view is showing")]
    public async Task ThenTheRemindersViewIsShowing()
    {
        await Assertions.Expect(_dashboardPage.RemindersViewContainer).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    // ── What a section/row shows ─────────────────────────────────────────────

    [Then(@"the ""([^""]*)"" section has a row for ""([^""]*)""")]
    public async Task ThenTheSectionHasARowFor(string sectionName, string title)
    {
        var key = ParseSection(sectionName);
        var rows = await _dashboardPage.ReadReminderRowsAsync(key);
        var target = await _dashboardPage.FindReminderRowByTitleAsync(title);

        rows.Should().Contain(
            r => r.EventId == target.EventId,
            $"'{title}' should have a row filed under '{sectionName}'.");
    }

    [Then(@"the ""([^""]*)"" section has no row for ""([^""]*)""")]
    public async Task ThenTheSectionHasNoRowFor(string sectionName, string title)
    {
        var key = ParseSection(sectionName);
        var rows = await _dashboardPage.ReadReminderRowsAsync(key);
        var target = await _dashboardPage.FindReminderRowByTitleAsync(title);

        rows.Should().NotContain(
            r => r.EventId == target.EventId,
            $"'{title}' must not also be filed under '{sectionName}'.");
    }

    [Then(@"the ""([^""]*)"" section has a row for ""([^""]*)"" showing ""([^""]*)"" and its start time")]
    public async Task ThenTheSectionHasARowForShowingAndItsStartTime(
        string sectionName, string title, string leadText)
    {
        // sectionName is not re-resolved here: FindReminderRowByTitleAsync already searches the
        // whole view, and ThenTheSectionHasARowFor (above) is what pins WHICH section a row landed
        // in. This step is about what the row SAYS, not where it is.
        //
        // The start time itself is checked by FORMAT (a bare "HH:mm", the row's leading column — see
        // ReminderRowDisplay.EventTime), not an exact clock value: the event is seeded relative to
        // "now" (see GivenTheUserHasATimedEventStartingInMinutesInCalendar) so its exact wall-clock
        // start isn't known until the seeding step runs, and re-deriving it here would just be
        // re-implementing that computation a second time for no real gain — the row rendering SOME
        // correctly-formatted start time, for the one event the scenario created, is what "and its
        // start" actually asks of this row.
        var row = await _dashboardPage.FindReminderRowByTitleAsync(title);

        row.Text.Should().Contain(leadText, $"the row for '{title}' should state its reminder's lead time.");
        row.Text.Should().MatchRegex(
            @"\b\d{2}:\d{2}\b", $"the row for '{title}' should state when the event itself starts.");
    }

    [Then(@"the ""([^""]*)"" section has a default-tagged row for ""([^""]*)""")]
    public async Task ThenTheSectionHasADefaultTaggedRowFor(string sectionName, string title)
    {
        var key = ParseSection(sectionName);
        var rows = await _dashboardPage.ReadReminderRowsAsync(key);
        var target = await _dashboardPage.FindReminderRowByTitleAsync(title);

        var match = rows.SingleOrDefault(r => r.EventId == target.EventId)
            ?? throw new InvalidOperationException(
                $"'{title}' has no row filed under '{sectionName}' to check the default tag on.");

        match.IsDefault.Should().BeTrue(
            $"'{title}' follows the calendar's usual reminders, so its row should carry the default tag.");
    }

    [Then(@"the ""([^""]*)"" section has a row for ""([^""]*)"" naming (\d+) reminders?")]
    public async Task ThenTheSectionHasARowForNamingReminders(string sectionName, string title, int count)
    {
        var key = ParseSection(sectionName);
        var rows = await _dashboardPage.ReadReminderRowsAsync(key);
        var target = await _dashboardPage.FindReminderRowByTitleAsync(title);

        var matching = rows.Where(r => r.EventId == target.EventId).ToList();

        matching.Should().ContainSingle(
            $"'{title}' carries several reminders, so its section should file ONE row for it, never one per reminder.");

        var noun = count == 1 ? "reminder" : "reminders";
        matching[0].Text.Should().Contain(
            $"{count} {noun}", $"the row for '{title}' should state it has {count} {noun}.");
    }

    [Then(@"the row for ""([^""]*)"" appears only in the ""([^""]*)"" section")]
    public async Task ThenTheRowForAppearsOnlyInTheSection(string title, string sectionName)
    {
        // The business rule the family specifically asked for: a row is filed where its EVENT starts,
        // and nowhere else — not in every section one of its reminders' trigger instants happens to
        // land in. Checking every section rather than just the expected one is the point: a row that
        // ALSO appears somewhere else is exactly the scattering bug this view used to have.
        var expected = ParseSection(sectionName);
        var target = await _dashboardPage.FindReminderRowByTitleAsync(title);

        foreach (var key in Enum.GetValues<ReminderSectionKey>())
        {
            var rows = await _dashboardPage.ReadReminderRowsAsync(key);
            var present = rows.Any(r => r.EventId == target.EventId);

            if (key == expected)
            {
                present.Should().BeTrue(
                    $"'{title}' should be filed under '{sectionName}', the section containing its start.");
            }
            else
            {
                present.Should().BeFalse(
                    $"'{title}' must not also appear under '{key}' — a row is filed once, by its event's start.");
            }
        }
    }

    [Then(@"the ""([^""]*)"" section has a row for ""([^""]*)"" naming ""([^""]*)"" and ""([^""]*)""")]
    public async Task ThenTheSectionHasARowForNamingAnd(
        string sectionName, string title, string person1, string person2)
    {
        var key = ParseSection(sectionName);
        var rows = await _dashboardPage.ReadReminderRowsAsync(key);
        var target = await _dashboardPage.FindReminderRowByTitleAsync(title);

        var match = rows.SingleOrDefault(r => r.EventId == target.EventId)
            ?? throw new InvalidOperationException(
                $"'{title}' has no row filed under '{sectionName}' to check the member names on.");

        match.Text.Should().Contain(person1, $"the shared row for '{title}' should name {person1}.");
        match.Text.Should().Contain(person2, $"the shared row for '{title}' should name {person2}.");
        match.Text.Should().NotContain(
            "Family Events",
            "a shared event's row should name the PEOPLE it is shared with, never the shared calendar itself.");
    }

    [Then(@"the ""([^""]*)"" section is empty")]
    public async Task ThenTheSectionIsEmpty(string sectionName)
    {
        var key = ParseSection(sectionName);

        // Check the actual rows first: a mismatch here fails with the unexpected row's own data in
        // the message, which is far more useful than the plain "found False" a bare boolean leaves.
        var rows = await _dashboardPage.ReadReminderRowsAsync(key);
        rows.Should().BeEmpty($"the '{sectionName}' section should have no events filed into it.");

        var isEmpty = await _dashboardPage.ReminderSectionIsEmptyAsync(key);
        isEmpty.Should().BeTrue(
            $"the '{sectionName}' section has nothing filed into it and should collapse to its placeholder line.");
    }

    [Then(@"no row names ""([^""]*)"" anywhere in the reminders view")]
    public async Task ThenNoRowNamesAnywhereInTheRemindersView(string title)
    {
        var count = await _dashboardPage.CountReminderRowsForTitleAsync(title);

        count.Should().Be(0, $"'{title}' will never have a reminder due, so the timeline must not carry a row for it.");
    }

    [Then(@"the reminders view says there is nothing coming up")]
    public async Task ThenTheRemindersViewSaysThereIsNothingComingUp()
    {
        var empty = await _dashboardPage.RemindersViewIsEntirelyEmptyAsync();

        empty.Should().BeTrue(
            "with no reminders due, the view should state that plainly rather than rendering a blank panel.");
    }

    [Then(@"the all-day footnote is shown")]
    public async Task ThenTheAllDayFootnoteIsShown()
    {
        var text = await _dashboardPage.ReadAllDayFootnoteAsync();

        text.Should().Be(
            "All-day reminders set for the day itself aren't shown — Google doesn't share them.",
            "the footnote is permanent and not dismissible, so its wording should never depend on " +
            "whether the view has anything else to show.");
    }

    // ── Tapping a row ─────────────────────────────────────────────────────────

    [When(@"I tap the reminders row for ""([^""]*)""")]
    public async Task WhenITapTheRemindersRowFor(string title)
    {
        var row = await _dashboardPage.FindReminderRowByTitleAsync(title);
        await _dashboardPage.TapReminderRowAsync(row.EventId);
    }

    [Then(@"the event modal is open on ""([^""]*)"" showing its Reminders tab")]
    public async Task ThenTheEventModalIsOpenOnShowingItsRemindersTab(string title)
    {
        var openTitle = await _dashboardPage.GetEventDetailsAsync();
        openTitle.Should().Be(title, "tapping a row should fetch and open THAT event, by id.");

        await _dashboardPage.AssertModalTabActiveAsync("reminders");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ReminderSectionKey ParseSection(string name) => name switch
    {
        "Today" => ReminderSectionKey.Today,
        "Tomorrow" => ReminderSectionKey.Tomorrow,
        "This week" => ReminderSectionKey.ThisWeek,
        "This month" => ReminderSectionKey.ThisMonth,
        "Next month" => ReminderSectionKey.NextMonth,
        _ => throw new ArgumentOutOfRangeException(
            nameof(name), name,
            "Not a reminders-section heading (\"Today\", \"Tomorrow\", \"This week\", \"This month\", \"Next month\").")
    };
}
