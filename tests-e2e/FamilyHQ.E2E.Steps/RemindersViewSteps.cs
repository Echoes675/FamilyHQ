using System.Globalization;
using FamilyHQ.E2E.Common.Helpers;
using FamilyHQ.E2E.Common.Pages;
using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

/// <summary>
/// Drives the Reminders VIEW — the fourth dashboard tab's standalone timeline of events whose
/// reminders were set on the event itself — as distinct from the event modal's own Reminders tab,
/// which <see cref="EventRemindersSteps"/> already covers.
/// </summary>
/// <remarks>
/// Every assertion here addresses a row by its event id, never by position, for the same reason the
/// modal's Reminders tab rows do: this view sorts by EVENT START, which depends on seeding order
/// rather than on anything Google guarantees, so a row's position on screen is not a stable
/// identifier a scenario can key off.
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

    /// <summary>
    /// The <see cref="ScenarioContext"/> key a seeded event's own start is stashed under, so the
    /// assertion step can check the row's leading time column against the EXACT value seeded rather
    /// than against the shape of a time. Keyed by event name: one scenario can seed several.
    /// </summary>
    private static string SeededStartKey(string eventName) => $"SeededStart:{eventName}";

    // ── Seeding an event today, relative to "now" ───────────────────────────────────────────────
    // Relative to "now" rather than at a fixed clock time so the reminder added on top of it is
    // still in the future wherever in the day the suite runs: a reminder set against a fixed
    // "09:00 today" has already fired for most of the day, which is a different row (one reading
    // "all sent") from the one most of these scenarios assert.
    //
    // The start has to land on TODAY's local date, because the row is filed by the EVENT's own start:
    // once "now" is within `minutes` of local midnight the start rolls into tomorrow and the row is
    // correctly filed under Tomorrow while the scenario still asserts Today. The seeding offset IS
    // the width of that window, so it is refused outright rather than left to fail later as a
    // baffling "no row under Today" — see .agent/skills/fail-fast-standard/SKILL.md.
    [Given(@"the user has a timed event ""([^""]*)"" starting in (\d+) minutes in ""([^""]*)""")]
    public async Task GivenTheUserHasATimedEventStartingInMinutesInCalendar(
        string eventName, int minutes, string calendarName)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName)
                       ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found.");

        var start = BrowserClock.Now.AddMinutes(minutes);
        var startDate = DateOnly.FromDateTime(start);
        if (startDate != BrowserClock.TodayDate)
        {
            throw new InvalidOperationException(
                $"Seeding '{eventName}' {minutes} minutes from now starts it on " +
                $"{startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, not today " +
                $"({BrowserClock.TodayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}): " +
                $"this scenario cannot run within {minutes.ToString(CultureInfo.InvariantCulture)} " +
                "minutes of local midnight, because its row would be filed under Tomorrow.");
        }

        _scenarioContext[SeededStartKey(eventName)] = start;

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
        var row = await _dashboardPage.FindReminderRowByTitleAsync(title);

        row.Text.Should().Contain(leadText, $"the row for '{title}' should state its reminder's lead time.");

        // The leading column is read on its own, and against the exact instant the seeding step
        // stashed. Matching an HH:mm pattern anywhere in the row's text would pass on a row that led
        // with its NEXT REMINDER's time instead of the event's — precisely the regression this view
        // was reworked to stop — and an all-day row's "next 1 day before at 17:00" satisfies such a
        // pattern on its own. Nothing is re-derived here: the seeding step already computed the start.
        if (!_scenarioContext.TryGetValue<DateTime>(SeededStartKey(title), out var seededStart))
        {
            throw new InvalidOperationException(
                $"No seeded start recorded for '{title}', so this step cannot say what time the row " +
                "should lead with. It needs the \"starting in N minutes\" seeding step, which is what " +
                "records it.");
        }

        var expected = seededStart.ToString("HH:mm", CultureInfo.InvariantCulture);
        var shown = await _dashboardPage.ReadReminderRowStartTimeAsync(title);

        shown.Should().Be(expected,
            $"the row for '{title}' should lead with when the EVENT starts, not when a reminder fires.");
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

    [Then(@"the ""([^""]*)"" section has a row for ""([^""]*)"" saying its reminders have all been sent")]
    public async Task ThenTheSectionHasARowForSayingItsRemindersHaveAllBeenSent(string sectionName, string title)
    {
        var key = ParseSection(sectionName);
        var rows = await _dashboardPage.ReadReminderRowsAsync(key);
        var target = await _dashboardPage.FindReminderRowByTitleAsync(title);

        var match = rows.SingleOrDefault(r => r.EventId == target.EventId)
            ?? throw new InvalidOperationException(
                $"'{title}' has no row filed under '{sectionName}'. An event keeps its row until the " +
                "event itself is past, so a reminder having already fired is not a reason for it to " +
                "be missing.");

        match.Text.Should().Contain(
            "all sent",
            $"every reminder on '{title}' has already fired, so its row should say so rather than " +
            "naming a next one — or disappearing.");
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

        // Counted across the whole view first, because the per-section read below only sees RENDERED
        // rows: a collapsed section renders at most RemindersViewLogic.PreviewRows of them, so a
        // duplicate sitting behind "Show all" would read as absent and this step would pass on the
        // very scattering it exists to catch.
        var total = await _dashboardPage.CountReminderRowsForTitleAsync(title);
        total.Should().Be(1, $"'{title}' is one event, so the whole timeline should carry one row for it.");

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

        count.Should().Be(0, $"'{title}' is not an event this view reports, so the timeline must not carry a row for it.");
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

    [Then(@"the inherited-reminders footnote is shown")]
    public async Task ThenTheInheritedRemindersFootnoteIsShown()
    {
        var text = await _dashboardPage.ReadInheritedFootnoteAsync();

        // Asserted on the exact wording rather than just on the element being there: the family
        // agreed to the exclusion on the condition that the view admits it, so a line reworded into
        // something vaguer is the same failure as the line going missing.
        text.Should().Be(
            "Events that just use their calendar's usual reminders aren't listed here.",
            "the view lists only reminders set on an event, and is permanently required to say so.");
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
