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

    // ── Seeding an event today, relative to "now" ───────────────────────────────────────────────
    // NO SCENARIO USES THIS TODAY. The one whose subject was the state of "now" itself — a reminder
    // that has already fired on an event that has not yet happened — reaches that state by putting
    // the REMINDER in the past instead (48 hours before an event tomorrow), which holds at every
    // hour of every day. What is kept here is the GUARD: a scenario that genuinely needs "now" to
    // sit between two instants has no choice but to seed from the clock, and it has to refuse to run
    // inside its own offset of local midnight rather than failing later as a baffling missing row.
    //
    // Every scenario that wants a row dated today uses a FIXED time instead ("at 09:00 on today"),
    // and should: a row is filed by its event's start DATE and reads "Today" for the whole of that
    // day — RemindersController's window opens at local midnight, not at now, and RowFor reads no
    // clock at all. A fixed seed therefore also fails LOUDER than a relative one if that near edge
    // ever moves back to "now", because a 09:00 event would drop out of the response while a future
    // one would not.
    //
    // The start has to land on TODAY's local date, because the row is labelled from the EVENT's own
    // start: once "now" is within `minutes` of local midnight the start rolls into tomorrow and the
    // row correctly reads "Tomorrow" while the scenario still asserts "Today". The seeding offset IS
    // the width of that window, so it is refused outright — see
    // .agent/skills/fail-fast-standard/SKILL.md.
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
                "minutes of local midnight, because its row would read \"Tomorrow\".");
        }

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

    /// <summary>
    /// Gives an event already on the calendar a reminder of its own, through the modal, because
    /// nothing seeds one: the Simulator's event model carries no reminder overrides, so the only way
    /// to reach the state this precondition describes is the write the kiosk itself makes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Takes the event's own date so the grid can be moved to it first. The month grid renders only
    /// the weeks its own month needs and stops as soon as a row completes past the month's end, so
    /// on a month that ends on a Saturday "tomorrow" is not on screen at all and the click would
    /// find nothing — once every seven months or so, which is worse than never.
    /// <see cref="DashboardPage.NavigateToShowDateIfNeededAsync"/> is a no-op for a date already
    /// showing, so passing "today" costs nothing.
    /// </para>
    /// <para>
    /// Inheritance goes off first: the Add form is not offered while an event still follows its
    /// calendar's usual reminders.
    /// </para>
    /// </remarks>
    [Given(@"the event ""([^""]*)"" on ""([^""]*)"" has been given a reminder (\d+) (minutes|hours) before")]
    public async Task GivenTheEventOnDateHasBeenGivenAReminderBefore(
        string title, string dateExpr, int amount, string unit)
    {
        var date = DateTime.ParseExact(
            DateExpressionResolver.Resolve(dateExpr), "yyyy-MM-dd", CultureInfo.InvariantCulture);

        await _dashboardPage.NavigateToShowDateIfNeededAsync(date);
        await _dashboardPage.OpenEventForEditingAsync(title);
        await _dashboardPage.SetReminderInheritanceAsync(follow: false);
        await _dashboardPage.AddTimedReminderAsync(amount, unit);
        await _dashboardPage.SaveOpenEventAsync();
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

    // ── What a panel holds, and what a row says ──────────────────────────────
    //
    // Two different kinds of assertion, and the difference matters. WHICH PANEL holds a row is a
    // statement about the row's date against the month, so a scenario can only name a panel for a
    // date whose panel is the same on every run date: "today", or an event resolved into next
    // month. What a row SAYS is a statement about its own date against today, and is asserted
    // without naming a panel at all — on the last day of a month tomorrow is filed under "Next
    // month" and still reads "Tomorrow", which is the day the family can least afford to lose it.

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

    [Then(@"the row for ""([^""]*)"" leads with ""([^""]*)""")]
    public async Task ThenTheRowForLeadsWith(string title, string expected)
    {
        // No panel is named, deliberately: a row names its own day, read off its own local date
        // against today, never off the heading above it. Naming a panel here would make the
        // expectation a function of the run date — tomorrow is filed under "Next month" on the last
        // day of every month — which is the hazard e2e-testing-maintenance.md records for every
        // relative date expression.
        //
        // EQUALITY on the leading column, not a containment, and that is what the step is for: the
        // failure it guards is a row that dropped its day and reverted to the bare time, and "09:00"
        // is contained in "Today · 09:00". The column is read on its own rather than out of the
        // row's whole text, so no other time rendered inside the row can satisfy it either.
        //
        // The expected text is written out by the scenario rather than read back from what the
        // seeding step computed, which is what a fixed seed time buys: a seeding bug that derived
        // the wrong start would otherwise produce the same wrong value on both sides and pass.
        await _dashboardPage.AssertReminderRowLeadsWithAsync(title, expected);
    }

    [Then(@"the row for ""([^""]*)"" leads with the date (\d+) day after ""([^""]*)"" at ""([^""]*)""")]
    public async Task ThenTheRowForLeadsWithTheDateDaysAfterAt(
        string title, int daysAfter, string dateExpr, string time)
    {
        // The offset is the whole point of the step, and it is stated in the scenario rather than
        // computed here: the event was seeded on one date and the row must name the NEXT one, because
        // the household's zone puts it there. A step that resolved both sides from the same expression
        // would pass on a row that named the seeded date.
        //
        // No panel is named. The seeded date resolves into next month and the household's day is one
        // later, so which panel holds it depends on where the month boundary falls — and what this
        // asserts is what the row SAYS, which is a fact about its own date.
        var expected = DateTime.ParseExact(
                DateExpressionResolver.Resolve(dateExpr), "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .AddDays(daysAfter);
        var expectedLeadingColumn =
            $"{expected.ToString("ddd d MMM", CultureInfo.InvariantCulture)} · {time}";

        await _dashboardPage.AssertReminderRowLeadsWithAsync(title, expectedLeadingColumn);
    }

    [Then(@"the ""([^""]*)"" section has a row for ""([^""]*)"" leading with the day and date of ""([^""]*)""")]
    public async Task ThenTheSectionHasARowForLeadingWithTheDayAndDateOf(
        string sectionName, string title, string dateExpr)
    {
        // The other form the leading column takes: a day further out than tomorrow is named by its
        // date, because a panel covers a whole calendar month and several occurrences of one
        // recurring series would otherwise render as identical rows — the screenshot that prompted
        // this showed three of them.
        //
        // The expected date comes from DateExpressionResolver, the same single source of truth the
        // seeding step resolved the expression through, so this cannot disagree with the seed about
        // what "next month" means. The day NAME is asserted along with the date because the row
        // renders both, and InvariantCulture because that is what the row formats with.
        //
        // The panel IS named here, unlike the step above, and both halves are read off the SAME
        // snapshot, so "which panel holds it" and "what it says" cannot resolve different rows.
        // Naming one costs this step nothing: a date only appears on a row that is neither today
        // nor tomorrow, so a scenario asserting one has already had to seed its event into a panel
        // it knows.
        var expectedDate = DateTime.ParseExact(
            DateExpressionResolver.Resolve(dateExpr), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var expectedPrefix = expectedDate.ToString("ddd d MMM", CultureInfo.InvariantCulture);

        var key = ParseSection(sectionName);
        var rows = await _dashboardPage.ReadReminderRowsAsync(key);
        var target = await _dashboardPage.FindReminderRowByTitleAsync(title);

        var match = rows.SingleOrDefault(r => r.EventId == target.EventId)
            ?? throw new InvalidOperationException(
                $"'{title}' has no row filed under '{sectionName}' to read a date off.");

        match.When.Should().StartWith(
            expectedPrefix,
            $"a row in '{sectionName}' should lead with the day and date of its event, because the " +
            "panel covers a whole month and its heading cannot say which day a row is.");
    }

    [Then(@"the row for ""([^""]*)"" appears only in the ""([^""]*)"" section")]
    public async Task ThenTheRowForAppearsOnlyInTheSection(string title, string sectionName)
    {
        // The business rule the family specifically asked for: a row is filed where its EVENT starts,
        // and nowhere else — not in every panel one of its reminders' trigger instants happens to
        // land in. Checking every panel rather than just the expected one is the point: a row that
        // ALSO appears in the other one is exactly the scattering bug this view used to have, and
        // with two panels either of them can be the wrong one.
        var expected = ParseSection(sectionName);
        var target = await _dashboardPage.FindReminderRowByTitleAsync(title);

        // Counted across the whole view first, because the per-panel read below only sees RENDERED
        // rows: a collapsed panel renders at most RemindersViewLogic.PreviewRows of them, so a
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
                    $"'{title}' should be filed under '{sectionName}', the panel containing its start.");
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

    [Then(@"the day view is showing ""([^""]*)""")]
    public async Task ThenTheDayViewIsShowing(string title)
    {
        // Asserted on the event's own TILE rather than on the Day view merely being visible: the
        // Day view renders only the events filed under the day it is showing, so this can pass only
        // if it opened on the event's day — and, when that day is outside the month the dashboard
        // had loaded, only if the month was fetched first.
        await _dashboardPage.AssertDayViewShowingEventAsync(title);
    }

    // ── The Reminders tab's count of what falls today ────────────────────────
    //
    // The timeline has no "Today" panel to glance at any more, so the tab strip is what tells the
    // family something falls today. Its whole requirement is that it is right while they are
    // standing in front of ANOTHER view, where no part of the reminders list is on screen.

    [Then(@"the Reminders tab shows a bell and reads ""([^""]*)""")]
    public async Task ThenTheRemindersTabShowsABellAndReads(string announcement)
    {
        // The scenario states the whole announcement rather than just a number, because the wording
        // is itself a requirement: a bell glyph and a bare digit tell a screen reader nothing, and a
        // wall display is not the only way this page is read. Writing it out here is also the only
        // thing that pins the singular/plural form — deriving it in the test from the same rule the
        // component applies would assert that the code agrees with itself.
        //
        // The digit is taken from the announcement's first word rather than passed as a second
        // argument, so the two cannot disagree about which number the scenario meant.
        var count = announcement.Split(' ')[0];

        await _dashboardPage.AssertRemindersTabBadgeAsync(announcement, count);
    }

    [Then(@"the Reminders tab shows no bell at all")]
    public async Task ThenTheRemindersTabShowsNoBellAtAll()
    {
        // The absence of the element, not a badge reading zero: silence is the signal, and a pill
        // left on screen saying "0" is a standing claim that something is being tracked today.
        //
        // The caller has to have proved the fetch already landed before reaching this step — the
        // scenario reads a row off the timeline first - because an assertion about something not
        // being there passes instantly against a page that has not finished loading.
        await _dashboardPage.AssertRemindersTabHasNoBadgeAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ReminderSectionKey ParseSection(string name) => name switch
    {
        "This month" => ReminderSectionKey.ThisMonth,
        "Next month" => ReminderSectionKey.NextMonth,
        _ => throw new ArgumentOutOfRangeException(
            nameof(name), name,
            "Not a reminders-panel heading (\"This month\", \"Next month\").")
    };
}
