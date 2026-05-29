using System.Text.RegularExpressions;
using FamilyHQ.E2E.Common.Configuration;
using Microsoft.Playwright;

namespace FamilyHQ.E2E.Common.Pages;

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

    // Weather Locators
    public ILocator WeatherStrip => Page.Locator(".weather-strip");
    public ILocator WeatherStripTemp => Page.Locator(".weather-strip__temp");
    public ILocator WeatherStripCondition => Page.Locator(".weather-strip__condition");
    public ILocator WeatherStripForecastDays => Page.Locator(".weather-strip__forecast-day");
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

    private async Task WaitForCalendarVisibleAsync()
    {
        // Wait for either the month table or day view container to be visible.
        // This is safe to call at any point — it simply waits for the final rendered state.
        // We intentionally do NOT wait for the spinner first because in some flows
        // (e.g. after OAuth redirect) the spinner may never appear in the DOM.
        await Page.Locator(".month-table, .day-view-container, .agenda-view-container").First.WaitForAsync(
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

    public async Task<int> GetAgendaDayRowCountAsync()
    {
        return await Page.Locator(".agenda-day-row").CountAsync();
    }

    public async Task<bool> HasTodayRowHighlightAsync()
    {
        // Wait for the agenda to paint today's row before the instant count (TOCTOU, intermittent-issues #6).
        try
        {
            await Page.Locator(".agenda-today-row").First
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        }
        catch (TimeoutException)
        {
            return false;
        }
        return await Page.Locator(".agenda-today-row").CountAsync() == 1;
    }

    public async Task<bool> WeekendRowsHaveClassAsync()
    {
        // Wait for at least one row to be rendered before counting
        await Page.Locator(".agenda-weekend-row").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        // A month always has at least 8 weekend days
        return await Page.Locator(".agenda-weekend-row").CountAsync() >= 8;
    }

    public async Task<int> GetWeekdayRowsWithoutWeekendClassAsync()
    {
        // Wait for the agenda to paint at least one day row before counting; an unrendered
        // agenda yields 0 and a false-negative subtraction (TOCTOU, intermittent-issues #6).
        await Page.Locator(".agenda-day-row").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        // All .agenda-day-row rows that do NOT have .agenda-weekend-row
        var all = await Page.Locator(".agenda-day-row").CountAsync();
        var weekends = await Page.Locator(".agenda-weekend-row").CountAsync();
        return all - weekends;
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

    public async Task<int> GetAgendaEventLineCountAsync(string dateKey, Guid calendarId)
    {
        var cell = Page.GetByTestId($"agenda-cell-{dateKey}-{calendarId}");
        return await cell.Locator(".agenda-event-line").CountAsync();
    }

    public async Task<string> GetAgendaOverflowTextAsync(string dateKey, Guid calendarId)
    {
        return await Page.GetByTestId($"agenda-overflow-{dateKey}-{calendarId}").InnerTextAsync();
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
        var chip = EventModal.Locator(".chip").Filter(new() { HasText = calendarName });
        var classes = await chip.GetAttributeAsync("class") ?? "";
        return classes.Contains("chip-active");
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
        var nextMonth = DateTime.Today.AddMonths(1);
        var expectedMonthText = nextMonth.ToString("MMMM yyyy"); // e.g. "April 2026"

        await NextMonthBtn.ClickAsync();

        // Wait for the month header button to display the next month, confirming the
        // navigation has been processed and the UI has re-rendered.
        var monthHeaderBtn = Page.GetByRole(AriaRole.Button, new() { Name = expectedMonthText });
        await monthHeaderBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    /// <summary>
    /// Navigates to the next month only if <paramref name="date"/> falls outside the
    /// currently-visible month grid. The grid spans from the Sunday on or before the
    /// first of the current month to the Saturday on or after the last day of the month.
    /// </summary>
    public async Task NavigateToShowDateIfNeededAsync(DateTime date)
    {
        var today = DateTime.Today;
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
    }

    // --- FHQ-32: create modal must not silently default the calendar selection ---

    /// <summary>Opens the create-event modal via the Add Event button.</summary>
    public async Task OpenCreateEventModalAsync()
    {
        await AddEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    /// <summary>
    /// Number of calendar chips offered in the open modal that match
    /// <paramref name="calendarName"/>. Used to assert the shared calendar is never offered.
    /// </summary>
    public async Task<int> CalendarChipCountInModalAsync(string calendarName)
        => await EventModal.Locator(".chip").Filter(new() { HasText = calendarName }).CountAsync();

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
        var alert = EventModal.Locator(".alert-danger");
        await alert.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var text = await alert.InnerTextAsync();
        return await EventModal.IsVisibleAsync()
            && text.Contains("calendar", StringComparison.OrdinalIgnoreCase);
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
    }

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

    public async Task<int> GetCalendarHeaderCountAsync()
    {
        return await Page.Locator(".calendar-header-col").CountAsync();
    }

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
        // Wait for the time line to paint before the instant visibility read; it renders only
        // when viewing today and can lag the Day-view switch (TOCTOU, intermittent-issues #6).
        try
        {
            await CurrentTimeLine.First
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        }
        catch (TimeoutException)
        {
            return false;
        }
        return await CurrentTimeLine.IsVisibleAsync();
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

    /// <summary>Number of recurrence indicator glyphs currently rendered in the view.</summary>
    public async Task<int> CountRecurrenceIndicatorsAsync() => await RecurrenceIndicators.CountAsync();

    /// <summary>
    /// Reads the recurrence subtitle text from the open event modal (e.g.
    /// "Repeats weekly on Tuesday"). Waits for the subtitle to be visible first.
    /// </summary>
    public async Task<string> GetRecurrenceSubtitleTextAsync(int timeoutMs = 30000)
    {
        await RecurrenceSubtitle.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
        return (await RecurrenceSubtitle.InnerTextAsync()).Trim();
    }

    // FHQ-18.11 Pass 2 — native recurring-event create & toggle-off ────────────
    // The recurrence picker lives inside the open event modal under
    // [data-testid="recurrence-section"]. These helpers drive it via the per-option
    // data-testids added to the mode / frequency / end-mode pills and the weekday
    // toggle row, then compose the full native-create and toggle-off flows.

    private ILocator RecurrenceSection => EventModal.GetByTestId("recurrence-section");
    private ILocator ScopePrompt => Page.GetByTestId("recurrence-scope-prompt");
    private ILocator ScopePromptOkBtn => Page.GetByTestId("recurrence-scope-ok");

    // FHQ-18.11 Pass 5 (§10.1): the inline warning shown in the scope prompt when a member change is
    // pending and a non-All scope is selected. Member changes are only valid for the whole series.
    private ILocator ScopePromptMemberWarning => Page.GetByTestId("recurrence-scope-member-warning");

    // FHQ-18.11 Pass 3: the three scope pills inside the prompt. Scope names map to the testids
    // declared on RecurrenceScopePrompt's PillSegmentGroup options.
    private ILocator ScopePromptPill(string scope) => Page.GetByTestId($"recurrence-scope-{scope}");

    /// <summary>Selects a recurrence mode pill (e.g. "weekly", "custom", "none").</summary>
    private async Task SelectRecurrenceModeAsync(string mode)
    {
        var pill = RecurrenceSection.GetByTestId($"recurrence-mode-{mode}");
        await pill.ClickAsync();
        await Assertions.Expect(pill).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });
    }

    /// <summary>Selects a custom-drawer frequency pill (e.g. "weekly"). Requires Custom mode.</summary>
    private async Task SelectRecurrenceFrequencyAsync(string frequency)
    {
        var pill = RecurrenceSection.GetByTestId($"recurrence-frequency-{frequency}");
        await pill.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await pill.ClickAsync();
        await Assertions.Expect(pill).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });
    }

    /// <summary>Toggles a weekday button on in the custom weekly drawer (DayOfWeek name, e.g. "Tuesday").</summary>
    private async Task ToggleRecurrenceWeekdayAsync(string dayOfWeekName)
    {
        var toggle = RecurrenceSection.GetByTestId($"recurrence-weekday-{dayOfWeekName}");
        await toggle.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });
    }

    /// <summary>Activates the named calendar chip in the open modal if it is not already active.</summary>
    private async Task EnsureCalendarChipActiveAsync(string calendarName)
    {
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

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
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

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
    }

    /// <summary>
    /// Opens an existing recurring event for editing, sets recurrence to "Does not repeat", saves,
    /// and confirms the recurrence-scope prompt (defaulting to "All events") so the series collapses
    /// to a single non-recurring event. Waits for the reconcile response and repaint.
    /// </summary>
    public async Task TurnOffRecurrenceForEventAsync(string eventName)
    {
        await OpenEventForEditingAsync(eventName);
        await SelectRecurrenceModeAsync("none");

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

        var count = await tiles.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var style = await tiles.Nth(i).GetAttributeAsync("style") ?? string.Empty;
            if (style.Contains(calendarColour, StringComparison.OrdinalIgnoreCase))
                return;
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

        // Observable block: the member warning is shown and OK is disabled.
        await ScopePromptMemberWarning.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        var warningVisible = await ScopePromptMemberWarning.IsVisibleAsync();
        var okDisabled = await ScopePromptOkBtn.IsDisabledAsync();
        return warningVisible && okDisabled;
    }
}
