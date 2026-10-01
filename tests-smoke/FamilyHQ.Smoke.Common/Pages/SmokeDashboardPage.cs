using System.Globalization;
using System.Text.RegularExpressions;
using FamilyHQ.Smoke.Common.Configuration;
using Microsoft.Playwright;

namespace FamilyHQ.Smoke.Common.Pages;

/// <summary>
/// The preprod kiosk, as the smoke suite drives it.
/// <para>
/// A deliberately small page object: the smoke suite is not a second E2E suite, so it only knows how
/// to do the handful of things the third-party scenarios need — open the dashboard, read the weather
/// strip, create / rename / delete an event, create a bounded weekly series, and look at the Day view.
/// Everything else about the UI stays E2E's business, and this class is a standalone duplicate of the
/// parts it needs rather than a reference into that suite.
/// </para>
/// <para>
/// Assertions live in the step definitions. What lives here is the knowledge of which control is which,
/// and the waits that make an interaction deterministic — never a wait that papers over a failure.
/// </para>
/// </summary>
public sealed class SmokeDashboardPage(IPage page, SmokeConfiguration configuration) : SmokeBasePage(page)
{
    private const int TileRenderTimeoutMs = 30000;
    private const int MaxStepperClicks = 60;

    private static readonly Regex ActiveChipClass = new("chip-active", RegexOptions.Compiled);

    public override string PageUrl => configuration.BaseUrl.TrimEnd('/') + "/";

    // ── Dashboard chrome ────────────────────────────────────────────────────────

    public ILocator WeatherStrip => Page.GetByTestId("weather-strip");

    public ILocator WeatherCurrentConditions => Page.GetByTestId("weather-strip-current");

    public ILocator WeatherTemperature => Page.GetByTestId("weather-strip-temp");

    public ILocator WeatherCondition => Page.GetByTestId("weather-strip-condition");

    public ILocator DayViewTiles => Page.GetByTestId("day-event-block");

    public ILocator AllDayTiles => Page.GetByTestId("event-capsule");

    public ILocator RecurrenceIndicators => Page.GetByTestId("recurrence-indicator");

    // ── Modal ───────────────────────────────────────────────────────────────────

    private ILocator EventModal => Page.Locator(".modal-content");

    private ILocator TitleInput => EventModal.GetByTestId("event-title-input");

    private ILocator DescriptionInput => EventModal.GetByTestId("event-description-input");

    private ILocator LocationInput => EventModal.GetByTestId("event-location-input");

    private ILocator SaveButton => EventModal.GetByTestId("event-save-btn");

    private ILocator DeleteButton => EventModal.GetByTestId("event-delete-btn");

    private ILocator AllDayToggle => EventModal.GetByTestId("all-day-toggle");

    private ILocator RecurrenceSection => EventModal.GetByTestId("recurrence-section");

    private ILocator RepeatToggle => RecurrenceSection.GetByTestId("recurrence-repeat-toggle");

    private ILocator ScopePrompt => Page.GetByTestId("recurrence-scope-prompt");

    private ILocator ScopePromptConfirm => Page.GetByTestId("recurrence-scope-ok");

    /// <summary>
    /// Opens the dashboard and waits for the calendar's first events response, so a scenario never
    /// asserts against an empty grid that is merely still loading.
    /// </summary>
    public async Task OpenAsync()
    {
        var eventsResponse = Page.WaitForResponseAsync(
            response => response.Url.Contains("api/calendars/events", StringComparison.Ordinal),
            new PageWaitForResponseOptions { Timeout = configuration.DefaultTimeoutMs });

        await NavigateAsync();
        await eventsResponse;
        await WaitForCalendarVisibleAsync();
    }

