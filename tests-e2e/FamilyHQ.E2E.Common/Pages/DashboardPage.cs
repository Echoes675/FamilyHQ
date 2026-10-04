using System.Text.RegularExpressions;
using FamilyHQ.E2E.Common.Configuration;
using FamilyHQ.E2E.Common.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace FamilyHQ.E2E.Common.Pages;

/// <summary>
/// The five buckets the Reminders view files its rows into, by each row's EVENT start. A local
/// mirror of the app's own <c>ReminderSectionKey</c> — the E2E projects never reference
/// <c>FamilyHQ.WebUi</c>, so this exists purely to give
/// <see cref="DashboardPage.ReadReminderRowsAsync"/> a type-safe parameter instead of a bare slug
/// string.
/// </summary>
public enum ReminderSectionKey { Today, Tomorrow, ThisWeek, ThisMonth, NextMonth }

/// <summary>
/// One <c>reminder-row</c> read off the DOM by its event id, never by position — see the remarks on
/// the Reminders VIEW region below for why. <paramref name="Text"/> is the row's full rendered text
/// (start, reminder summary, title, member chips), for scenarios that need to read more than the id
/// carries.
/// </summary>
public sealed record ReminderRowSnapshot(Guid EventId, string Text);

public class DashboardPage : BasePage
{
    private readonly TestConfiguration _config;
    public override string PageUrl => _config.BaseUrl + "/";

    public DashboardPage(IPage page) : base(page)
    {
        _config = ConfigurationLoader.Load();
    }

    // Locators
    public ILocator MonthTable => Page.Locator("table.month-table");
    public ILocator DayViewContainer => Page.Locator(".day-view-container");
    public ILocator AgendaViewContainer => Page.Locator(".agenda-view-container");
    public ILocator MonthTab => Page.GetByTestId("month-tab");
    public ILocator DayTab => Page.GetByTestId("day-tab");
    public ILocator AgendaTab => Page.GetByTestId("agenda-tab");
    public ILocator RemindersTab => Page.GetByTestId("reminders-tab");
    public ILocator RemindersViewContainer => Page.GetByTestId("reminders-view");

    /// <summary>
    /// Any one of the dashboard's four views. This is what "the calendar is on screen" means — the
    /// signed-in dashboard as opposed to the login prompt — and it deliberately says nothing about
    /// WHICH view: a page load lands on whichever view the kiosk calls home, so a locator naming one
    /// of them would turn every "the dashboard is up" wait into a claim about the home view.
    /// </summary>
    public ILocator AnyCalendarView => Page.Locator(
        ".month-table, .day-view-container, .agenda-view-container, [data-testid='reminders-view']");
    public ILocator EventCapsules => Page.Locator(".event-capsule");
    public ILocator CurrentTimeLine => Page.Locator(".current-time-line");

    // FHQ-18.11: recurrence affordances. The indicator glyph renders on every recurring tile
    // across Day / Month / Agenda; the subtitle renders inside the open event modal.
    public ILocator RecurrenceIndicators => Page.GetByTestId("recurrence-indicator");
    public ILocator RecurrenceSubtitle => Page.GetByTestId("recurrence-subtitle");
    public ILocator LoginBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Login to Google" });
    public ILocator SignOutBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Sign Out" });
    public ILocator UserInfo => Page.GetByText("Signed in as:");

    // Reauth banner (rendered on the dashboard when AuthStatus is needs_reauth)
    public ILocator ReauthBanner    => Page.GetByTestId("reauth-banner-dashboard");
    public ILocator ReauthBannerCta => Page.GetByTestId("reauth-banner-dashboard-cta");

    public Task<bool> IsReauthBannerVisibleAsync() => ReauthBanner.IsVisibleAsync();

    public async Task<string> GetReauthBannerTextAsync()
    {
        await ReauthBanner.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        return (await ReauthBanner.InnerTextAsync()).Trim();
    }
    private ILocator NextMonthBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Next ›" });
    private ILocator PrevMonthBtn => Page.GetByRole(AriaRole.Button, new() { Name = "‹ Prev" });
    private ILocator AddEventBtn => Page.GetByTestId("add-event-btn");

    // Modal Locators
    private ILocator EventTitleInput => Page.GetByPlaceholder("e.g. Doctor Appointment");
    private ILocator SaveEventBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Save" });
    private ILocator DeleteEventBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Delete" });
    private ILocator EventModal => Page.Locator(".modal-content");
    private ILocator DayPickerBtn => Page.GetByTestId("day-picker-btn");
    private ILocator DayPickerInput => Page.GetByTestId("day-picker-input");
    private ILocator DayPickerGoBtn => Page.GetByTestId("day-picker-go-btn");
    private ILocator DayPickerModal => Page.Locator(".modal-backdrop").Filter(new() { HasText = "Select Date" });

    /// <summary>FHQ-63: Day-view centre header button (the day-picker), for web-first assertions.</summary>
    public ILocator DayHeaderButton => DayPickerBtn;
    /// <summary>FHQ-63: Month-view centre header button (month-year label), for web-first assertions.</summary>
    public ILocator MonthHeaderButton => Page.GetByTestId("month-header-btn");
    /// <summary>FHQ-63: Agenda-view month-year label, for web-first assertions.</summary>
    public ILocator AgendaMonthYearLabel => Page.GetByTestId("agenda-month-year-label");

    // Weather Locators
    public ILocator WeatherStrip => Page.Locator(".weather-strip");
    public ILocator WeatherStripTemp => Page.Locator(".weather-strip__temp");
    public ILocator WeatherStripCondition => Page.Locator(".weather-strip__condition");
    public ILocator WeatherStripForecastDays => Page.Locator(".weather-strip__forecast-day");
    public ILocator WeatherStripForecast => Page.GetByTestId("weather-strip-forecast");
    public ILocator WeatherStripForecastContainerDays =>
        WeatherStripForecast.Locator(".weather-strip__forecast-day");
    public ILocator WeatherOverlay => Page.Locator("#weather-overlay");

    public ILocator AgendaWeatherForDate(string dateKey) =>
        Page.GetByTestId($"agenda-day-label-{dateKey}").Locator(".agenda-weather");
    public ILocator AgendaWeatherTemps(string dateKey) =>
        Page.GetByTestId($"agenda-day-label-{dateKey}").Locator(".agenda-weather__temps");
    public ILocator DayHourTemps => Page.Locator(".day-hour-temp");

    // Actions