    public Task WaitForWeatherStripAsync(int timeoutMs) =>
        Assertions.Expect(WeatherStrip).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = timeoutMs });

    /// <summary>Switches to the Day view and moves it to <paramref name="date"/>.</summary>
    public async Task GoToDayAsync(DateOnly date)
    {
        await Page.GetByTestId("day-tab").ClickAsync();
        await Page.Locator(".day-view-container")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await Page.GetByTestId("day-picker-btn").ClickAsync();
        var picker = Page.Locator(".modal-backdrop").Filter(new LocatorFilterOptions { HasText = "Select Date" });
        await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await Page.GetByTestId("day-picker-input")
            .FillAsync(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await Page.GetByTestId("day-picker-go-btn").ClickAsync();

        await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await WaitForCalendarVisibleAsync();
    }

    /// <summary>Every event title currently rendered on the Day view, timed tiles and all-day tiles alike.</summary>
    public async Task<IReadOnlyList<string>> GetDayViewTitlesAsync()
    {
        var timed = await DayViewTiles.AllInnerTextsAsync();
        var allDay = await AllDayTiles.AllInnerTextsAsync();
        return [.. timed, .. allDay];
    }

    /// <summary>
    /// Waits until a tile whose text contains <paramref name="titleFragment"/> is on the Day view for
    /// <paramref name="date"/>. Day view rather than Month view on purpose: the month grid renders at
    /// most three tiles per day, and a day can hold more than that — the events a failed scenario leaves
    /// behind stay on the calendars until somebody clears them.
    /// </summary>
    public async Task WaitForEventOnDayAsync(string titleFragment, DateOnly date)
    {
        await GoToDayAsync(date);
        await TileWithText(titleFragment).First
            .WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = TileRenderTimeoutMs
            });
    }

    /// <summary>True when the tile carrying <paramref name="titleFragment"/> also carries the recurring glyph.</summary>
    public async Task<bool> TileIsMarkedRecurringAsync(string titleFragment)
    {
        var tile = TileWithText(titleFragment).First;
        await tile.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = TileRenderTimeoutMs
        });
        return await tile.GetByTestId("recurrence-indicator").CountAsync() > 0;
    }

    /// <summary>
    /// Creates an event through the kiosk exactly as a family member would, then waits for the create
    /// to be accepted. A non-success status is raised immediately rather than surfacing later as a
    /// "modal never closed" timeout.
    /// </summary>
    public async Task CreateEventAsync(SmokeEventDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await Page.GetByTestId("add-event-btn").ClickAsync();
        await EventModal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ShowTabAsync("details");
        await CommitFieldAsync(TitleInput, draft.Title);
        await CommitFieldAsync(DescriptionInput, draft.Description);

        if (draft.Location is not null)
        {
            await CommitFieldAsync(LocationInput, draft.Location);
        }

        // All-day first, because it decides whether the time pickers are rendered at all. Setting it
        // afterwards would leave a pair of times that the modal has since stopped showing.
        await SetAllDayAsync(draft.IsAllDay);

        await SetDateRangeAsync(draft.Date);

        if (!draft.IsAllDay)
        {
            // Start before end, and never the other way round. The modal's start-time setter preserves
            // the event's duration by shifting the end by the same delta (moving 09:00→10:00 drags a
            // 10:00 end to 11:00), so a start set afterwards would silently move an end that was
            // already correct. Setting the end second is safe because its setter is absolute.
            await SetTimeAsync("start", draft.StartTime);
            await SetTimeAsync("end", draft.EndTime);
        }

        foreach (var calendarName in draft.CalendarNames)
        {
            await ActivateCalendarChipAsync(calendarName);
        }

        // After the calendars, never before: an event's reminders belong to the calendar it lands on, and
        // the modal re-reads that calendar's own reminders into the tab while the tab is still untouched.
        // Choosing reminders first would have them replaced by the ones the chosen calendar carries.
        if (draft.Reminders is not null)
        {
            await SetOwnRemindersAsync(draft.Reminders);
        }

        if (draft.Recurrence is not null)
        {
            await SetBoundedRepeatAsync(draft.Recurrence);
        }

        await SaveAndAwaitWriteAsync("POST", $"create '{draft.Title}'");
    }

    /// <summary>
    /// Changes an event's title on the kiosk and <b>nothing else</b> — the golden-rule scenario. No
    /// other control in the modal is touched, so whatever Google holds for the other fields is whatever
    /// FamilyHQ chooses to send back for them.
    /// </summary>
    public async Task RenameEventAsync(string currentTitleFragment, string newTitle, DateOnly date)
    {
        await OpenEventAsync(currentTitleFragment, date);
        await ShowTabAsync("details");
        await CommitFieldAsync(TitleInput, newTitle);
        await SaveAndAwaitWriteAsync("PUT", $"rename to '{newTitle}'");
    }

    /// <summary>Deletes an event from the kiosk.</summary>
    public Task DeleteEventAsync(string titleFragment, DateOnly date) =>
        DeleteThroughModalAsync(titleFragment, date, scope: null);

    /// <summary>
    /// Renames one occurrence of a series and applies the change at <paramref name="scope"/> — the
    /// choice the kiosk asks for after Save.
    /// <para>
    /// Only the title changes, so whatever else Google holds afterwards is FamilyHQ's own choice of
    /// what to send back, exactly as in the single-event golden-rule flow.
    /// </para>
    /// </summary>
    public async Task RenameOccurrenceAsync(
        string currentTitleFragment, string newTitle, DateOnly occurrenceDate, SmokeRecurrenceScope scope)
    {
        await OpenEventAsync(currentTitleFragment, occurrenceDate);
        await ShowTabAsync("details");
        await CommitFieldAsync(TitleInput, newTitle);
        await SaveAndAwaitWriteAsync("PUT", $"rename to '{newTitle}' at scope {scope}", scope);
    }

    /// <summary>Deletes one occurrence of a series, applying the delete at <paramref name="scope"/>.</summary>
    public Task DeleteOccurrenceAsync(
        string titleFragment, DateOnly occurrenceDate, SmokeRecurrenceScope scope) =>
        DeleteThroughModalAsync(titleFragment, occurrenceDate, scope);

    /// <summary>
    /// Switches the repeat off on an existing series, which collapses it to the single event the
    /// series started from.
    /// <para>
    /// The kiosk still asks which occurrences to change, and the answer is stated rather than defaulted:
    /// a collapse is inherently a whole-series operation, so "all events" is the only honest answer — and
    /// stating it means the prompt is <b>waited for</b> instead of glanced at, which is the difference
    /// between a reliable step and one that depends on the prompt having rendered by the time it is
    /// looked at.
    /// </para>
    /// </summary>
    public async Task TurnOffRecurrenceAsync(string titleFragment, DateOnly date)
    {
        await OpenEventAsync(titleFragment, date);
        await ShowTabAsync("repeat");

        if (await RepeatToggle.GetAttributeAsync("aria-pressed") == "true")
        {
            await RepeatToggle.ClickAsync();
        }

        await Assertions.Expect(RepeatToggle).ToHaveAttributeAsync("aria-pressed", "false");
        await SaveAndAwaitWriteAsync(
            "PUT", $"switch the repeat off on '{titleFragment}'", SmokeRecurrenceScope.AllEvents);
    }

    // ── The Reminders tab ───────────────────────────────────────────────────────
    // Rows are addressed BY VALUE — by the method and the offset they carry as attributes — and never by
    // index. Google returns an event's overrides in an order of its own choosing, so the row in position
    // one is not the reminder a scenario is talking about.

    /// <summary>
    /// Reads back the reminders the Reminders tab is showing for an event, without changing anything.
    /// <para>
    /// Opening the tab is deliberately part of the golden-rule flow as well: merely looking at it must
    /// leave the save saying nothing about reminders, and a scenario that read them and then renamed the
    /// event proves that as a side effect.
    /// </para>
    /// </summary>
    /// <param name="expectedCount">
    /// How many rows the scenario expects, used only to wait for the tab to finish rendering. A count that
    /// never arrives is <b>not</b> raised here: the rows are read and returned anyway, so the caller's own
    /// comparison names which reminder is missing instead of leaving a bare locator timeout behind.
    /// </param>
    public async Task<IReadOnlyList<SmokeReminder>> ReadDisplayedRemindersAsync(
        string titleFragment, DateOnly date, int expectedCount)
    {
        await OpenEventAsync(titleFragment, date);
        await ShowTabAsync("reminders");

        try
        {
            await Assertions.Expect(ReminderRows).ToHaveCountAsync(expectedCount);
        }
        catch (PlaywrightException)
        {
            // Swallowed on purpose — see expectedCount.
        }

        var displayed = new List<SmokeReminder>();
        foreach (var row in await ReminderRows.AllAsync())
        {
            var method = await row.GetAttributeAsync("data-reminder-method");
            var minutes = await row.GetAttributeAsync("data-reminder-minutes");

            if (method is null
                || !int.TryParse(minutes, CultureInfo.InvariantCulture, out var offsetMinutes))
            {
                throw new InvalidOperationException(
                    "A reminder row on the kiosk carried no method or no offset, so the tab cannot be "
                    + $"read by value (method='{method}', minutes='{minutes}'). The attributes are how "
                    + "both test suites address a row; addressing one by position would silently compare "
                    + "the wrong reminder.");
            }

            displayed.Add(SmokeReminder.FromMinutes(method, offsetMinutes));
        }

        await CloseModalWithoutSavingAsync();
        return displayed;
    }

    /// <summary>
    /// Hands an event's reminders back to its calendar's own — the state Google records as
    /// <c>useDefault</c> — and saves.
    /// </summary>
    public async Task UseCalendarDefaultRemindersAsync(string titleFragment, DateOnly date)
    {
        await OpenEventAsync(titleFragment, date);
        await SetReminderInheritanceAsync(true);
        await SaveAndAwaitWriteAsync(
            "PUT", $"hand the reminders on '{titleFragment}' back to its calendar");
    }

    /// <summary>
    /// Switches an existing event to all day and saves, touching nothing else.
    /// <para>
    /// This is the one path that writes reminders without the Reminders tab ever being opened: Google
    /// discards an event's reminders when it becomes all-day rather than converting them, the kiosk
    /// mirrors that, and the save therefore also asks for the calendar's own reminders.
    /// </para>
    /// </summary>
    public async Task SwitchToAllDayAsync(string titleFragment, DateOnly date)
    {
        await OpenEventAsync(titleFragment, date);
        await ShowTabAsync("details");
        await SetAllDayAsync(true);
        await SaveAndAwaitWriteAsync("PUT", $"switch '{titleFragment}' to all day");
    }

    // ── Internals ───────────────────────────────────────────────────────────────

    private ILocator RemindersSection => EventModal.GetByTestId("reminders-section");

    private ILocator ReminderUseDefaultToggle => RemindersSection.GetByTestId("reminder-use-default-toggle");

    private ILocator ReminderRows => RemindersSection.GetByTestId("reminder-item");

    private ILocator ReminderAmountInput => RemindersSection.GetByTestId("reminder-amount");

    private ILocator ReminderAddButton => RemindersSection.GetByTestId("reminder-add-btn");

    private ILocator ReminderUnitPill(SmokeReminderUnit unit) =>
        RemindersSection.GetByTestId($"reminder-unit-{SmokeReminder.UnitTestIdSuffix(unit)}");

    private ILocator ReminderMethodPill(string method) =>
        RemindersSection.GetByTestId($"reminder-method-{method}");

    private ILocator ReminderRow(string method, int minutes) => RemindersSection.Locator(
        $"[data-testid='reminder-item'][data-reminder-method='{method}'][data-reminder-minutes='{minutes}']");

    /// <summary>
    /// Leaves the open modal's Reminders tab holding exactly <paramref name="reminders"/>.
    /// <para>
    /// Switching the inheritance toggle off copies the calendar's own reminders in as editable rows, as
    /// the Google Calendar app pre-fills them, so they are removed before the scenario's own are added.
    /// Otherwise the event would end up carrying the calendar's reminders as well, and a set comparison
    /// against Google would be asserting whatever that calendar happens to be configured with today.
    /// </para>
    /// </summary>
    private async Task SetOwnRemindersAsync(IReadOnlyList<SmokeReminder> reminders)
    {
        await SetReminderInheritanceAsync(false);

        // Always the first remaining row, then wait for the count to drop, so the next click cannot land
        // on an element the re-render has already replaced.
        for (var remaining = await ReminderRows.CountAsync(); remaining > 0; remaining--)
        {
            await ReminderRows.First.GetByTestId("reminder-remove").ClickAsync();
            await Assertions.Expect(ReminderRows).ToHaveCountAsync(remaining - 1);
        }

        foreach (var reminder in reminders)
        {
            await AddReminderAsync(reminder);
        }
    }

    private async Task SetReminderInheritanceAsync(bool follow)
    {
        await ShowTabAsync("reminders");

        var wanted = follow ? "true" : "false";
        if (await ReminderUseDefaultToggle.GetAttributeAsync("aria-pressed") != wanted)
        {
            await ReminderUseDefaultToggle.ClickAsync();
        }

        await Assertions.Expect(ReminderUseDefaultToggle).ToHaveAttributeAsync("aria-pressed", wanted);
    }

    /// <summary>
    /// Adds one reminder through the picker's own controls, in the order the picker requires: the unit
    /// first, because changing it re-clamps the amount and would otherwise undo a value already typed.
    /// </summary>
    private async Task AddReminderAsync(SmokeReminder reminder)
    {
        await PressPillAsync(ReminderUnitPill(reminder.Unit));
        await PressPillAsync(ReminderMethodPill(reminder.Method));

        var wanted = reminder.Amount.ToString(CultureInfo.InvariantCulture);
        await CommitFieldAsync(ReminderAmountInput, wanted);

        // The picker rebuilds this field from its own model whenever it corrects a typed number, so
        // reading it back is the model's answer rather than the keystrokes'. Same distinction as the time
        // picker, and the same reason: a value that sat in the DOM without committing would fail later,
        // somewhere else, as a reminder Google never received.
        var accepted = await ReminderAmountInput.InputValueAsync();
        if (!string.Equals(accepted, wanted, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The reminder picker did not accept {wanted} {reminder.Unit} as an offset; the field "
                + $"reads '{accepted}'. Either the value is outside what Google allows, or it never "
                + "committed to the component's model. Nothing downstream of this can be trusted.");
        }

        await ReminderAddButton.ClickAsync();
        await Assertions.Expect(ReminderRow(reminder.Method, reminder.Minutes)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Closes the modal, discarding whatever it is showing.
    /// <para>
    /// Addressed by its accessible name rather than by a <c>data-testid</c>, which it has not got. That is
    /// still identity and not user-facing copy — the control renders no text at all — so it does not move
    /// when the wording elsewhere in the modal does.
    /// </para>
    /// </summary>
    private async Task CloseModalWithoutSavingAsync()
    {
        await EventModal
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close", Exact = true })
            .ClickAsync();
        await EventModal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await WaitForCalendarVisibleAsync();
    }

    private async Task DeleteThroughModalAsync(
        string titleFragment, DateOnly date, SmokeRecurrenceScope? scope)
    {
        await OpenEventAsync(titleFragment, date);

        var deleteResponse = Page.WaitForResponseAsync(
            response => response.Url.Contains("/api/events", StringComparison.Ordinal)
                        && string.Equals(response.Request.Method, "DELETE", StringComparison.Ordinal),
            new PageWaitForResponseOptions { Timeout = configuration.DefaultTimeoutMs });

        await DeleteButton.ClickAsync();
        await ConfirmScopePromptAsync(scope, $"delete '{titleFragment}'");
        await AssertWriteSucceededAsync(await deleteResponse, $"delete '{titleFragment}'");
        await EventModal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await WaitForCalendarVisibleAsync();
    }

    private ILocator TileWithText(string titleFragment) =>
        Page.Locator("[data-testid='day-event-block'], [data-testid='event-capsule']")
            .Filter(new LocatorFilterOptions { HasText = titleFragment });

    private Task WaitForCalendarVisibleAsync() =>
        Page.Locator(".month-table, .day-view-container, .agenda-view-container").First
            .WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = TileRenderTimeoutMs
            });

    private async Task OpenEventAsync(string titleFragment, DateOnly date)
    {
        await GoToDayAsync(date);
        await TileWithText(titleFragment).First.ClickAsync(new LocatorClickOptions
        {
            Timeout = TileRenderTimeoutMs
        });
        await EventModal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    }

    private async Task ShowTabAsync(string tab)
    {
        var tabButton = EventModal.GetByTestId($"event-modal-tab-{tab}");
        await tabButton.ClickAsync();
        await Assertions.Expect(EventModal.GetByTestId($"event-modal-panel-{tab}")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Types a value into a Blazor-bound field <b>and makes it stick</b>.
    /// <para>
    /// Playwright's <c>FillAsync</c> sets the value and raises <c>input</c>, but the <c>change</c> event that
    /// Blazor's <c>@bind</c> and <c>@onchange</c> actually listen for does not follow until the field is
    /// blurred. Elsewhere in a test suite that is invisible: the next thing a flow does is usually click
    /// another control, the click blurs the field, <c>change</c> fires, and the value commits just in time.
    /// It is sequencing luck, and FHQ-141's first preprod run is what it looks like when the luck runs out —
    /// the value sat in the DOM while the component's model kept its old one.
    /// </para>
    /// <para>
    /// Pressing Tab makes the commit explicit and ordered, so no field depends on what happens to it next.
    /// </para>
    /// </summary>
    private static async Task CommitFieldAsync(ILocator field, string value)
    {
        await field.FillAsync(value);
        await field.PressAsync("Tab");
    }

    private async Task SetDateRangeAsync(DateOnly date)
    {
        var value = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dateInputs = EventModal.Locator("input[type='date']");

        await CommitFieldAsync(dateInputs.Nth(0), value);
        await CommitFieldAsync(dateInputs.Nth(1), value);

        // Read back from the DOM, which after a commit is re-rendered from the component's model — so this
        // is a check that the model took the date, not merely that the keystrokes landed.
        foreach (var index in new[] { 0, 1 })
        {
            var actual = await dateInputs.Nth(index).InputValueAsync();
            if (!string.Equals(actual, value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The event modal did not accept {value} as its "
                    + $"{(index == 0 ? "start" : "end")} date; it reads '{actual}'.");
            }
        }
    }

    /// <summary>
    /// Sets one of the two time pickers and confirms the component's <b>model</b> took the value.
    /// <para>
    /// The stepper readouts are rendered from the model, and the text box is not — so asserting the
    /// readouts is the only way to tell "the value committed" from "the value is merely on screen". That
    /// distinction is the whole bug: a typed value that never committed left the picker showing 10:00 over
    /// a model still holding 09:00, and the scenario went on to fail somewhere else entirely.
    /// </para>
    /// <para>
    /// On failure it says what it wanted, what the model holds and what the box shows, rather than leaving
    /// a bare locator timeout for someone to reverse-engineer from a screenshot.
    /// </para>
    /// </summary>
    private async Task SetTimeAsync(string which, TimeOnly time)
    {
        var picker = EventModal.GetByTestId($"{which}-time-picker");
        var textField = picker.Locator("input[type='text']");
        var displays = picker.Locator(".time-picker-display");

        var wanted = time.ToString("HH\\:mm", CultureInfo.InvariantCulture);
        await CommitFieldAsync(textField, wanted);

        try
        {
            await Assertions.Expect(displays.Nth(0))
                .ToHaveTextAsync(time.Hour.ToString("00", CultureInfo.InvariantCulture));
            await Assertions.Expect(displays.Nth(1))
                .ToHaveTextAsync(time.Minute.ToString("00", CultureInfo.InvariantCulture));
        }
        catch (PlaywrightException ex)
        {
            var model = $"{(await displays.Nth(0).InnerTextAsync()).Trim()}"
                        + $":{(await displays.Nth(1).InnerTextAsync()).Trim()}";
            var shown = await textField.InputValueAsync();
            var classes = await textField.GetAttributeAsync("class") ?? string.Empty;

            throw new InvalidOperationException(
                $"The {which} time picker did not accept {wanted}. Its steppers — which render the "
                + $"component's model — read {model}, and its text box reads '{shown}'"
                + (classes.Contains("is-invalid", StringComparison.Ordinal)
                    ? " and is flagged invalid, so the value was rejected as unparseable."
                    : ", so the typed value never committed to the model.")
                + " Nothing downstream of this can be trusted, so the scenario stops here.",
                ex);
        }
    }

    private async Task ActivateCalendarChipAsync(string calendarName)
    {
        await ShowTabAsync("details");
        var chip = EventModal.Locator(".chip").Filter(new LocatorFilterOptions { HasText = calendarName });
        var classes = await chip.GetAttributeAsync("class") ?? string.Empty;
        if (!classes.Contains("chip-active", StringComparison.Ordinal))
        {
            await chip.ClickAsync();
        }

        await Assertions.Expect(chip).ToHaveClassAsync(ActiveChipClass);
    }

    /// <summary>
    /// Sets the all-day toggle to <paramref name="on"/> and confirms the component took it. The
    /// toggle renders its own state, so the pressed attribute is the model's answer rather than the
    /// click's.
    /// </summary>
    private async Task SetAllDayAsync(bool on)
    {
        var wanted = on ? "true" : "false";

        if (await AllDayToggle.GetAttributeAsync("aria-pressed") != wanted)
        {
            await AllDayToggle.ClickAsync();
        }

        await Assertions.Expect(AllDayToggle).ToHaveAttributeAsync("aria-pressed", wanted);
    }

    /// <summary>
    /// Drives the recurrence picker to a bounded rule. The Custom drawer is the only place the picker
    /// offers an end condition, so a bounded series is created there by construction — every preset
    /// would produce an endless one.
    /// </summary>
    private async Task SetBoundedRepeatAsync(SmokeRecurrence recurrence)
    {
        await ShowTabAsync("repeat");

        if (await RepeatToggle.GetAttributeAsync("aria-pressed") != "true")
        {
            await RepeatToggle.ClickAsync();
            await Assertions.Expect(RepeatToggle).ToHaveAttributeAsync("aria-pressed", "true");
        }

        await PressPillAsync(RecurrenceSection.GetByTestId("recurrence-mode-custom"));
        await PressPillAsync(
            RecurrenceSection.GetByTestId(
                $"recurrence-frequency-{recurrence.Frequency.ToString().ToLowerInvariant()}"));

        if (recurrence.Interval != 1)
        {
            await SetStepperAsync("recurrence-interval", recurrence.Interval);
        }

        if (recurrence.SelectedWeekdays.Count > 0)
        {
            await SetWeekdaysAsync(recurrence.SelectedWeekdays);
        }

        await PressPillAsync(RecurrenceSection.GetByTestId("recurrence-end-count"));
        await SetStepperAsync("recurrence-count", recurrence.Occurrences);
    }

    private static async Task PressPillAsync(ILocator pill)
    {
        await pill.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await pill.ClickAsync();
        await Assertions.Expect(pill).ToHaveAttributeAsync("aria-pressed", "true");
    }

    /// <summary>
    /// Leaves exactly <paramref name="weekdays"/> pressed. Choosing Custom + Weekly pre-selects the
    /// start date's weekday, so the unwanted ones have to be switched off — asserting "exactly Google's
    /// instances" later means the rule sent must be exactly the rule asked for.
    /// </summary>
    private async Task SetWeekdaysAsync(IReadOnlyList<DayOfWeek> weekdays)
    {
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var toggle = RecurrenceSection.GetByTestId($"recurrence-weekday-{day}");
            var wanted = weekdays.Contains(day);
            var pressed = await toggle.GetAttributeAsync("aria-pressed") == "true";
            if (pressed != wanted)
            {
                await toggle.ClickAsync();
            }

            await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", wanted ? "true" : "false");
        }
    }

    /// <summary>
    /// Steps a numeric recurrence field to <paramref name="wanted"/> using the picker's own +/-
    /// buttons rather than typing into it, because the stepper is what a kiosk user has and it commits
    /// on every click.
    /// </summary>
    private async Task SetStepperAsync(string fieldTestId, int wanted)
    {
        var field = RecurrenceSection.GetByTestId(fieldTestId);
        var increment = RecurrenceSection.GetByTestId($"{fieldTestId}-increment");
        var decrement = RecurrenceSection.GetByTestId($"{fieldTestId}-decrement");

        for (var click = 0; click < MaxStepperClicks; click++)
        {
            var current = int.Parse(await field.InputValueAsync(), CultureInfo.InvariantCulture);
            if (current == wanted)
            {
                return;
            }

            await (current < wanted ? increment : decrement).ClickAsync();
        }

        throw new InvalidOperationException(
            $"The '{fieldTestId}' stepper did not reach {wanted} within {MaxStepperClicks} clicks.");
    }

    private Task SaveAndAwaitWriteAsync(string method, string what) =>
        SaveAndAwaitWriteAsync(method, what, scope: null);

    private async Task SaveAndAwaitWriteAsync(string method, string what, SmokeRecurrenceScope? scope)
    {
        await AssertSaveIsNotBlockedAsync(what);

        var writeResponse = Page.WaitForResponseAsync(
            response => response.Url.Contains("/api/events", StringComparison.Ordinal)
                        && string.Equals(response.Request.Method, method, StringComparison.Ordinal),
            new PageWaitForResponseOptions { Timeout = configuration.DefaultTimeoutMs });

        await SaveButton.ClickAsync();
        await ConfirmScopePromptAsync(scope, what);

        IResponse response;
        try
        {
            response = await writeResponse;
        }
        catch (TimeoutException ex)
        {
            // The modal swallowed the click. Say what it is showing now, rather than reporting only that no
            // request arrived within thirty seconds.
            throw new InvalidOperationException(
                $"The kiosk never issued the {method} for '{what}'. The modal is still showing: "
                + await DescribeModalStateAsync(),
                ex);
        }

        await AssertWriteSucceededAsync(response, what);
        await EventModal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await WaitForCalendarVisibleAsync();
    }

    /// <summary>
    /// Refuses to click a Save the modal has disabled.
    /// <para>
    /// The modal gates Save on its own validation — an unselected calendar, a repeat that has been switched
    /// on without a frequency — and a disabled button swallows the click silently. Without this the symptom
    /// arrives much later and somewhere else: a thirty-second wait for a request that was never going to be
    /// made, or an assertion about an event that was never created. Checking first turns that into one
    /// sentence naming the control that is incomplete.
    /// </para>
    /// </summary>
    private async Task AssertSaveIsNotBlockedAsync(string what)
    {
        if (await SaveButton.IsEnabledAsync())
        {
            return;
        }

        throw new InvalidOperationException(
            $"The kiosk will not let the suite {what}: Save is disabled. {await DescribeModalStateAsync()}");
    }

    /// <summary>
    /// What the modal is currently complaining about, in one line. Best-effort — a diagnostic that threw
    /// would replace the real failure with a worse one.
    /// </summary>
    private async Task<string> DescribeModalStateAsync()
    {
        var parts = new List<string>();

        try
        {
            foreach (var tab in new[] { "details", "repeat" })
            {
                if (await EventModal.GetByTestId($"event-modal-tab-{tab}-incomplete").CountAsync() > 0)
                {
                    parts.Add($"the {tab} tab is marked incomplete");
                }
            }

            var hint = EventModal.GetByTestId("event-save-hint");
            if (await hint.CountAsync() > 0)
            {
                parts.Add($"save hint: '{(await hint.InnerTextAsync()).Trim()}'");
            }

            var error = EventModal.GetByTestId("event-modal-error");
            if (await error.CountAsync() > 0)
            {
                parts.Add($"error banner: '{(await error.InnerTextAsync()).Trim()}'");
            }

            var activeChips = await EventModal.Locator(".chip-active").CountAsync();
            parts.Add($"{activeChips} calendar chip(s) selected");
        }
        catch (PlaywrightException ex)
        {
            parts.Add($"(modal state could not be read: {ex.Message})");
        }

        return parts.Count == 0 ? "no validation markers are showing." : string.Join("; ", parts) + ".";
    }

    /// <summary>
    /// Answers the recurrence scope prompt.
    /// <para>
    /// With no <paramref name="scope"/> the prompt is accepted at its default ("all events") if one is
    /// showing, and its absence is fine — a single event is never asked.
    /// </para>
    /// <para>
    /// With a scope, the prompt <b>must</b> appear: the scope is the thing the scenario is testing, and
    /// an edit that was silently applied as a single event would otherwise be reported much later as a
    /// wrong occurrence set with no hint of why. Each control is waited for before the next is clicked,
    /// and the pill's own pressed state is confirmed, because Save → pill → OK is three renders and
    /// clicking ahead of one of them loses the click.
    /// </para>
    /// </summary>
    private async Task ConfirmScopePromptAsync(SmokeRecurrenceScope? scope, string what)
    {
        if (scope is null)
        {
            if (await ScopePrompt.IsVisibleAsync())
            {
                await ScopePromptConfirm.ClickAsync();
            }

            return;
        }

        try
        {
            await ScopePrompt.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = configuration.DefaultTimeoutMs
            });
        }
        catch (PlaywrightException ex)
        {
            throw new InvalidOperationException(
                $"The kiosk never asked which occurrences to change while trying to {what}, so the "
                + "scope this scenario is about was never chosen. Either the event the suite opened is "
                + "not part of a series, or the prompt did not appear. "
                + await DescribeModalStateAsync(),
                ex);
        }

        var pill = Page.GetByTestId($"recurrence-scope-{ScopeTestIdSuffix(scope.Value)}");
        await pill.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await pill.ClickAsync();
        await Assertions.Expect(pill).ToHaveAttributeAsync("aria-pressed", "true");

        await ScopePromptConfirm.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ScopePromptConfirm.ClickAsync();
    }

    private static string ScopeTestIdSuffix(SmokeRecurrenceScope scope) => scope switch
    {
        SmokeRecurrenceScope.ThisEvent => "this",
        SmokeRecurrenceScope.ThisAndFollowing => "following",
        SmokeRecurrenceScope.AllEvents => "all",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Not a recurrence scope.")
    };

    private static async Task AssertWriteSucceededAsync(IResponse response, string what)
    {
        if (response.Status >= 400)
        {
            throw new InvalidOperationException(
                $"The kiosk refused to {what}: the API answered HTTP {response.Status} to "
                + $"{response.Request.Method} {response.Url}. The modal stays open on an error, so "
                + "without this the symptom would be an opaque timeout.");
        }
    }
}