    /// <summary>
    /// Navigates to the dashboard and waits for the events API response before returning,
    /// ensuring calendar data is rendered. Listener is set up before navigation to avoid
    /// missing fast responses.
    /// </summary>
    public async Task NavigateAndWaitAsync()
    {
        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });
        await NavigateAsync();
        await eventsResponseTask;
        
        // Wait for either view to be ready
        await WaitForCalendarVisibleAsync();
    }

    /// <summary>
    /// Loads the dashboard as the kiosk does on power-on and leaves it on whatever view the app
    /// itself lands on — no tab is tapped, which is the whole point of this method existing beside
    /// <see cref="NavigateAndWaitAsync"/>.
    /// <para>
    /// Waits for the upcoming-reminders response as well as the events one, so a landing path that
    /// rendered the timeline without fetching its rows fails here, where the cause is obvious,
    /// rather than later as an unexplained missing row. The listener is registered before navigation
    /// for the same reason <see cref="NavigateAndWaitAsync"/> registers its own first: the response
    /// can land before the await is reached.
    /// </para>
    /// </summary>
    public async Task LoadKioskHomeViewAsync()
    {
        var remindersResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/reminders/upcoming"),
            new() { Timeout = 30000 });

        await NavigateAndWaitAsync();
        await remindersResponseTask;

        await RemindersViewContainer.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    /// <summary>
    /// The dashboard's view-tab labels in the order they are rendered, left to right.
    /// <para>
    /// Scoped to <c>#dashboard-container</c> deliberately: the event modal's own tab strip carries
    /// the same <c>.view-tabs</c>/<c>.view-tab</c> classes for its look, and it is rendered outside
    /// that container, so an unscoped selector would read the modal's tabs too whenever one is open.
    /// </para>
    /// </summary>
    public Task<string[]> ReadViewTabLabelsAsync() =>
        Page.Locator("#dashboard-container .view-tabs .view-tab")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.innerText.trim())");

    private async Task WaitForCalendarVisibleAsync()
    {
        // Wait for ANY of the four views to be on screen — that is what "the dashboard has finished
        // loading" means, and which one it is depends on where the app lands rather than on
        // anything a caller chose. The Reminders timeline is in AnyCalendarView because it is what a
        // page load now lands on: without it every navigation here would wait out its whole timeout
        // for a month/day/agenda container that is not rendered until a tab is tapped.
        // This is safe to call at any point — it simply waits for the final rendered state.
        // We intentionally do NOT wait for the spinner first because in some flows
        // (e.g. after OAuth redirect) the spinner may never appear in the DOM.
        await AnyCalendarView.First.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    // ── FHQ-29 instrumentation ───────────────────────────────────────────────
    // The multi-calendar chip scenarios (add/remove chip, "no capsule" assertion)
    // have flaked with a bare 30s Playwright timeout in CI (Deploy-Staging #103),
    // giving no clue WHICH await hung. These helpers wrap each timeout-prone await
    // so any TimeoutException is rethrown with a snapshot of the relevant page
    // state. The channel is the exception message — it surfaces in the xUnit/TRX
    // test report (the only forensic channel that survives to CI; there is no
    // artifact archiving) — matching the existing [FHQ-28] diagnostic convention.
    // Grep CI logs for "[FHQ-29 diagnostic]" to triage a recurrence.

    /// <summary>
    /// Runs <paramref name="action"/>; if it times out, rethrows the TimeoutException
    /// with an <c>[FHQ-29 diagnostic]</c> snapshot of the page/modal/grid state appended.
    /// </summary>
    private async Task RunWithChipDiagnosticAsync(string operation, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (TimeoutException ex)
        {
            var state = await CaptureChipScenarioStateAsync();
            throw new TimeoutException(
                $"{ex.Message} [FHQ-29 diagnostic] operation='{operation}' | {state}", ex);
        }
    }

    /// <summary>
    /// Best-effort snapshot of the chip-scenario page state for embedding in a diagnostic
    /// message. Never throws — each probe degrades to a "(failed: …)" token so a capture
    /// problem can never mask the original timeout.
    /// </summary>
    private async Task<string> CaptureChipScenarioStateAsync()
    {
        var parts = new List<string>();

        try { parts.Add($"url={Page.Url}"); }
        catch (Exception ex) { parts.Add($"url=(failed: {ex.Message})"); }

        try
        {
            var modalVisible = await EventModal.IsVisibleAsync();
            parts.Add($"modal-visible={modalVisible}");
            if (modalVisible)
            {
                var chipTexts = await EventModal.Locator(".chip").AllInnerTextsAsync();
                parts.Add($"modal-chips=[{string.Join(" | ", chipTexts)}]");

                var removeLabels = await EventModal.Locator(".chip-remove")
                    .EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('aria-label') ?? '(no-label)')");
                parts.Add($"remove-buttons=[{string.Join(" | ", removeLabels)}]");

                var modalHtml = await EventModal.InnerHTMLAsync();
                if (modalHtml.Length > 1500) modalHtml = modalHtml.Substring(0, 1500) + "…(truncated)";
                parts.Add($"modal-html-head={modalHtml}");
            }
        }
        catch (Exception ex) { parts.Add($"modal-state=(failed: {ex.Message})"); }

        try
        {
            // text + inline style (which carries the per-calendar background colour the
            // capsule assertions key off) for every capsule currently on the grid.
            var capsules = await EventCapsules.EvaluateAllAsync<string[]>(
                "els => els.map(e => `${e.innerText}::${e.getAttribute('style') ?? ''}`)");
            parts.Add($"grid-capsules=[{string.Join(" | ", capsules)}]");
        }
        catch (Exception ex) { parts.Add($"grid-capsules=(failed: {ex.Message})"); }

        return string.Join(" ", parts);
    }
    // ─────────────────────────────────────────────────────────────────────────

    public async Task SwitchToDayViewAsync()
    {
        await DayTab.ClickAsync();
        await DayViewContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task SwitchToMonthViewAsync()
    {
        await MonthTab.ClickAsync();
        await MonthTable.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    /// <summary>
    /// Puts the dashboard on the month grid, which is where a scenario that has not said otherwise
    /// has always started — it is simply what a page load used to land on. Now that the kiosk opens
    /// on the Reminders timeline instead, the grid has to be asked for rather than inherited, and
    /// this is the one place that says so: the steps that leave a scenario sitting on the dashboard
    /// call it, so the starting view is explicit setup instead of a side effect of loading the page.
    /// <para>
    /// Deliberately unconditional and deliberately not called from the ready-waits. A scenario about
    /// the home view itself must reach the dashboard through
    /// <see cref="LoadKioskHomeViewAsync"/>, and one that has switched to the Day or Agenda view must
    /// not have the grid put back under it.
    /// </para>
    /// </summary>
    public Task ShowCalendarGridAsync() => SwitchToMonthViewAsync();

    public async Task SwitchToAgendaViewAsync()
    {
        await AgendaTab.ClickAsync();
        await AgendaViewContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task NavigateAgendaPrevMonthAsync()
    {
        var current = await GetAgendaCurrentMonthAsync();
        var expectedText = current.AddMonths(-1).ToString("MMMM yyyy");
        await Page.GetByTestId("agenda-prev-month").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("agenda-month-year-label"))
            .ToHaveTextAsync(expectedText, new() { Timeout = 30000 });
        await Page.WaitForTimeoutAsync(1000);
    }

    public async Task NavigateAgendaNextMonthAsync()
    {
        var current = await GetAgendaCurrentMonthAsync();
        var expectedText = current.AddMonths(1).ToString("MMMM yyyy");
        await Page.GetByTestId("agenda-next-month").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("agenda-month-year-label"))
            .ToHaveTextAsync(expectedText, new() { Timeout = 30000 });
        await Page.WaitForTimeoutAsync(1000);
    }

    public async Task<string> GetAgendaMonthYearTextAsync()
    {
        return (await Page.GetByTestId("agenda-month-year-label").InnerTextAsync()).Trim();
    }

    private async Task<DateTime> GetAgendaCurrentMonthAsync()
    {
        var text = await GetAgendaMonthYearTextAsync();
        return DateTime.ParseExact(text, "MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Drives the agenda's own month navigation until the rendered month contains <paramref name="date"/>.
    /// <para>
    /// The agenda renders exactly one calendar month — one row per day of it and nothing either side.
    /// A cell keyed on a date outside that month therefore does not exist at all, so an assertion
    /// reading it waits out its whole timeout for an element that can never appear. That is not a
    /// timing problem and no amount of waiting fixes it: the seeded date and the rendered view have to
    /// be made to agree. Any relative seed date can fall outside the month — "tomorrow" does on the
    /// last day of every month — so every agenda assertion keyed on a date needs the view moved to it
    /// first.
    /// </para>
    /// <para>
    /// Each step re-reads the live month-year label rather than counting clicks from a month the caller
    /// assumed, so this lands on the right month wherever the view happens to start, and it cannot be
    /// fooled by a view that moved for some other reason.
    /// </para>
    /// </summary>
    public async Task ShowAgendaMonthContainingAsync(DateOnly date)
    {
        var target = new DateTime(date.Year, date.Month, 1);

        // Two years of steps either way. A seed further out than that is a broken scenario, not a
        // view that needs more navigation — so say so rather than asserting against an empty month.
        for (var step = 0; step < 24; step++)
        {
            var current = await GetAgendaCurrentMonthAsync();
            if (current.Year == target.Year && current.Month == target.Month) return;

            if (current < target)
                await NavigateAgendaNextMonthAsync();
            else
                await NavigateAgendaPrevMonthAsync();
        }

        throw new InvalidOperationException(
            $"The agenda did not reach {target:MMMM yyyy} within 24 month steps; " +
            $"it is showing '{await GetAgendaMonthYearTextAsync()}'.");
    }

    /// <summary>Agenda day rows. Exposed for web-first count assertions (FHQ-41).</summary>
    public ILocator AgendaDayRows => Page.Locator(".agenda-day-row");

    public async Task<bool> HasTodayRowHighlightAsync()
    {
        try
        {
            await Assertions.Expect(Page.Locator(".agenda-today-row"))
                .ToHaveCountAsync(1, new() { Timeout = 5000 });
            return true;
        }
        catch (PlaywrightException) { return false; }
    }

    public async Task<bool> WeekendRowsHaveClassAsync()
    {
        try
        {
            await FamilyHQ.E2E.Common.Helpers.Polling.UntilAsync(
                async () => await Page.Locator(".agenda-weekend-row").CountAsync() >= 8,
                "Expected >= 8 .agenda-weekend-row after render", timeoutMs: 5000);
            return true;
        }
        catch (TimeoutException) { return false; }
    }

    public async Task<int> GetWeekdayRowsWithoutWeekendClassAsync()
    {
        await Page.Locator(".agenda-day-row").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var counts = await Page.EvaluateAsync<int[]>(
            "() => [document.querySelectorAll('.agenda-day-row').length, document.querySelectorAll('.agenda-weekend-row').length]");
        return counts[0] - counts[1];
    }

    public async Task<bool> IsAgendaCalendarHeaderVisibleAsync(string calendarName)
    {
        var header = Page.Locator("[data-testid^='agenda-calendar-header-']")
                         .Filter(new() { HasText = calendarName })
                         .First;
        try
        {
            await header.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsAgendaEventVisibleAsync(string expectedText, string dateKey, Guid calendarId)
    {
        var cell = Page.GetByTestId($"agenda-cell-{dateKey}-{calendarId}");
        return await cell.GetByText(expectedText, new() { Exact = false }).CountAsync() > 0;
    }

    public async Task<bool> IsAgendaOverflowVisibleAsync(string dateKey, Guid calendarId)
    {
        return await Page.GetByTestId($"agenda-overflow-{dateKey}-{calendarId}").CountAsync() > 0;
    }

    public async Task TapAgendaEventAsync(string eventText, string dateKey, Guid calendarId)
    {
        var cell = Page.GetByTestId($"agenda-cell-{dateKey}-{calendarId}");
        await cell.GetByText(eventText, new() { Exact = false }).First.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task TapAgendaCellAsync(string dateKey, Guid calendarId)
    {
        // Click the cell itself (not an event line) to trigger the create modal
        await Page.GetByTestId($"agenda-cell-{dateKey}-{calendarId}").ClickAsync(
            new() { Position = new Position { X = 5, Y = 5 } });
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task TapAgendaFilledCellAsync(string dateKey, Guid calendarId)
    {
        // Click a cell that contains events — navigates to Day view
        await Page.GetByTestId($"agenda-cell-{dateKey}-{calendarId}").ClickAsync(
            new() { Position = new Position { X = 5, Y = 5 } });
        await DayViewContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task TapAgendaOverflowAsync(string dateKey, Guid calendarId)
    {
        await Page.GetByTestId($"agenda-overflow-{dateKey}-{calendarId}").ClickAsync();
        await DayViewContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task TapAgendaCreateButtonAsync()
    {
        await Page.GetByTestId("agenda-create-button").ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task<string> GetModalStartDateValueAsync()
    {
        // Start date is the first date input in the modal, value format: "yyyy-MM-dd"
        var input = EventModal.Locator("input[type='date']").First;
        return await input.InputValueAsync();
    }

    public async Task<bool> IsCalendarChipActiveAsync(string calendarName)
    {
        // Web-first: the chip's active class is applied as the modal pre-selects calendars; a single
        // class read can race that render. ToHaveClassAsync auto-retries against the live DOM (FHQ-41);
        // a web-first failure throws PlaywrightException, mapped back to false to preserve the bool.
        var chip = EventModal.Locator(".chip").Filter(new() { HasText = calendarName });
        try
        {
            await Assertions.Expect(chip).ToHaveClassAsync(new Regex("chip-active"), new() { Timeout = 5000 });
            return true;
        }
        catch (PlaywrightException) { return false; }
    }

    public async Task OpenDayPickerAndGoAsync(string dateYyyyMmDd)
    {
        // Click the center date header button on Day view
        await DayPickerBtn.ClickAsync();
        await DayPickerModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await DayPickerInput.FillAsync(dateYyyyMmDd);
        await DayPickerGoBtn.ClickAsync();

        // Ensure modal is gone before proceeding
        await DayPickerModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await WaitForCalendarVisibleAsync();
    }

    public async Task<string> GetDayPickerButtonTextAsync()
    {
        await DayPickerBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return await DayPickerBtn.InnerTextAsync();
    }

    public async Task ClickMoreEventsLinkAsync(string dayDateString)
    {
        // Click the +n more text on the month view
        var link = Page.GetByText(new Regex(@"^\+\d+ more$"));
        // Need to narrow down to the specific day's more link if provided, but since most tests only run on specific days we can just pick the first visible or use nth(0) assuming our tests are isolated.
        // Actually best is to find the cell by date. The cell has an id like `day-cell-2026-03-24`
        var cell = Page.Locator($"#day-cell-{dayDateString}");
        await cell.GetByTestId("overflow-indicator").ClickAsync();
        await DayViewContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task ClickDayGridSlotAsync(string calendarName, string timeString)
    {
        var calHeader = Page.Locator(".calendar-header-col").Filter(new() { HasText = calendarName });
        int colIndex = await calHeader.EvaluateAsync<int>(
            "el => Array.from(el.parentNode.children).indexOf(el) - 1"); // -1 for time-axis
        var col = Page.Locator(".calendar-col").Nth(colIndex);

        var hParts = timeString.Split(':');
        int totalMinutes = int.Parse(hParts[0]) * 60 + int.Parse(hParts[1]);

        // Pin the day-view-container scroll so Playwright's geometry and the
        // production click handler agree on what column-relative Y means. This
        // overrides the app's OnAfterRenderAsync scroll-to-now behaviour for the
        // duration of the test interaction, removing the race window that produced
        // the wrong-time click on Deploy-Staging #89 (2026-05-09). See FHQ-17.
        await Page.EvaluateAsync(@"(targetY) => {
            const c = document.getElementById('day-view-container');
            if (!c) return;
            c.scrollTop = Math.max(0, targetY - c.clientHeight / 2);
        }", totalMinutes);

        await col.ClickAsync(new() { Position = new Position { X = 10, Y = totalMinutes } });

        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    /// <summary>
    /// Waits for the next events API response. Call this only when you know a response
    /// is already in-flight (e.g., immediately after a UI action that triggers a reload).
    /// </summary>
    public async Task WaitForCalendarToLoadAsync()
    {
        await Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });
        await WaitForCalendarVisibleAsync();
    }

    /// <summary>Awaits the current user's sync queue draining (FHQ-41). No-op when already idle.</summary>
    public Task WaitForSyncSettledAsync()
        => FamilyHQ.E2E.Common.Helpers.SyncSettle.WaitForUserQueueDrainAsync(Page);

    public async Task WaitForWeatherStripAsync(int timeoutMs = 60000)
    {
        await Assertions.Expect(WeatherStrip).ToBeVisibleAsync(
            new() { Timeout = timeoutMs });
    }

    public async Task LoginAsync(string userName)
    {
        // Follow the full OAuth redirect chain:
        // /api/auth/login → simulator consent page → /api/auth/callback → /login-success → /
        await LoginBtn.ClickAsync();
        await Page.WaitForURLAsync(url => url.Contains("/oauth2/auth"), new() { Timeout = 30000 });
        await Page.Locator("select#selectedUserId").SelectOptionAsync(new SelectOptionValue { Label = userName });
        await Page.Locator("button[type='submit']").ClickAsync();
        await WaitForCalendarToLoadAsync();
    }

    public async Task SignOutAsync()
    {
        // The sign-out button moved to the Settings page (Task 11).
        // For test isolation, clear the auth token from localStorage directly
        // and reload to force the unauthenticated state.
        if (await IsSignedInAsync())
        {
            await Page.EvaluateAsync("() => { localStorage.clear(); sessionStorage.clear(); }");
            await Page.GotoAsync(_config.BaseUrl + "/");
            await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);
            await LoginBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        }
    }

    public async Task<bool> IsSignedInAsync()
    {
        // The dashboard header (brand + settings gear) is only rendered when authenticated.
        // When not authenticated, only the Login to Google button is shown.
        return await Page.Locator(".dashboard-header").CountAsync() > 0;
    }

    public async Task CreateEventAsync(string title)
    {
        await AddEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await FillAndSaveEventAsync(title);
    }

    /// <summary>
    /// Fills in the event title and saves when the modal is already open
    /// (e.g. after clicking a Day View grid slot).
    /// </summary>
    public async Task FillAndSaveEventAsync(string title)
    {
        await EventTitleInput.FillAsync(title);

        // FHQ-32: the create modal no longer pre-selects a default calendar, so a plain
        // create must explicitly pick one or the empty-selection guard blocks Save. If a
        // caller already seeded a selection (e.g. a day/agenda slot tap passes an explicit
        // calendarId, leaving its chip active) this is a no-op; otherwise select the first
        // available calendar chip.
        var activeChips = EventModal.Locator(".chip-active");
        if (await activeChips.CountAsync() == 0)
        {
            var firstChip = EventModal.Locator(".chip").First;
            await firstChip.ClickAsync();
            await Assertions.Expect(firstChip).ToHaveClassAsync(new Regex("chip-active"), new() { Timeout = 5000 });
        }

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
        await WaitForSyncSettledAsync();
    }

    public async Task UpdateEventAsync(string oldTitle, string newTitle)
    {
        await Page.GetByText(oldTitle).First.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await EventTitleInput.FillAsync(newTitle);

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
        await WaitForSyncSettledAsync();
    }

    public async Task ChangeEventCalendarAsync(string eventName, string targetCalendarName)
    {
        await Page.GetByText(eventName).First.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        // Activate the target calendar chip if it is not already active.
        var targetChip = EventModal.Locator($".chip >> text={targetCalendarName}");
        var targetClasses = await targetChip.GetAttributeAsync("class") ?? "";
        if (!targetClasses.Contains("chip-active"))
            await targetChip.ClickAsync();

        // Deactivate all active chips that are not the target calendar.
        // Iterate in reverse to avoid index drift as chips change state.
        var activeChips = EventModal.Locator(".chip-active");
        var activeCount = await activeChips.CountAsync();
        for (int i = activeCount - 1; i >= 0; i--)
        {
            var chip = activeChips.Nth(i);
            var text = await chip.InnerTextAsync();
            if (text.Contains(targetCalendarName)) continue;

            var removeBtn = chip.Locator(".chip-remove");
            if (await removeBtn.CountAsync() > 0)
                await removeBtn.ClickAsync();
        }

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
        await WaitForSyncSettledAsync();
    }

    public async Task DeleteEventAsync(string title)
    {
        await Page.GetByText(title).First.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await DeleteEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
        await WaitForSyncSettledAsync();
    }

    public async Task ClickEventAsync(string eventName)
    {
        await Page.GetByText(eventName).First.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task<string> GetEventDetailsAsync()
    {
        return await EventTitleInput.InputValueAsync();
    }

    public async Task NavigateToNextMonthAsync()
    {
        var nextMonth = BrowserClock.Today.AddMonths(1);
        var expectedMonthText = nextMonth.ToString("MMMM yyyy"); // e.g. "April 2026"

        await NextMonthBtn.ClickAsync();

        // Wait for the month header button to display the next month, confirming the
        // navigation has been processed and the UI has re-rendered.
        var monthHeaderBtn = Page.GetByRole(AriaRole.Button, new() { Name = expectedMonthText });
        await monthHeaderBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    /// <summary>
    /// Returns the dashboard to the month containing today, clicking Prev until the month header
    /// says so.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="NavigateToNextMonthAsync"/>, and formatted the same way on
    /// purpose, so the two helpers and the header they both read agree by construction. A scenario
    /// needs this when it had to navigate FORWARD to edit a future event and then has to exercise a
    /// path whose whole point is that the event is OUTSIDE the loaded month — without coming back,
    /// the month is loaded and the path is never taken.
    /// <para>
    /// Bounded rather than a <c>while</c>: a header that never arrives means a page that is not
    /// rendering, and saying how many clicks were tried is more use than a bare timeout on the last
    /// one. Six is well past the one click any current caller needs.
    /// </para>
    /// </remarks>
    public async Task NavigateToCurrentMonthAsync()
    {
        var expectedMonthText = BrowserClock.Today.ToString("MMMM yyyy"); // e.g. "April 2026"
        var monthHeaderBtn = Page.GetByRole(AriaRole.Button, new() { Name = expectedMonthText });

        for (var clicks = 0; clicks < 6; clicks++)
        {
            if (await monthHeaderBtn.CountAsync() > 0)
            {
                await monthHeaderBtn.WaitForAsync(
                    new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
                return;
            }

            await PrevMonthBtn.ClickAsync();
            await WaitForCalendarVisibleAsync();
        }

        throw new InvalidOperationException(
            $"The month grid never showed '{expectedMonthText}' after six Prev clicks, so the " +
            "dashboard could not be returned to the current month.");
    }

    /// <summary>
    /// Navigates to the next month only if <paramref name="date"/> falls outside the
    /// currently-visible month grid. The grid spans from the Sunday on or before the
    /// first of the current month to the Saturday on or after the last day of the month.
    /// </summary>
    public async Task NavigateToShowDateIfNeededAsync(DateTime date)
    {
        var today = BrowserClock.Today;
        var lastDayOfMonth = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        var daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)lastDayOfMonth.DayOfWeek + 7) % 7;
        var gridEnd = lastDayOfMonth.AddDays(daysUntilSaturday);

        if (date > gridEnd)
        {
            await NavigateToNextMonthAsync();
        }
    }

    public async Task OpenEventForEditingAsync(string eventName)
    {
        await RunWithChipDiagnosticAsync($"open event '{eventName}' for editing", async () =>
        {
            await Page.GetByText(eventName).First.ClickAsync();
            await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        });
    }

    /// <summary>
    /// Activates the chip for <paramref name="calendarName"/> in the event modal chip selector,
    /// saves the event and waits for the calendar to reload.
    /// </summary>
    public async Task AddCalendarChipToEventAsync(string calendarName)
    {
        var chip = EventModal.Locator($".chip >> text={calendarName}");
        await chip.ClickAsync();

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
        await WaitForSyncSettledAsync();
    }

    /// <summary>
    /// Removes the chip for <paramref name="calendarName"/> from the event modal chip selector,
    /// saves the event and waits for the calendar to reload.
    /// </summary>
    public async Task RemoveCalendarChipFromEventAsync(string calendarName)
    {
        var removeBtn = EventModal.Locator($"[aria-label='Remove {calendarName}']");

        // The remove button never appearing is the original FHQ-29 symptom; the
        // diagnostic snapshot shows which chips/remove-buttons ARE rendered, so we
        // can tell whether the multi-calendar setup failed to associate the event
        // with all expected calendars (bug upstream of the click).
        await RunWithChipDiagnosticAsync($"click remove button for '{calendarName}' chip",
            () => removeBtn.ClickAsync());

        await RunWithChipDiagnosticAsync($"save after removing '{calendarName}' chip", async () =>
        {
            var eventsResponseTask = Page.WaitForResponseAsync(
                r => r.Url.Contains("api/calendars/events"),
                new() { Timeout = 30000 });

            await SaveEventBtn.ClickAsync();
            await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
            await eventsResponseTask;
            await WaitForCalendarVisibleAsync();
            await WaitForSyncSettledAsync();
        });
    }

    /// <summary>
    /// Creates an event in two named calendars by filling the title and activating both chips.
    /// </summary>
    public async Task CreateEventInCalendarsAsync(string title, string calendarName1, string calendarName2)
    {
        await AddEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await EventTitleInput.FillAsync(title);

        // Activate both chips — the primary calendar chip may already be active;
        // clicking an already-active chip toggles it off, so we check state first.
        // After each click, wait for Blazor to reflect the active state in the DOM
        // before proceeding, to avoid a race where Save fires before state is committed.
        //
        // Use HasText filter to target the outer .chip <div>, not the inner <span class="chip-name">.
        // The ">>" chain would resolve to the inner span which does not carry chip-active.
        var chip1 = EventModal.Locator(".chip").Filter(new() { HasText = calendarName1 });
        var chip1Classes = await chip1.GetAttributeAsync("class") ?? "";
        if (!chip1Classes.Contains("chip-active"))
        {
            await chip1.ClickAsync();
            await Assertions.Expect(chip1).ToHaveClassAsync(new Regex("chip-active"), new() { Timeout = 5000 });
        }

        var chip2 = EventModal.Locator(".chip").Filter(new() { HasText = calendarName2 });
        var chip2Classes = await chip2.GetAttributeAsync("class") ?? "";
        if (!chip2Classes.Contains("chip-active"))
        {
            await chip2.ClickAsync();
            await Assertions.Expect(chip2).ToHaveClassAsync(new Regex("chip-active"), new() { Timeout = 5000 });
        }

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await Assertions.Expect(EventModal).ToBeHiddenAsync(new() { Timeout = 30000 });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
        await WaitForSyncSettledAsync();
    }

    /// <summary>
    /// Creates an event from the agenda create button with the given title, description,
    /// and primary calendar pill. Used to exercise description-name parsing behaviour
    /// where additional member names in the description are auto-detected.
    /// </summary>
    public async Task CreateEventWithDescriptionInCalendarAsync(string title, string description, string primaryCalendarName)
    {
        await AddEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await EventTitleInput.FillAsync(title);

        // Ensure only the primary calendar chip is active.
        var primaryChip = EventModal.Locator(".chip").Filter(new() { HasText = primaryCalendarName });
        var primaryClasses = await primaryChip.GetAttributeAsync("class") ?? "";
        if (!primaryClasses.Contains("chip-active"))
        {
            await primaryChip.ClickAsync();
            await Assertions.Expect(primaryChip).ToHaveClassAsync(new Regex("chip-active"), new() { Timeout = 5000 });
        }

        var descriptionInput = EventModal.Locator("textarea");
        await descriptionInput.FillAsync(description);

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await Assertions.Expect(EventModal).ToBeHiddenAsync(new() { Timeout = 30000 });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
        await WaitForSyncSettledAsync();
    }

    // --- FHQ-32: create modal must not silently default the calendar selection ---

    /// <summary>Opens the create-event modal via the Add Event button.</summary>
    public async Task OpenCreateEventModalAsync()
    {
        await AddEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    /// <summary>
    /// Calendar chips offered in the open modal that match <paramref name="calendarName"/>.
    /// Exposed as a locator so callers can use web-first auto-retrying count assertions
    /// (FHQ-41) instead of a single point-in-time CountAsync.
    /// </summary>
    public ILocator ModalChipsFor(string calendarName)
        => EventModal.Locator(".chip").Filter(new() { HasText = calendarName });

    /// <summary>
    /// Fills the title and clicks Save without selecting any calendar. Does NOT wait for the
    /// modal to close — the empty-selection guard is expected to keep it open.
    /// </summary>
    public async Task AttemptSaveWithoutCalendarAsync(string title)
    {
        await EventTitleInput.FillAsync(title);
        await SaveEventBtn.ClickAsync();
    }

    /// <summary>
    /// True when the modal is still open and showing the save-time calendar validation error.
    /// The <c>.alert-danger</c> banner is only populated when Save is attempted with an empty
    /// selection, so this proves the Save path was reached and blocked.
    /// </summary>
    public async Task<bool> ModalShowsCalendarValidationErrorAsync()
    {
        // Web-first: the alert is populated when Save is blocked and the modal stays open.
        // ToContainTextAsync auto-retries against the live banner rather than reading once
        // and racing the validation re-render (FHQ-41). A web-first failure throws
        // PlaywrightException, which we map back to false to preserve the bool contract.
        var alert = EventModal.Locator(".alert-danger");
        try
        {
            await Assertions.Expect(alert).ToContainTextAsync(
                new Regex("calendar", RegexOptions.IgnoreCase), new() { Timeout = 5000 });
            await Assertions.Expect(EventModal).ToBeVisibleAsync(new() { Timeout = 5000 });
            return true;
        }
        catch (PlaywrightException) { return false; }
    }

    /// <summary>
    /// The event modal's error banner (FHQ-175). Carries either a server-vetted message or the
    /// modal's generic "please try again" fallback.
    /// </summary>
    public ILocator EventModalError => EventModal.GetByTestId("event-modal-error");

    /// <summary>
    /// Opens the create modal, fills the title, selects the first calendar chip and clicks Save, then
    /// waits for the API's response to the save — whatever its status. Does NOT wait for the modal
    /// to close: callers use this when the save is expected to be refused and the modal to stay open
    /// showing an error (FHQ-175).
    /// </summary>
    public async Task AttemptCreateEventAsync(string title)
    {
        await AddEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await EventTitleInput.FillAsync(title);

        var firstChip = EventModal.Locator(".chip").First;
        await firstChip.ClickAsync();
        await Assertions.Expect(firstChip).ToHaveClassAsync(new Regex("chip-active"), new() { Timeout = 5000 });

        // Not filtered on status: the refused-save scenarios expect a 4xx and the click must still
        // complete cleanly (mirrors SettingsPage.ClickSyncNowAsync).
        var saveResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/events") && r.Request.Method == "POST",
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await saveResponseTask;
    }

    /// <summary>Cancels the open event modal and waits for it to close.</summary>
    public async Task CancelEventModalAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
    }

    /// <summary>Creates an event with the title assigned to exactly one named calendar.</summary>
    public async Task CreateEventInCalendarAsync(string title, string calendarName)
    {
        await AddEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await EventTitleInput.FillAsync(title);

        var chip = EventModal.Locator(".chip").Filter(new() { HasText = calendarName });
        var classes = await chip.GetAttributeAsync("class") ?? "";
        if (!classes.Contains("chip-active"))
        {
            await chip.ClickAsync();
            await Assertions.Expect(chip).ToHaveClassAsync(new Regex("chip-active"), new() { Timeout = 5000 });
        }

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
        await WaitForSyncSettledAsync();
    }

    // FHQ-63: kiosk day-rollover dev hooks. window.familyHqKiosk is attached by Index only
    // when FeatureClockOverride is on. setIdle forces the idle path; advanceDays moves the
    // displayed "today"; runIdleCheck forces an immediate evaluation (no 30s poll wait).

    /// <summary>Forces the idle stamp back by <paramref name="minutes"/> so the next check sees an idle kiosk.</summary>
    public Task ForceIdleMinutesAsync(int minutes) =>
        Page.EvaluateAsync("ms => window.familyHqKiosk.setIdle(ms)", minutes * 60_000);

    /// <summary>Advances the displayed clock by whole days.</summary>
    public Task AdvanceClockDaysAsync(int days) =>
        Page.EvaluateAsync("n => window.familyHqKiosk.advanceDays(n)", days);

    /// <summary>Forces an immediate idle-snap evaluation. The JS bridge Promise resolves only
    /// after the .NET snap (incl. any month reload) completes; callers then assert with
    /// web-first expectations so the DOM-render flush is awaited, not slept on.</summary>
    public async Task RunIdleCheckAsync()
    {
        await Page.EvaluateAsync("() => window.familyHqKiosk.runIdleCheck()");
    }

    /// <summary>The Day-view centre header text (the day-picker button), e.g. "Tue, 9 Jun".</summary>
    public Task<string> GetDayHeaderTextAsync() => GetDayPickerButtonTextAsync();

    /// <summary>
    /// The Month-view current month-year label text, e.g. "June 2026". Reads the btn-glass
    /// centre button in the month navigation bar (the only .btn-glass rendered in Month view).
    /// Mirrors <see cref="GetAgendaMonthYearTextAsync"/> which reads the equivalent testid on the
    /// Agenda view.
    /// </summary>
    public async Task<string> GetMonthHeaderTextAsync() =>
        (await MonthHeaderButton.InnerTextAsync()).Trim();

    /// <summary>
    /// Returns whether any event capsule for <paramref name="eventName"/> is rendered
    /// in the background colour of <paramref name="calendarName"/>.
    /// </summary>
    public async Task<bool> IsEventDisplayedInCalendarColourAsync(string eventName, string calendarColor)
    {
        var count = await EventCapsules.CountAsync();
        for (int i = 0; i < count; i++)
        {
            var capsule = EventCapsules.Nth(i);
            var text = await capsule.InnerTextAsync();
            if (!text.Contains(eventName)) continue;

            var style = await capsule.GetAttributeAsync("style") ?? "";
            if (style.Contains(calendarColor, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if no event capsule for <paramref name="eventName"/> carries
    /// the background colour associated with <paramref name="calendarName"/>.
    /// </summary>
    public async Task<bool> NoEventCapsuleWithCalendarColourAsync(string eventName, string calendarName, string calendarColor)
    {
        // NOTE: the Count→Nth→InnerText pattern below can hit a 30s auto-wait timeout if a
        // capsule detaches between the count and the per-index read while the grid re-renders
        // after the chip-removal save (the same TOCTOU class documented on GetVisibleEventsAsync).
        // Instrumented so a recurrence reports the grid state rather than a bare timeout.
        try
        {
            var count = await EventCapsules.CountAsync();
            for (int i = 0; i < count; i++)
            {
                var capsule = EventCapsules.Nth(i);
                var text = await capsule.InnerTextAsync();
                if (!text.Contains(eventName)) continue;

                var style = await capsule.GetAttributeAsync("style") ?? "";
                if (style.Contains(calendarColor, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
        catch (TimeoutException ex)
        {
            var state = await CaptureChipScenarioStateAsync();
            throw new TimeoutException(
                $"{ex.Message} [FHQ-29 diagnostic] operation='assert no {calendarName} capsule for {eventName}' " +
                $"| expected-absent-colour={calendarColor} | {state}", ex);
        }
    }

    /// <summary>
    /// Returns true when the only active chip in the event modal has no remove button visible,
    /// which is the "last chip protected" invariant.
    /// </summary>
    public async Task<bool> LastActiveChipHasNoRemoveButtonAsync()
    {
        var activeChips = EventModal.Locator(".chip-active");
        var activeCount = await activeChips.CountAsync();
        if (activeCount != 1) return false;

        var removeBtn = activeChips.First.Locator(".chip-remove");
        return await removeBtn.CountAsync() == 0;
    }

    // Assertions / State Checks
    /// <summary>
    /// Returns titles of all visible event capsules. Does NOT wait for the calendar
    /// to be fully rendered — this makes it safe for use inside polling loops
    /// (e.g. WaitForConditionAsync) where the page may be mid-re-render.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetVisibleEventsAsync()
    {
        // AllInnerTextsAsync captures all texts atomically in one call, avoiding the
        // TOCTOU race where CountAsync returns 1 but the element disappears before
        // InnerTextAsync can read it (causing a 30s auto-wait timeout).
        var texts = await EventCapsules.AllInnerTextsAsync();
        return texts;
    }

    /// <summary>
    /// Returns the background-color hex value declared for <paramref name="calendarName"/>
    /// in the chip selector within the open event modal.
    /// The colour is read from the <c>--chip-color</c> CSS variable on the chip element.
    /// </summary>
    public async Task<string> GetChipColourForCalendarAsync(string calendarName)
    {
        var chip = EventModal.Locator($".chip >> text={calendarName}");
        var style = await chip.GetAttributeAsync("style") ?? "";
        // style is e.g. "--chip-color: #ea4335"
        var idx = style.IndexOf("#", StringComparison.Ordinal);
        if (idx >= 0)
        {
            var raw = style[idx..].Split(';', ' ')[0].Trim();
            return raw;
        }
        return string.Empty;
    }

    /// <summary>Day-view calendar header columns. Exposed for web-first count assertions (FHQ-41).</summary>
    public ILocator CalendarHeaderColumns => Page.Locator(".calendar-header-col");

    public async Task WaitForAllDayEventVisibleAsync(string eventName)
    {
        await WaitForCalendarVisibleAsync();
        var capsule = Page.Locator($".all-day-col .event-capsule:has-text('{eventName}')").First;
        await capsule.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    public async Task WaitForTimedEventVisibleAsync(string eventName)
    {
        await WaitForCalendarVisibleAsync();
        var capsule = Page.Locator($".calendar-col .day-event-block:has-text('{eventName}')").First;
        await capsule.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    public async Task<double> GetTimedEventHeightAsync(string eventName)
    {
        var capsule = Page.Locator($".calendar-col .day-event-block:has-text('{eventName}')").First;
        await capsule.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var style = await capsule.GetAttributeAsync("style") ?? "";
        
        // Extract height e.g., "height: 4.166666666666667%;"
        var match = Regex.Match(style, @"height:\s*([\d.]+)%");
        if (match.Success && double.TryParse(match.Groups[1].Value, out double heightPercentage))
        {
            // The container is 1440px tall, so 1% = 14.4px
            return heightPercentage * 14.4;
        }
        return 0;
    }

    public async Task<double> GetTimedEventWidthPercentageAsync(string eventName)
    {
        var capsule = Page.Locator($".calendar-col .day-event-block:has-text('{eventName}')").First;
        var style = await capsule.GetAttributeAsync("style") ?? "";
        
        var match = Regex.Match(style, @"width:\s*calc\(([\d.]+)%");
        if (match.Success && double.TryParse(match.Groups[1].Value, out double widthPercentage))
        {
            return widthPercentage;
        }
        return 0;
    }

    public async Task<bool> IsCurrentTimeLineVisibleAsync()
    {
        try
        {
            await Assertions.Expect(CurrentTimeLine.First).ToBeVisibleAsync(new() { Timeout = 5000 });
            return true;
        }
        catch (PlaywrightException) { return false; }
    }

    // FHQ-18.11 recurrence helpers ────────────────────────────────────────────

    /// <summary>
    /// Counts the event capsules currently on the grid whose text contains
    /// <paramref name="eventName"/>. Used to assert that a recurring series expanded into
    /// the expected number of instances after sync.
    /// </summary>
    public async Task<int> CountVisibleEventInstancesAsync(string eventName)
    {
        var texts = await EventCapsules.AllInnerTextsAsync();
        return texts.Count(t => t.Contains(eventName));
    }

    /// <summary>
    /// Waits until at least <paramref name="expected"/> capsules for <paramref name="eventName"/>
    /// are rendered, then returns the count. Polls to absorb the render cycle between the sync
    /// HTTP response landing and Blazor painting the instances.
    /// </summary>
    public async Task<int> WaitForEventInstanceCountAsync(string eventName, int expected, int timeoutMs = 30000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var count = 0;
        while (DateTime.UtcNow < deadline)
        {
            count = await CountVisibleEventInstancesAsync(eventName);
            if (count >= expected) return count;
            await Page.WaitForTimeoutAsync(250);
        }
        return count;
    }

    /// <summary>
    /// Asserts a weekly recurring series renders one instance on each of its occurrence dates by
    /// driving the Day view to each date in turn and confirming the named tile is shown there.
    /// Caller must already be on the Day view.
    /// </summary>
    /// <remarks>
    /// FHQ-18.11: this replaces a raw capsule count taken in the Month view. The month grid is a
    /// fixed 6-week window, so a series that starts late in the month pushes its later occurrences
    /// past the visible edge and a "count == N" assertion under-counts purely because of where in
    /// the month the run happens to fall. Visiting each occurrence date individually removes that
    /// windowing dependency entirely: occurrence dates are derived from the seeded first-occurrence
    /// date (<paramref name="firstOccurrenceDate"/> + 7-day steps), each navigation reloads the
    /// owning month's data, and the Day view keys its lookup on the selected date — so the result
    /// is identical regardless of the run date. Dates are formatted with the invariant culture so
    /// the day-picker round-trip is locale-independent.
    /// </remarks>
    public async Task AssertWeeklyOccurrencesEachVisibleInDayViewAsync(
        string eventName, DateTime firstOccurrenceDate, int occurrences)
    {
        for (int i = 0; i < occurrences; i++)
        {
            var occurrenceDate = firstOccurrenceDate.Date.AddDays(7 * i);
            await OpenDayPickerAndGoAsync(
                occurrenceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

            var tile = Page.Locator($".calendar-col .day-event-block:has-text('{eventName}')").First;
            await tile.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        }
    }

    /// <summary>
    /// Waits for the recurrence indicator glyph to be visible on at least one event tile in the
    /// current view. The glyph is shared across Day / Month / Agenda, so this works in any view.
    /// </summary>
    public async Task WaitForRecurrenceIndicatorVisibleAsync(int timeoutMs = 30000)
    {
        await RecurrenceIndicators.First.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
    }

    // FHQ-18.11 Pass 2 — native recurring-event create & toggle-off ────────────
    // The recurrence picker lives inside the open event modal under
    // [data-testid="recurrence-section"]. These helpers drive it via the per-option
    // data-testids added to the mode / frequency / end-mode pills and the weekday
    // toggle row, then compose the full native-create and toggle-off flows.

    private ILocator RecurrenceSection => EventModal.GetByTestId("recurrence-section");

    // FHQ-199: the event modal is tabbed (Details · Repeat). It always opens on Details, and the
    // recurrence picker lives on the Repeat tab — hidden, but still mounted, until that tab is shown.
    // Every helper below that touches the picker calls ShowModalTabAsync("repeat") first, so the
    // feature files did not have to change when the tabs arrived. Save is in the footer and is
    // reachable from either tab.
    private ILocator ModalTab(string tab) => EventModal.GetByTestId($"event-modal-tab-{tab}");

    /// <summary>Shows the named event-modal tab ("details" or "repeat"). Does nothing if it is already showing.</summary>
    public async Task ShowModalTabAsync(string tab)
    {
        var tabButton = ModalTab(tab);
        if (await tabButton.GetAttributeAsync("aria-selected") != "true")
        {
            await tabButton.ClickAsync();
            await Assertions.Expect(tabButton).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 5000 });
        }
    }

    /// <summary>Asserts the named event-modal tab is the one showing.</summary>
    public async Task AssertModalTabActiveAsync(string tab)
        => await Assertions.Expect(ModalTab(tab)).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 5000 });

    /// <summary>Asserts the Repeat tab carries its "unfinished" marker.</summary>
    public async Task AssertRepeatTabIncompleteAsync()
        => await Assertions.Expect(EventModal.GetByTestId("event-modal-tab-repeat-incomplete"))
            .ToBeVisibleAsync(new() { Timeout = 5000 });

    /// <summary>Asserts the footer explains that an unfinished Repeat tab is what disables Save.</summary>
    public async Task AssertSaveHintVisibleAsync()
        => await Assertions.Expect(EventModal.GetByTestId("event-save-hint"))
            .ToBeVisibleAsync(new() { Timeout = 5000 });

    /// <summary>Asserts the Repeat tab's badge reads <paramref name="expected"/> (e.g. "Weekly").</summary>
    public async Task AssertRepeatTabBadgeAsync(string expected)
        => await Assertions.Expect(EventModal.GetByTestId("event-modal-tab-repeat-badge"))
            .ToHaveTextAsync(expected, new() { Timeout = 5000 });

    /// <summary>
    /// Clicks Save without waiting for the modal to close. Used where a validation failure is
    /// expected to keep the modal open — e.g. <see cref="BeginCreatingEventAsync"/> leaves the
    /// title blank, so this exercises the missing-title validation (FHQ-199: that error must stay
    /// visible even while the Repeat tab is showing).
    /// </summary>
    public async Task AttemptSaveAsync() => await SaveEventBtn.ClickAsync();

    /// <summary>Asserts the Details tab carries its "unfinished" marker (FHQ-199 Fix 2: no calendar selected).</summary>
    public async Task AssertDetailsTabIncompleteAsync()
        => await Assertions.Expect(EventModal.GetByTestId("event-modal-tab-details-incomplete"))
            .ToBeVisibleAsync(new() { Timeout = 5000 });

    /// <summary>Asserts the Details tab's "unfinished" marker is gone (a calendar is now selected).</summary>
    public async Task AssertDetailsTabNotIncompleteAsync()
        => await Assertions.Expect(EventModal.GetByTestId("event-modal-tab-details-incomplete"))
            .ToHaveCountAsync(0, new() { Timeout = 5000 });

    /// <summary>
    /// FHQ-199 Fix 1: asserts the modal's bounding box (position AND size) is unchanged, within 1px
    /// for sub-pixel rounding, as the active tab moves from Details to Repeat and back. Each switch
    /// is synchronised on the tab's aria-selected flip inside <see cref="ShowModalTabAsync"/> — no
    /// sleep — before the box is re-measured, so a pass proves the CSS grid-stacking fix, not timing.
    /// </summary>
    public async Task AssertModalStaysStillAcrossTabsAsync()
    {
        var initial = await ModalBoundingBoxAsync();

        await ShowModalTabAsync("repeat");
        await AssertModalBoxMatchesAsync(initial);

        await ShowModalTabAsync("details");
        await AssertModalBoxMatchesAsync(initial);
    }

    private async Task AssertModalBoxMatchesAsync(LocatorBoundingBoxResult expected)
    {
        var actual = await ModalBoundingBoxAsync();
        actual.X.Should().BeApproximately(expected.X, 1f, "the modal must not shift horizontally when the tab changes.");
        actual.Y.Should().BeApproximately(expected.Y, 1f, "the modal must not shift vertically when the tab changes (it would if the dialog resized and re-centred).");
        actual.Width.Should().BeApproximately(expected.Width, 1f, "the modal must not resize when the tab changes.");
        actual.Height.Should().BeApproximately(expected.Height, 1f, "the modal must not resize when the tab changes.");
    }

    private async Task<LocatorBoundingBoxResult> ModalBoundingBoxAsync()
    {
        var box = await EventModal.BoundingBoxAsync();
        box.Should().NotBeNull("the event modal must be visible with a measurable layout box.");
        return box!;
    }

    private ILocator ScopePrompt => Page.GetByTestId("recurrence-scope-prompt");
    private ILocator ScopePromptOkBtn => Page.GetByTestId("recurrence-scope-ok");

    // FHQ-18.11 Pass 5 (§10.1): the inline warning shown in the scope prompt when a member change is
    // pending and a non-All scope is selected. Member changes are only valid for the whole series.
    private ILocator ScopePromptMemberWarning => Page.GetByTestId("recurrence-scope-member-warning");

    // FHQ-18.11 Pass 3: the three scope pills inside the prompt. Scope names map to the testids
    // declared on RecurrenceScopePrompt's PillSegmentGroup options.
    private ILocator ScopePromptPill(string scope) => Page.GetByTestId($"recurrence-scope-{scope}");

    // FHQ-62: the recurrence picker now leads with a single Does-not-repeat ↔ Repeats toggle.
    // The frequency pills (Daily/Weekly/…/Custom) only render once Repeat is toggled on, and the
    // explicit "none" mode pill has been replaced by toggling the pill off.
    private ILocator RepeatToggle => RecurrenceSection.GetByTestId("recurrence-repeat-toggle");

    /// <summary>Toggles Repeat ON if it is not already, so the frequency pills are revealed.</summary>
    private async Task EnsureRepeatOnAsync()
    {
        await ShowModalTabAsync("repeat");
        var pressed = await RepeatToggle.GetAttributeAsync("aria-pressed");
        if (pressed != "true")
        {
            await RepeatToggle.ClickAsync();
            await Assertions.Expect(RepeatToggle).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });
        }
    }

    /// <summary>Toggles Repeat OFF if it is not already (the new "Does not repeat" affordance).</summary>
    private async Task EnsureRepeatOffAsync()
    {
        await ShowModalTabAsync("repeat");
        var pressed = await RepeatToggle.GetAttributeAsync("aria-pressed");
        if (pressed != "false")
        {
            await RepeatToggle.ClickAsync();
            await Assertions.Expect(RepeatToggle).ToHaveAttributeAsync("aria-pressed", "false", new() { Timeout = 5000 });
        }
    }

    /// <summary>
    /// Selects a recurrence frequency pill (e.g. "weekly", "custom"). Ensures Repeat is toggled on
    /// first so the frequency pills are visible.
    /// </summary>
    private async Task SelectRecurrenceModeAsync(string mode)
    {
        await EnsureRepeatOnAsync();
        var pill = RecurrenceSection.GetByTestId($"recurrence-mode-{mode}");
        await pill.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await pill.ClickAsync();
        await Assertions.Expect(pill).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });
    }

    // FHQ-62: the Save button now carries a stable testid and is gated while Repeat is on with no
    // frequency chosen. The toggle-driven scenarios inspect its disabled state directly rather than
    // relying on the visible "Save" label, which the spinner state can momentarily replace.
    private ILocator SaveEventBtnByTestId => EventModal.GetByTestId("event-save-btn");

    /// <summary>Opens the create-event modal and activates the named calendar chip.</summary>
    public async Task BeginCreatingEventAsync(string calendarName)
    {
        await OpenCreateEventModalAsync();
        await EnsureCalendarChipActiveAsync(calendarName);
    }

    /// <summary>
    /// Asserts the recurrence picker opens in the not-repeating state: the toggle reads
    /// "Does not repeat" (aria-pressed=false) and no frequency pills are rendered.
    /// </summary>
    public async Task AssertRecurrenceOffAndNoFrequenciesAsync()
    {
        await ShowModalTabAsync("repeat");
        await Assertions.Expect(RepeatToggle).ToHaveAttributeAsync("aria-pressed", "false", new() { Timeout = 5000 });
        await Assertions.Expect(RepeatToggle).ToContainTextAsync("Does not repeat", new() { Timeout = 5000 });
        await Assertions.Expect(RecurrenceSection.GetByTestId("recurrence-mode-weekly"))
            .ToHaveCountAsync(0, new() { Timeout = 5000 });
    }

    /// <summary>Toggles Repeat on via the does-not-repeat ↔ repeats toggle.</summary>
    public async Task TurnOnRepeatAsync() => await EnsureRepeatOnAsync();

    /// <summary>
    /// Asserts that with Repeat toggled on but no frequency chosen, the toggle reads "Repeats", the
    /// frequency pills are shown with none selected, and Save is disabled.
    /// </summary>
    public async Task AssertRepeatOnNoFrequencySelectedAndSaveDisabledAsync()
    {
        await ShowModalTabAsync("repeat");
        await Assertions.Expect(RepeatToggle).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });
        await Assertions.Expect(RepeatToggle).ToContainTextAsync("Repeats", new() { Timeout = 5000 });

        // The frequency pills are revealed but none is highlighted (no .pill-toggle--on among them).
        await Assertions.Expect(RecurrenceSection.GetByTestId("recurrence-mode-weekly"))
            .ToBeVisibleAsync(new() { Timeout = 5000 });
        await Assertions.Expect(RecurrenceSection.Locator(".pill-segment-group .pill-toggle--on"))
            .ToHaveCountAsync(0, new() { Timeout = 5000 });

        await Assertions.Expect(SaveEventBtnByTestId).ToBeDisabledAsync(new() { Timeout = 5000 });
    }

    /// <summary>Chooses the weekly repeat frequency pill (Repeat must already be on).</summary>
    public async Task ChooseWeeklyFrequencyAsync() => await SelectRecurrenceModeAsync("weekly");

    /// <summary>Asserts the Save button is enabled.</summary>
    public async Task AssertSaveEnabledAsync()
        => await Assertions.Expect(SaveEventBtnByTestId).ToBeEnabledAsync(new() { Timeout = 5000 });

    // FHQ-62: stepper pills around the Custom-drawer interval input.
    private ILocator IntervalInput => RecurrenceSection.GetByTestId("recurrence-interval");
    private ILocator IntervalIncrement => RecurrenceSection.GetByTestId("recurrence-interval-increment");
    private ILocator IntervalDecrement => RecurrenceSection.GetByTestId("recurrence-interval-decrement");

    /// <summary>
    /// Opens the create-event modal on the named calendar, turns Repeat on, and opens the Custom
    /// recurrence drawer — the precondition for exercising the interval stepper pills.
    /// </summary>
    public async Task BeginCreatingCustomRepeatingEventAsync(string calendarName)
    {
        await BeginCreatingEventAsync(calendarName);
        await SelectRecurrenceModeAsync("custom");
    }

    /// <summary>Asserts the Custom drawer interval input holds the given value.</summary>
    public async Task AssertIntervalValueAsync(string expected)
    {
        await ShowModalTabAsync("repeat");
        await Assertions.Expect(IntervalInput).ToHaveValueAsync(expected, new() { Timeout = 5000 });
    }

    /// <summary>
    /// Asserts the Custom drawer interval input holds <paramref name="expected"/> (the floor, "1")
    /// with its decrement stepper disabled, so the interval cannot be lowered further.
    /// </summary>
    public async Task AssertIntervalAtFloorWithDecrementDisabledAsync(string expected)
    {
        await ShowModalTabAsync("repeat");
        await Assertions.Expect(IntervalInput).ToHaveValueAsync(expected, new() { Timeout = 5000 });
        await Assertions.Expect(IntervalDecrement).ToBeDisabledAsync(new() { Timeout = 5000 });
    }

    /// <summary>Taps the interval increment stepper and asserts the input rises to the given value.</summary>
    public async Task IncrementIntervalAndAssertValueAsync(string expected)
    {
        await ShowModalTabAsync("repeat");
        await IntervalIncrement.ClickAsync();
        await Assertions.Expect(IntervalInput).ToHaveValueAsync(expected, new() { Timeout = 5000 });
    }

    /// <summary>Taps the interval decrement stepper and asserts the input falls to the given value.</summary>
    public async Task DecrementIntervalAndAssertValueAsync(string expected)
    {
        await ShowModalTabAsync("repeat");
        await IntervalDecrement.ClickAsync();
        await Assertions.Expect(IntervalInput).ToHaveValueAsync(expected, new() { Timeout = 5000 });
    }

    /// <summary>
    /// Opens the occurrence tile of <paramref name="seriesName"/> on <paramref name="occurrenceDate"/>
    /// for editing (Day view), leaving the event modal open for inspection.
    /// </summary>
    public async Task OpenOccurrenceForEditingAsync(string seriesName, DateTime occurrenceDate)
    {
        await OpenDayPickerAndGoAsync(
            occurrenceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        var tile = Page.Locator($".calendar-col .day-event-block:has-text('{seriesName}')").First;
        await tile.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await tile.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    /// <summary>
    /// Asserts the open event modal shows the picker as repeating with the weekly frequency pill
    /// selected: toggle reads "Repeats" and the weekly pill is pressed.
    /// </summary>
    public async Task AssertRepeatingWeeklySelectedAsync()
    {
        await ShowModalTabAsync("repeat");
        await Assertions.Expect(RepeatToggle).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });
        await Assertions.Expect(RepeatToggle).ToContainTextAsync("Repeats", new() { Timeout = 5000 });
        await Assertions.Expect(RecurrenceSection.GetByTestId("recurrence-mode-weekly"))
            .ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });
    }

    /// <summary>Selects a custom-drawer frequency pill (e.g. "weekly"). Requires Custom mode.</summary>
    private async Task SelectRecurrenceFrequencyAsync(string frequency)
    {
        await ShowModalTabAsync("repeat");
        var pill = RecurrenceSection.GetByTestId($"recurrence-frequency-{frequency}");
        await pill.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await pill.ClickAsync();
        await Assertions.Expect(pill).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });
    }

    /// <summary>Toggles a weekday button on in the custom weekly drawer (DayOfWeek name, e.g. "Tuesday").</summary>
    private async Task ToggleRecurrenceWeekdayAsync(string dayOfWeekName)
    {
        await ShowModalTabAsync("repeat");
        var toggle = RecurrenceSection.GetByTestId($"recurrence-weekday-{dayOfWeekName}");
        await toggle.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });
    }

    /// <summary>
    /// Activates the named calendar chip in the open modal if it is not already active. Public so a
    /// scenario can select a calendar without also saving (FHQ-199: clearing the Details tab's
    /// "no calendar selected" marker) as well as via the composed create/edit flows below.
    /// </summary>
    /// <remarks>
    /// FHQ-199: the chip selector lives on the Details pane, so this shows that tab first — mirrors
    /// <see cref="EnsureRepeatOnAsync"/> calling <c>ShowModalTabAsync("repeat")</c> first, so callers
    /// do not need a tab-switch step of their own regardless of which tab is currently showing. A
    /// freshly-opened modal is already on Details, so this is a no-op for the create/edit flows below;
    /// it only does work for a caller that selects a calendar while the Repeat tab is active. The
    /// inactive Details pane is `visibility: hidden` (Fix 1), so the chip genuinely cannot be clicked
    /// without switching first — Playwright times out waiting for it to become visible.
    /// </remarks>
    public async Task EnsureCalendarChipActiveAsync(string calendarName)
    {
        await ShowModalTabAsync("details");

        var chip = EventModal.Locator(".chip").Filter(new() { HasText = calendarName });
        var classes = await chip.GetAttributeAsync("class") ?? "";
        if (!classes.Contains("chip-active"))
        {
            await chip.ClickAsync();
            await Assertions.Expect(chip).ToHaveClassAsync(new Regex("chip-active"), new() { Timeout = 5000 });
        }
    }

    /// <summary>
    /// Creates a weekly recurring event natively: opens the create modal, picks the named calendar,
    /// sets the recurrence mode to Weekly (which repeats on the start date's weekday), and saves.
    /// Waits for the reconcile events response and the calendar to repaint.
    /// </summary>
    public async Task CreateWeeklyRecurringEventAsync(string title, string calendarName)
    {
        await OpenCreateEventModalAsync();
        await EventTitleInput.FillAsync(title);
        await EnsureCalendarChipActiveAsync(calendarName);
        await SelectRecurrenceModeAsync("weekly");

        // Await the create itself (POST api/events), NOT the dashboard's GET api/calendars/events poll.
        // Asserting the create's status makes a server-side failure (e.g. the FHQ-66 recurring-create
        // reconcile race that returns a 500) surface as a clear failure here, rather than as an opaque
        // 30s "modal never closed" timeout.
        var createResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/events") && r.Request.Method == "POST",
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await AssertCreateSucceededAsync(createResponseTask);
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await WaitForCalendarVisibleAsync();
        await WaitForSyncSettledAsync();
    }

    /// <summary>
    /// Awaits the create POST and fails loudly on a non-success status. A failed create leaves the
    /// modal open, so without this the only symptom would be a 30s modal-hidden timeout that hides the
    /// real cause (the FHQ-66 reconcile-race 500).
    /// </summary>
    private static async Task AssertCreateSucceededAsync(Task<IResponse> createResponseTask)
    {
        var createResponse = await createResponseTask;
        if (createResponse.Status >= 400)
            throw new PlaywrightException(
                $"Recurring-event create POST returned HTTP {createResponse.Status}; the modal will not " +
                "close on an error. A duplicate-key 500 here is the FHQ-66 reconcile-race signature.");
    }

    /// <summary>
    /// Creates a recurring event repeating weekly on a specific weekday: opens the create modal,
    /// picks the named calendar, switches to the Custom drawer, selects Weekly frequency and the
    /// given weekday(s) (DayOfWeek names), and saves.
    /// </summary>
    public async Task CreateCustomWeeklyRecurringEventAsync(
        string title, string calendarName, IReadOnlyList<string> weekdayNames)
    {
        await OpenCreateEventModalAsync();
        await EventTitleInput.FillAsync(title);
        await EnsureCalendarChipActiveAsync(calendarName);
        await SelectRecurrenceModeAsync("custom");
        await SelectRecurrenceFrequencyAsync("weekly");
        foreach (var weekday in weekdayNames)
        {
            await ToggleRecurrenceWeekdayAsync(weekday);
        }

        var createResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/events") && r.Request.Method == "POST",
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await AssertCreateSucceededAsync(createResponseTask);
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await WaitForCalendarVisibleAsync();
        await WaitForSyncSettledAsync();
    }

    /// <summary>
    /// Opens an existing recurring event for editing, sets recurrence to "Does not repeat", saves,
    /// and confirms the recurrence-scope prompt (defaulting to "All events") so the series collapses
    /// to a single non-recurring event. Waits for the reconcile response and repaint.
    /// </summary>
    public async Task TurnOffRecurrenceForEventAsync(string eventName)
    {
        await OpenEventForEditingAsync(eventName);
        await EnsureRepeatOffAsync();

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();

        // Collapsing a series prompts for scope; the prompt defaults to "All events" (the only
        // valid scope for a clear), so confirming with OK drives the toggle-OFF patch.
        await ScopePrompt.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await ScopePromptOkBtn.ClickAsync();

        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
        await WaitForSyncSettledAsync();
    }

    /// <summary>
    /// Navigates the Day view to <paramref name="date"/> and asserts the named event appears there
    /// as a single, non-recurring occurrence: exactly one tile and no recurrence indicator. Used to
    /// prove a toggled-OFF series has collapsed to one event. Caller must already be on the Day view.
    /// </summary>
    public async Task AssertSingleNonRecurringOccurrenceInDayViewAsync(string eventName, DateTime date)
    {
        await OpenDayPickerAndGoAsync(
            date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        var tiles = Page.Locator($".calendar-col .day-event-block:has-text('{eventName}')");
        await tiles.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await Assertions.Expect(tiles).ToHaveCountAsync(1, new() { Timeout = 10000 });
        await Assertions.Expect(RecurrenceIndicators).ToHaveCountAsync(0, new() { Timeout = 10000 });
    }

    /// <summary>
    /// Counts the day-view tiles bearing <paramref name="eventName"/> on <paramref name="date"/>.
    /// Navigates the Day view to that date first. Caller must already be on the Day view.
    /// </summary>
    public async Task<int> CountDayViewOccurrencesOnDateAsync(string eventName, DateTime date)
    {
        await OpenDayPickerAndGoAsync(
            date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        var tiles = Page.Locator($".calendar-col .day-event-block:has-text('{eventName}')");
        await WaitForCalendarVisibleAsync();
        return await tiles.CountAsync();
    }

    /// <summary>
    /// Asserts a weekly recurring series occupies EXACTLY its expected occurrence slots: on each of
    /// <paramref name="occurrences"/> consecutive weekly dates (from <paramref name="firstOccurrenceDate"/>)
    /// there is exactly one tile bearing <paramref name="eventName"/>, and every occurrence tile shows
    /// identical content (same title AND same start time). This catches a series that was relocated to
    /// a later occurrence's date — an expected slot would be empty (count 0) — a series that lost or
    /// gained occurrences, and occurrences whose time-of-day drifted apart. The day-event tile renders
    /// the start time (<c>h:mm tt</c>) beneath the title, so identical tile text means a uniform time.
    /// </summary>
    public async Task AssertWeeklySeriesOccupiesExactlyItsSlotsAsync(
        string eventName, DateTime firstOccurrenceDate, int occurrences)
    {
        string? firstTileText = null;
        for (var i = 0; i < occurrences; i++)
        {
            var date = firstOccurrenceDate.AddDays(7 * i);
            await OpenDayPickerAndGoAsync(
                date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

            var tiles = Page.Locator($".calendar-col .day-event-block:has-text('{eventName}')");
            await tiles.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
            await Assertions.Expect(tiles).ToHaveCountAsync(1, new() { Timeout = 10000 });

            // The tile renders "<title>\n<h:mm tt>"; identical text across occurrences means the whole
            // series shares one title and one start time.
            var tileText = (await tiles.First.InnerTextAsync()).Trim();
            if (firstTileText is null)
                firstTileText = tileText;
            else if (tileText != firstTileText)
                throw new System.Exception(
                    $"Occurrence {i + 1} of '{eventName}' shows \"{tileText}\" but the first occurrence shows " +
                    $"\"{firstTileText}\" — every occurrence of a series must share the same title and start time.");
        }
    }

    // FHQ-18.11 Pass 3 — edit-scope flow (This event / This and following / All events) ──────────

    /// <summary>
    /// Drives the recurrence-scope prompt that appears after Save when editing a recurring series:
    /// waits for the prompt to be visible, selects the named scope pill (waiting on its
    /// <c>aria-pressed=true</c>), waits for OK to be visible, confirms, and waits for the modal to
    /// close and the calendar to reconcile + repaint.
    /// </summary>
    /// <remarks>
    /// FHQ-29 click-race: the prompt is a Save→pill→OK flow, so each interactive element is
    /// explicitly awaited Visible (and the pill's pressed state confirmed) before the next click —
    /// never click an element that has not been observed ready. <paramref name="scope"/> is one of
    /// "this", "following", "all" (the recurrence-scope-* testid suffixes).
    /// </remarks>
    public async Task SubmitEditWithScopeAsync(string scope)
    {
        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();

        // Wait for the prompt itself before touching any pill (FHQ-29 visibility wait).
        await ScopePrompt.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        var pill = ScopePromptPill(scope);
        await pill.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await pill.ClickAsync();
        await Assertions.Expect(pill).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });

        // Confirm only once OK is observed visible (FHQ-29 visibility wait on the pill→OK leg).
        await ScopePromptOkBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await ScopePromptOkBtn.ClickAsync();

        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
        await WaitForSyncSettledAsync();
    }

    /// <summary>
    /// Opens the named recurring event on the Day view for <paramref name="occurrenceDate"/>, sets a
    /// new title, and submits with the given scope ("this" / "following" / "all"). Navigates the Day
    /// view to the occurrence date first so the clicked tile is the intended occurrence.
    /// </summary>
    public async Task EditRecurringOccurrenceTitleWithScopeAsync(
        string occurrenceName, DateTime occurrenceDate, string newTitle, string scope)
    {
        await OpenDayPickerAndGoAsync(
            occurrenceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        var tile = Page.Locator($".calendar-col .day-event-block:has-text('{occurrenceName}')").First;
        await tile.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await tile.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await EventTitleInput.FillAsync(newTitle);
        await SubmitEditWithScopeAsync(scope);
    }

    /// <summary>
    /// Navigates the Day view to <paramref name="date"/> and asserts a tile bearing
    /// <paramref name="eventName"/> is visible there. Used to prove an edited occurrence shows the
    /// change (or that an untouched occurrence still shows the original title).
    /// </summary>
    public async Task AssertEventVisibleInDayViewOnDateAsync(string eventName, DateTime date)
    {
        await OpenDayPickerAndGoAsync(
            date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        var tile = Page.Locator($".calendar-col .day-event-block:has-text('{eventName}')").First;
        await tile.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    // FHQ-18.11 Pass 4 — delete-scope flow (This event / This and following / All events) ──────────

    /// <summary>
    /// Drives the recurrence-scope prompt that appears after the trash-can delete when removing a
    /// recurring series: waits for the prompt to be visible, selects the named scope pill (waiting on
    /// its <c>aria-pressed=true</c>), waits for OK to be visible, confirms, and waits for the modal to
    /// close and the calendar to reconcile + repaint.
    /// </summary>
    /// <remarks>
    /// The delete prompt is the same <c>recurrence-scope-prompt</c> component as the edit prompt (the
    /// delete variant carries the "Delete recurring event" header). FHQ-29 click-race: each interactive
    /// element is explicitly awaited Visible — and the pill's pressed state confirmed — before the next
    /// click; never click an element that has not been observed ready. <paramref name="scope"/> is one
    /// of "this" / "following" / "all" (the recurrence-scope-* testid suffixes).
    /// </remarks>
    public async Task SubmitDeleteWithScopeAsync(string scope)
    {
        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await DeleteEventBtn.ClickAsync();

        // Wait for the prompt itself before touching any pill (FHQ-29 visibility wait).
        await ScopePrompt.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        var pill = ScopePromptPill(scope);
        await pill.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await pill.ClickAsync();
        await Assertions.Expect(pill).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });

        // Confirm only once OK is observed visible (FHQ-29 visibility wait on the pill→OK leg).
        await ScopePromptOkBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await ScopePromptOkBtn.ClickAsync();

        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
        await WaitForSyncSettledAsync();
    }

    /// <summary>
    /// Opens the named recurring event on the Day view for <paramref name="occurrenceDate"/> and
    /// deletes it with the given scope ("this" / "following" / "all"). Navigates the Day view to the
    /// occurrence date first so the clicked tile is the intended occurrence.
    /// </summary>
    public async Task DeleteRecurringOccurrenceWithScopeAsync(
        string occurrenceName, DateTime occurrenceDate, string scope)
    {
        await OpenDayPickerAndGoAsync(
            occurrenceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        var tile = Page.Locator($".calendar-col .day-event-block:has-text('{occurrenceName}')").First;
        await tile.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await tile.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await SubmitDeleteWithScopeAsync(scope);
    }

    /// <summary>
    /// Navigates the Day view to <paramref name="date"/> and asserts NO tile bearing
    /// <paramref name="eventName"/> is present there. Used to prove a deleted occurrence (or the
    /// post-split tail) no longer appears.
    /// </summary>
    public async Task AssertEventAbsentInDayViewOnDateAsync(string eventName, DateTime date)
    {
        await OpenDayPickerAndGoAsync(
            date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        var tiles = Page.Locator($".calendar-col .day-event-block:has-text('{eventName}')");
        await Assertions.Expect(tiles).ToHaveCountAsync(0, new() { Timeout = 30000 });
    }

    // FHQ-18.11 Pass 5 — preservation: members tag (§10.1) and echo guard (§10.2) ──────────────────

    /// <summary>
    /// Navigates the Day view to <paramref name="date"/> and asserts a timed tile bearing
    /// <paramref name="eventName"/> is present there in <paramref name="calendarColour"/> (the
    /// background-colour of one of the event's member calendars). A multi-member event fans out to
    /// one tile per member column, each painted in that member's calendar colour, so calling this for
    /// each member colour proves the synced/edited occurrence is linked to every member. Day-view
    /// per-date navigation is used deliberately so the assertion never depends on the windowed month
    /// grid (FHQ-18.11 learning: never count occurrences in the 6-week month grid).
    /// </summary>
    public async Task AssertEventInColourOnDateInDayViewAsync(string eventName, DateTime date, string calendarColour)
    {
        await OpenDayPickerAndGoAsync(
            date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        // A timed multi-member instance renders as .day-event-block tiles, one per member column,
        // each carrying its calendar colour in the inline background-color style.
        var tiles = Page.Locator($".calendar-col .day-event-block:has-text('{eventName}')");
        await tiles.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        // The specific member-colour tile can render a beat after the first tile, as the member-linkage
        // sync lands and the day view re-renders (async sync + EventsUpdated; TOCTOU, intermittent-issues #6).
        // Poll for the colour rather than reading once.
        var deadline = System.DateTime.UtcNow.AddSeconds(15);
        while (System.DateTime.UtcNow < deadline)
        {
            var count = await tiles.CountAsync();
            for (var i = 0; i < count; i++)
            {
                var style = await tiles.Nth(i).GetAttributeAsync("style") ?? string.Empty;
                if (style.Contains(calendarColour, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            await Task.Delay(250);
        }

        throw new InvalidOperationException(
            $"No '{eventName}' day-view tile painted in colour '{calendarColour}' was found on " +
            $"{date:yyyy-MM-dd}. The occurrence is not linked to that member calendar.");
    }

    /// <summary>
    /// Opens the recurring occurrence named <paramref name="occurrenceName"/> on the Day view for
    /// <paramref name="occurrenceDate"/>, activates the <paramref name="memberCalendarName"/> chip
    /// (adding that calendar as a member), saves, and confirms the recurrence-scope prompt at the
    /// "all" scope — the only scope where a member change is permitted (§10.1). Drives the same
    /// FHQ-29-safe Save→pill→OK flow as <see cref="SubmitEditWithScopeAsync"/>.
    /// </summary>
    public async Task AddMemberToRecurringOccurrenceAllScopeAsync(
        string occurrenceName, DateTime occurrenceDate, string memberCalendarName)
    {
        await OpenDayPickerAndGoAsync(
            occurrenceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        var tile = Page.Locator($".calendar-col .day-event-block:has-text('{occurrenceName}')").First;
        await tile.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await tile.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await EnsureCalendarChipActiveAsync(memberCalendarName);
        await SubmitEditWithScopeAsync("all");
    }

    /// <summary>
    /// Opens the recurring occurrence named <paramref name="occurrenceName"/> on the Day view for
    /// <paramref name="occurrenceDate"/>, activates the <paramref name="memberCalendarName"/> chip
    /// (a pending member change), clicks Save to surface the scope prompt, selects the "This event"
    /// scope, and reports whether the change is blocked: returns true when the member-change warning
    /// is shown AND the OK button is disabled. Proves a member change is refused at non-All scope
    /// (§10.1). Does NOT confirm — the prompt is left open and is dismissed by the caller / teardown.
    /// </summary>
    public async Task<bool> IsMemberChangeBlockedAtThisEventScopeAsync(
        string occurrenceName, DateTime occurrenceDate, string memberCalendarName)
    {
        await OpenDayPickerAndGoAsync(
            occurrenceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        var tile = Page.Locator($".calendar-col .day-event-block:has-text('{occurrenceName}')").First;
        await tile.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await tile.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await EnsureCalendarChipActiveAsync(memberCalendarName);

        await SaveEventBtn.ClickAsync();
        await ScopePrompt.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        // Select "This event" — a non-All scope where the pending member change must be refused.
        var pill = ScopePromptPill("this");
        await pill.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await pill.ClickAsync();
        await Assertions.Expect(pill).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });

        // Observable block: the member warning is shown and OK is disabled. Web-first assertions
        // auto-retry against the live DOM rather than reading visibility/disabled state once and
        // racing the scope-prompt re-render (FHQ-41); a web-first failure throws PlaywrightException,
        // mapped back to false to preserve the bool contract.
        try
        {
            await Assertions.Expect(ScopePromptMemberWarning).ToBeVisibleAsync(new() { Timeout = 10000 });
            await Assertions.Expect(ScopePromptOkBtn).ToBeDisabledAsync(new() { Timeout = 10000 });
            return true;
        }
        catch (PlaywrightException) { return false; }
    }

    // ── The Reminders tab ────────────────────────────────────────────────────
    // Every reminder row carries its own method and offset as attributes, so each helper below
    // addresses a row BY VALUE. Addressing one by index would be wrong rather than merely brittle:
    // Google returns the overrides in an order of its own and the picker re-sorts them, so the row
    // at position 1 is not the reminder the scenario is talking about.

    /// <summary>Google's delivery method for an on-screen notification.</summary>
    private const string PopupMethod = "popup";

    private ILocator RemindersSection => EventModal.GetByTestId("reminders-section");
    private ILocator RemindersBadge => EventModal.GetByTestId("event-modal-tab-reminders-badge");
    private ILocator ReminderUseDefaultToggle => RemindersSection.GetByTestId("reminder-use-default-toggle");
    private ILocator ReminderRows => RemindersSection.GetByTestId("reminder-item");
    private ILocator ReminderEmptyState => RemindersSection.GetByTestId("reminder-empty-state");
    private ILocator ReminderAllDayResetNotice => RemindersSection.GetByTestId("reminder-all-day-reset-notice");
    private ILocator ReminderAmountInput => RemindersSection.GetByTestId("reminder-amount");
    private ILocator ReminderDaysBeforeInput => RemindersSection.GetByTestId("reminder-days-before");
    private ILocator ReminderDaysDecrement => RemindersSection.GetByTestId("reminder-days-decrement");
    private ILocator ReminderAddBtn => RemindersSection.GetByTestId("reminder-add-btn");

    // Scoped INSIDE the Add button on purpose. The form holds a value from the moment the tab opens,
    // so the reminder it describes is nobody's choice until Add is pressed; this locator resolving
    // only within the button is itself half of what AssertAddControlOffersAsync asserts.
    private ILocator ReminderAddPreview => ReminderAddBtn.GetByTestId("reminder-add-preview");
    private ILocator ReminderUnitPill(string unit) => RemindersSection.GetByTestId($"reminder-unit-{unit}");
    private ILocator ReminderMethodPill(string method) => RemindersSection.GetByTestId($"reminder-method-{method}");
    private ILocator AllDayToggle => EventModal.GetByTestId("all-day-toggle");

    // The inline warning the scope prompt shows when a reminder change is about to be applied to a
    // whole series, which replaces every occurrence's reminders — including an occurrence whose own
    // were set separately. Google does the same; the prompt says so first.
    private ILocator ScopePromptReminderWarning => Page.GetByTestId("recurrence-scope-reminder-warning");

    // The two things the inheriting panel can say when it has no list to show. They are addressed by
    // testid rather than by their copy because which of the two appears is the assertion: one says
    // the calendar has no usual reminders, the other that they could not be read. Telling a family
    // the first when the second is true is how an event's real reminders get saved away.
    private ILocator ReminderDefaultNone => RemindersSection.GetByTestId("reminder-default-none");
    private ILocator ReminderDefaultUnavailable =>
        RemindersSection.GetByTestId("reminder-default-unavailable");

    private ILocator ReminderRow(string method, int minutes) => RemindersSection.Locator(
        $"[data-testid='reminder-item'][data-reminder-method='{method}'][data-reminder-minutes='{minutes}']");

    private ILocator CalendarDefaultRow(string method, int minutes) => RemindersSection.Locator(
        $"[data-testid='reminder-default-item'][data-reminder-method='{method}'][data-reminder-minutes='{minutes}']");

    /// <summary>Shows the Reminders tab of the open event modal.</summary>
    public Task ShowRemindersTabAsync() => ShowModalTabAsync("reminders");

    /// <summary>
    /// Switches the "use this calendar's usual reminders" toggle to <paramref name="follow"/>.
    /// Idempotent: does nothing when it is already there.
    /// </summary>
    /// <remarks>
    /// Switching it OFF copies the calendar's usual reminders in as editable entries, as the Google
    /// Calendar app pre-fills them — so an event that stops inheriting does not silently land on "no
    /// reminders", which Google treats as a different thing.
    /// </remarks>
    public async Task SetReminderInheritanceAsync(bool follow)
    {
        await ShowRemindersTabAsync();

        var expected = follow ? "true" : "false";
        if (await ReminderUseDefaultToggle.GetAttributeAsync("aria-pressed") == expected)
        {
            return;
        }

        await ReminderUseDefaultToggle.ClickAsync();
        await Assertions.Expect(ReminderUseDefaultToggle)
            .ToHaveAttributeAsync("aria-pressed", expected, new() { Timeout = 5000 });
    }

    /// <summary>
    /// Adds a reminder of the event's own, <paramref name="amount"/> <paramref name="unit"/> before it
    /// starts, delivered by <paramref name="method"/>. The event must already have stopped following
    /// the calendar's usual reminders — the Add form is not offered while it inherits, because Google
    /// refuses a write that asks for the defaults and for specific reminders at once.
    /// </summary>
    public async Task AddTimedReminderAsync(int amount, string unit, string method = PopupMethod)
    {
        await ConfigureTimedReminderAsync(amount, unit, method);

        await ReminderAddBtn.ClickAsync();
        await Assertions.Expect(ReminderRow(method, amount * UnitMinutes(unit)))
            .ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    /// <summary>
    /// Moves the Add form's controls to describe a reminder <paramref name="amount"/>
    /// <paramref name="unit"/> before the event starts, delivered by <paramref name="method"/>, and
    /// stops there — Add is never pressed, so nothing is committed to the list.
    /// </summary>
    /// <remarks>
    /// This is the state a family reported a reminder going missing from: the form describes a
    /// reminder they configured, the list above it is empty, and Save was the next thing they
    /// touched. Reused by <see cref="AddTimedReminderAsync"/> so the two cannot drift — a scenario
    /// about forgetting Add has to drive the same controls as one that presses it.
    /// </remarks>
    public async Task ConfigureTimedReminderAsync(int amount, string unit, string method = PopupMethod)
    {
        await ShowRemindersTabAsync();

        await SelectPillAsync(ReminderUnitPill(unit));
        await SelectPillAsync(ReminderMethodPill(method));

        // The number field commits on the DOM's change event, which a fill alone does not raise — the
        // blur does. Same as the recurrence interval.
        var typed = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await ReminderAmountInput.FillAsync(typed);
        await ReminderAmountInput.PressAsync("Tab");
        await Assertions.Expect(ReminderAmountInput).ToHaveValueAsync(typed, new() { Timeout = 5000 });
    }

    /// <summary>Removes the event's reminder with the given method and offset.</summary>
    public async Task RemoveReminderAsync(int minutes, string method = PopupMethod)
    {
        await ShowRemindersTabAsync();

        var row = ReminderRow(method, minutes);
        await row.GetByTestId("reminder-remove").ClickAsync();
        await Assertions.Expect(row).ToHaveCountAsync(0, new() { Timeout = 5000 });
    }

    /// <summary>
    /// Removes every reminder the event carries of its own, leaving the state Google records as
    /// "replace the calendar's usual reminders with nothing".
    /// </summary>
    public async Task RemoveEveryReminderAsync()
    {
        await ShowRemindersTabAsync();

        // Always remove the first remaining row and then wait for the count to drop, so the next
        // click cannot land on an element the re-render has already replaced.
        for (var remaining = await ReminderRows.CountAsync(); remaining > 0; remaining--)
        {
            await ReminderRows.First.GetByTestId("reminder-remove").ClickAsync();
            await Assertions.Expect(ReminderRows).ToHaveCountAsync(remaining - 1, new() { Timeout = 5000 });
        }
    }

    /// <summary>Asserts the event carries a reminder with the given method and offset.</summary>
    public Task AssertReminderPresentAsync(int minutes, string method = PopupMethod) =>
        Assertions.Expect(ReminderRow(method, minutes)).ToBeVisibleAsync(new() { Timeout = 5000 });

    /// <summary>Asserts how many reminders of its own the event carries.</summary>
    public Task AssertReminderCountAsync(int expected) =>
        Assertions.Expect(ReminderRows).ToHaveCountAsync(expected, new() { Timeout = 5000 });

    /// <summary>Asserts the Reminders tab's badge (a count, "default", "none" or "none here").</summary>
    public Task AssertRemindersBadgeAsync(string expected) =>
        Assertions.Expect(RemindersBadge).ToHaveTextAsync(expected, new() { Timeout = 5000 });

    /// <summary>
    /// Asserts the event follows its calendar's usual reminders, and that the tab shows what those
    /// actually are rather than an empty list.
    /// </summary>
    public async Task AssertFollowsCalendarDefaultAsync(int minutes, string method = PopupMethod)
    {
        await ShowRemindersTabAsync();
        await Assertions.Expect(ReminderUseDefaultToggle)
            .ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });
        await Assertions.Expect(CalendarDefaultRow(method, minutes)).ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    /// <summary>
    /// Asserts the event follows its calendar's usual reminders and that the tab states the calendar
    /// has none of its own — so following them notifies nobody.
    /// </summary>
    /// <remarks>
    /// The second assertion is the half that cannot be dropped. Both messages render in the same
    /// place for the same reason (there is no list to show), so asserting only that the "none"
    /// message is present would pass again the moment the two states are collapsed back into one.
    /// </remarks>
    public async Task AssertCalendarHasNoUsualRemindersStatedAsync()
    {
        await ShowRemindersTabAsync();
        await Assertions.Expect(ReminderUseDefaultToggle)
            .ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });
        await Assertions.Expect(ReminderDefaultNone).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Assertions.Expect(ReminderDefaultUnavailable).ToHaveCountAsync(0, new() { Timeout = 5000 });
    }

    /// <summary>
    /// Asserts the event follows its calendar's usual reminders and that the tab states they could
    /// not be read — Google still applies them, so the tab must not claim the event has none.
    /// </summary>
    /// <remarks>The mirror of <see cref="AssertCalendarHasNoUsualRemindersStatedAsync"/>.</remarks>
    public async Task AssertCalendarUsualRemindersUnreadableStatedAsync()
    {
        await ShowRemindersTabAsync();
        await Assertions.Expect(ReminderUseDefaultToggle)
            .ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });
        await Assertions.Expect(ReminderDefaultUnavailable).ToBeVisibleAsync(new() { Timeout = 5000 });
        await Assertions.Expect(ReminderDefaultNone).ToHaveCountAsync(0, new() { Timeout = 5000 });
    }

    /// <summary>
    /// Asserts the tab states that a TIMED event has no reminders at all. Deliberately timed-only:
    /// Google stores a reminder set for the day itself of an all-day event and never returns it, so no
    /// assertion may claim an all-day event has none.
    /// </summary>
    public async Task AssertNoRemindersStatedAsync()
    {
        await ShowRemindersTabAsync();
        await AssertReminderCountAsync(0);
        await Assertions.Expect(ReminderEmptyState).ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    /// <summary>
    /// Asserts the add control reads as an action: the reminder the form currently describes is part
    /// of the Add button's own label, prefixed by "Add", and not a line of prose beside it.
    /// </summary>
    /// <remarks>
    /// The distinction is the whole assertion. The form describes a reminder before anyone has
    /// touched it, so on a line of its own the description stated that the event had that reminder —
    /// immediately under the note saying it had none. Inside the button the same words can only be
    /// read as what pressing it would do.
    /// </remarks>
    public async Task AssertAddControlOffersAsync(string timing, string method)
    {
        await ShowRemindersTabAsync();

        await Assertions.Expect(ReminderAddPreview).ToContainTextAsync(timing, new() { Timeout = 5000 });
        await Assertions.Expect(ReminderAddPreview).ToContainTextAsync(method, new() { Timeout = 5000 });
        await Assertions.Expect(ReminderAddBtn).ToContainTextAsync("Add", new() { Timeout = 5000 });
    }

    /// <summary>
    /// Asserts the tab says the reminders were started again, which switching All day does — Google
    /// discards them rather than converting them, and the screen has to say so.
    /// </summary>
    public async Task AssertRemindersResetNoticeVisibleAsync()
    {
        await ShowRemindersTabAsync();
        await Assertions.Expect(ReminderAllDayResetNotice).ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    /// <summary>
    /// Types <paramref name="days"/> into the all-day reminder form's days-before field, blurring so
    /// the field commits — a fill alone does not raise the DOM change event the field listens for.
    /// </summary>
    public async Task AskForReminderDaysBeforeAsync(int days)
    {
        await ShowRemindersTabAsync();

        await ReminderDaysBeforeInput.FillAsync(days.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await ReminderDaysBeforeInput.PressAsync("Tab");
    }

    /// <summary>
    /// Asserts the all-day reminder form holds at <paramref name="days"/> days before with no way
    /// down, so a reminder on the day of the event itself is unreachable.
    /// </summary>
    /// <remarks>
    /// The floor is arithmetic, not validation. "0 days before at 09:00" is a negative offset, and
    /// Google does not reject one — it clamps it to zero and notifies the family at midnight.
    /// </remarks>
    public async Task AssertReminderDaysBeforeHoldsAtAsync(int days)
    {
        await ShowRemindersTabAsync();

        await Assertions.Expect(ReminderDaysBeforeInput).ToHaveValueAsync(
            days.ToString(System.Globalization.CultureInfo.InvariantCulture), new() { Timeout = 5000 });
        await Assertions.Expect(ReminderDaysDecrement).ToBeDisabledAsync(new() { Timeout = 5000 });
    }

    /// <summary>
    /// Asserts the scope prompt warns that applying a reminder change to the whole series replaces
    /// every occurrence's reminders.
    /// </summary>
    public async Task AssertScopePromptWarnsAboutRemindersAsync()
    {
        await ScopePrompt.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await Assertions.Expect(ScopePromptReminderWarning).ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    /// <summary>Switches the open event between all-day and timed, and waits for the change to land.</summary>
    public async Task ToggleAllDayAsync()
    {
        await ShowModalTabAsync("details");

        var wasOn = await AllDayToggle.GetAttributeAsync("aria-pressed") == "true";
        await AllDayToggle.ClickAsync();
        await Assertions.Expect(AllDayToggle)
            .ToHaveAttributeAsync("aria-pressed", wasOn ? "false" : "true", new() { Timeout = 5000 });
    }

    /// <summary>
    /// Saves the event modal that is already open and waits for the calendar to reconcile and repaint.
    /// </summary>
    public async Task SaveOpenEventAsync()
    {
        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
        await WaitForSyncSettledAsync();
    }

    /// <summary>
    /// Opens the create-event modal on the named calendar with the title filled in, ready for the
    /// Reminders tab. The calendar is chosen FIRST because which calendar an event lands on decides
    /// whose usual reminders the tab shows and copies in.
    /// </summary>
    public async Task BeginCreatingEventTitledAsync(string title, string calendarName)
    {
        await OpenCreateEventModalAsync();
        await EventTitleInput.FillAsync(title);
        await EnsureCalendarChipActiveAsync(calendarName);
    }

    /// <summary>
    /// Types a note into the open event modal's description field.
    /// <para>
    /// Separate from the create helpers because the scenario that needs it is asserting that a later
    /// edit left the note alone — so the note has to be put there by the same modal the edit will
    /// reopen, not by a seeding shortcut that bypasses it.
    /// </para>
    /// </summary>
    public async Task FillOpenEventDescriptionAsync(string description)
    {
        await EventModal.Locator("textarea").FillAsync(description);
    }

    /// <summary>Creates an all-day event with the title assigned to exactly one named calendar.</summary>
    public async Task CreateAllDayEventInCalendarAsync(string title, string calendarName)
    {
        await BeginCreatingEventTitledAsync(title, calendarName);
        await ToggleAllDayAsync();
        await SaveOpenEventAsync();
    }

    private static async Task SelectPillAsync(ILocator pill)
    {
        // Clicking an already-selected pill is a no-op in the component, so the pressed state is
        // asserted either way rather than conditioned on the current one.
        if (await pill.GetAttributeAsync("aria-pressed") != "true")
        {
            await pill.ClickAsync();
        }

        await Assertions.Expect(pill).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });
    }

    private static int UnitMinutes(string unit) => unit switch
    {
        "minutes" => 1,
        "hours" => 60,
        "days" => 24 * 60,
        "weeks" => 7 * 24 * 60,
        _ => throw new ArgumentOutOfRangeException(
            nameof(unit), unit, "Not a unit the reminder form offers (minutes, hours, days, weeks).")
    };

    // ── The Reminders VIEW (the fourth dashboard tab's timeline) ─────────────────────────────────
    // Distinct from "the Reminders tab" region above, which edits ONE event's own reminders inside
    // the event modal. This is the standalone timeline of every event whose reminders were set on the
    // event itself — one row per EVENT, never one per reminder, filed by when the event starts.
    //
    // Every row carries data-event-id so it can be addressed BY VALUE, never by position. That one
    // attribute is the whole identity of a row: there is exactly one row per event, and the view lists
    // only events whose reminders were set on the event itself, so there is no second kind of row to
    // tell apart.

    private ILocator RemindersEmptyState => Page.GetByTestId("reminders-empty");
    private ILocator RemindersAllDayFootnote => Page.GetByTestId("reminders-allday-footnote");
    private ILocator RemindersInheritedFootnote => Page.GetByTestId("reminders-inherited-footnote");
    private ILocator AllReminderRows => Page.GetByTestId("reminder-row");

    private static string ReminderSectionSlug(ReminderSectionKey key) => key switch
    {
        ReminderSectionKey.Today => "today",
        ReminderSectionKey.Tomorrow => "tomorrow",
        ReminderSectionKey.ThisWeek => "this-week",
        ReminderSectionKey.ThisMonth => "this-month",
        ReminderSectionKey.NextMonth => "next-month",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Not a reminders-section key.")
    };

    private ILocator ReminderSection(ReminderSectionKey key) =>
        Page.GetByTestId($"reminder-section-{ReminderSectionSlug(key)}");

    /// <summary>
    /// Switches to the Reminders tab — the fourth dashboard tab — and waits for its own fetch to
    /// land before returning.
    /// </summary>
    /// <remarks>
    /// The listener is set up BEFORE the click, mirroring <see cref="NavigateAndWaitAsync"/>, so a
    /// fast response cannot be missed. Waiting for the response alone is not quite enough: a
    /// NEGATIVE assertion (no row for an event that must never ping) has no element for Playwright's
    /// own auto-retry to anchor on, so a short settle follows — the same idiom
    /// <see cref="NavigateAgendaNextMonthAsync"/> uses after its own web-first assertion.
    /// </remarks>
    public async Task ShowRemindersViewAsync()
    {
        var remindersResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/reminders/upcoming"),
            new() { Timeout = 30000 });

        await RemindersTab.ClickAsync();
        await remindersResponseTask;
        await RemindersViewContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await Page.WaitForTimeoutAsync(1000);
    }

    /// <summary>
    /// Reads every row <paramref name="key"/>'s section is currently showing, addressed by its event
    /// id rather than by position.
    /// </summary>
    public async Task<IReadOnlyList<ReminderRowSnapshot>> ReadReminderRowsAsync(ReminderSectionKey key)
    {
        var rows = ReminderSection(key).GetByTestId("reminder-row");
        var count = await rows.CountAsync();
        var snapshots = new List<ReminderRowSnapshot>(count);
        for (var i = 0; i < count; i++)
        {
            snapshots.Add(await SnapshotAsync(rows.Nth(i)));
        }
        return snapshots;
    }

    private static async Task<ReminderRowSnapshot> SnapshotAsync(ILocator row)
    {
        var eventId = await row.GetAttributeAsync("data-event-id")
            ?? throw new InvalidOperationException("A reminder-row rendered with no data-event-id.");
        var text = (await row.InnerTextAsync()).Trim();

        return new ReminderRowSnapshot(Guid.Parse(eventId), text);
    }

    /// <summary>True when <paramref name="key"/>'s section is rendering its collapsed "Nothing" line.</summary>
    public async Task<bool> ReminderSectionIsEmptyAsync(ReminderSectionKey key) =>
        await ReminderSection(key).GetByTestId("reminder-section-empty").CountAsync() > 0;

    /// <summary>True when the whole view is rendering its "No reminders coming up" state.</summary>
    public async Task<bool> RemindersViewIsEntirelyEmptyAsync() =>
        await RemindersEmptyState.CountAsync() > 0;

    /// <summary>The permanent same-day-all-day-reminders footnote's text.</summary>
    public async Task<string> ReadAllDayFootnoteAsync() => (await RemindersAllDayFootnote.InnerTextAsync()).Trim();

    /// <summary>
    /// The permanent footnote's text saying events on their calendar's usual reminders are not listed.
    /// </summary>
    public async Task<string> ReadInheritedFootnoteAsync() =>
        (await RemindersInheritedFootnote.InnerTextAsync()).Trim();

    /// <summary>
    /// Finds the one row anywhere in the view whose rendered text names <paramref name="eventTitle"/>.
    /// A row carries no title ATTRIBUTE to match on — only its event id — so the title is used only to
    /// LOCATE it; callers address the row afterwards (e.g. via <see cref="TapReminderRowAsync"/>) using
    /// the id the returned snapshot carries.
    /// </summary>
    public async Task<ReminderRowSnapshot> FindReminderRowByTitleAsync(string eventTitle)
    {
        var row = AllReminderRows.Filter(new() { HasText = eventTitle });
        await Assertions.Expect(row.First).ToBeVisibleAsync(new() { Timeout = 10000 });
        return await SnapshotAsync(row.First);
    }

    /// <summary>
    /// The leading time column of the row naming <paramref name="eventTitle"/>, read on its own
    /// rather than out of the row's whole text. That is the point of it: the column renders the
    /// EVENT's start, and only reading it in isolation can tell a correct row from one that led with
    /// a reminder's trigger time instead.
    /// </summary>
    public async Task<string> ReadReminderRowStartTimeAsync(string eventTitle)
    {
        var row = AllReminderRows.Filter(new() { HasText = eventTitle });
        await Assertions.Expect(row.First).ToBeVisibleAsync(new() { Timeout = 10000 });

        return (await row.First.Locator(".reminder-row__time").InnerTextAsync()).Trim();
    }

    /// <summary>Every row anywhere in the view whose rendered text names <paramref name="eventTitle"/>.</summary>
    public Task<int> CountReminderRowsForTitleAsync(string eventTitle) =>
        AllReminderRows.Filter(new() { HasText = eventTitle }).CountAsync();

    /// <summary>
    /// Taps the row for <paramref name="eventId"/> and waits for the Day view to render.
    /// <paramref name="eventId"/> alone is enough to address it: there is exactly one row per event.
    /// </summary>
    /// <remarks>
    /// The Day view, not the event modal: a tap drills into the event's own day, as the Month and
    /// Agenda views' rows do. For an event outside the loaded month that involves a month fetch
    /// before the view can paint, which is why this waits rather than returning on the click.
    /// </remarks>
    public async Task TapReminderRowAsync(Guid eventId)
    {
        var row = Page.Locator($"[data-testid='reminder-row'][data-event-id='{eventId}']");
        await row.ClickAsync();
        await DayViewContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    /// <summary>
    /// Asserts the Day view is rendering a tile for <paramref name="eventName"/> on whichever day it
    /// is currently showing — without navigating anywhere, unlike
    /// <see cref="AssertEventVisibleInDayViewOnDateAsync"/>.
    /// </summary>
    /// <remarks>
    /// The tile is the assertion, not the container: DayView renders only the events filed under its
    /// own <c>SelectedDate</c>, so a tile for this event can only be here if the view opened on the
    /// event's day AND that day's month was loaded. A visible container alone would pass for the
    /// wrong day.
    /// </remarks>
    public async Task AssertDayViewShowingEventAsync(string eventName)
    {
        await DayViewContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        var tile = Page.Locator($".calendar-col .day-event-block:has-text('{eventName}')").First;
        await tile.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }
}
