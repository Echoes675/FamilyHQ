# E2E Testing Maintenance Guide

This document provides comprehensive guidance for maintaining and extending the FamilyHQ end-to-end (E2E) test suite.

## Table of Contents

1. [Running the E2E Tests](#running-the-e2e-tests)
2. [Test Structure](#test-structure)
3. [Adding New Tests](#adding-new-tests)
4. [Current Test Coverage](#current-test-coverage)
5. [Maintenance Checklist](#maintenance-checklist)

---

## Running the E2E Tests

### Prerequisites

Before running E2E tests, ensure the following are installed:

- **.NET 10 SDK** - Required to build and run the test projects
- **Playwright Browsers** - Chromium browser must be installed for Playwright

To install Playwright browsers, run:
```bash
cd tests-e2e/FamilyHQ.E2E.Common
dotnet playwright install chromium
```

### Required Services

> **Fastest path:** `pwsh scripts/dev-stack.ps1 up` stands up all three services plus the
> database, and `pwsh scripts/dev-stack.ps1 e2e` runs this suite against them. See
> `.agent/docs/local-stack.md`. The manual three-terminal steps below remain valid.

The E2E tests require three services to be running simultaneously:

| Service | Port | Description |
|---------|------|-------------|
| WebApi | 5000/5001 | ASP.NET Core backend API |
| WebUi | 7154 | The Blazor WASM frontend application |
| Simulator | 7199 | Test data simulator API |

#### Starting the Services

1. **Start the WebApi**:
   ```bash
   cd src/FamilyHQ.WebApi
   dotnet run
   ```

2. **Start the Simulator API** (in a new terminal):
   ```bash
   cd tools/FamilyHQ.Simulator
   dotnet run
   ```

3. **Start the WebUI** (in a new terminal):
   ```bash
   cd src/FamilyHQ.WebUi
   dotnet run
   ```

### Running the Tests

Run all E2E tests from the Features project:

```bash
cd tests-e2e/FamilyHQ.E2E.Features
dotnet test
```

To run tests with verbose output:

```bash
dotnet test --logger "console;verbosity=detailed"
```

To run a specific scenario, filter on the Reqnroll-generated method name — the scenario
title in PascalCase with punctuation removed (e.g. "View upcoming events on the dashboard
month view" generates `ViewUpcomingEventsOnTheDashboardMonthView`):

```bash
dotnet test --filter "FullyQualifiedName~ViewUpcomingEventsOnTheDashboardMonthView"
```

Do not use `--filter "Scenario=<title>"` — Reqnroll does not expose the human scenario
title as a test trait, so a title-based filter resolves to zero tests.

---

## Test Structure

The E2E test suite follows a **4-project structure** that separates concerns and promotes maintainability:

```
tests-e2e/
├── FamilyHQ.E2E.Common/          # Shared utilities and page objects
├── FamilyHQ.E2E.Data/            # Test data and API clients
├── FamilyHQ.E2E.Steps/           # Step definitions (BDD glue code)
└── FamilyHQ.E2E.Features/        # Feature files (Gherkin scenarios)
```

### Project Responsibilities

#### FamilyHQ.E2E.Common
Contains shared infrastructure used across all test projects:

- **Pages/** - Page Object Model classes
- **Hooks/** - Playwright driver initialization
- **Configuration/** - Test configuration loading

Key files:
- [`Pages/BasePage.cs`](tests-e2e/FamilyHQ.E2E.Common/Pages/BasePage.cs) - Base class for all page objects
- [`Pages/DashboardPage.cs`](tests-e2e/FamilyHQ.E2E.Common/Pages/DashboardPage.cs) - Dashboard page object
- [`Hooks/PlaywrightDriver.cs`](tests-e2e/FamilyHQ.E2E.Common/Hooks/PlaywrightDriver.cs) - Browser initialization
- [`Configuration/TestConfiguration.cs`](tests-e2e/FamilyHQ.E2E.Common/Configuration/TestConfiguration.cs) - Test settings

#### FamilyHQ.E2E.Data
Handles test data management and API communication:

- **Templates/** - User template JSON files
- **Models/** - Data models for simulator API
- **Api/** - HTTP client for Simulator API

Key files:
- [`Templates/user_templates.json`](tests-e2e/FamilyHQ.E2E.Data/Templates/user_templates.json) - User profile templates
- [`Api/SimulatorApiClient.cs`](tests-e2e/FamilyHQ.E2E.Data/Api/SimulatorApiClient.cs) - API client

#### FamilyHQ.E2E.Steps
Contains Reqnroll step definitions that connect Gherkin scenarios to code:

- **Steps/** - Step definition classes with [Binding] attributes
- **Hooks/** - Reqnroll hooks for setup/teardown

Key files:
- [`DashboardSteps.cs`](tests-e2e/FamilyHQ.E2E.Steps/DashboardSteps.cs) - Dashboard navigation, event display assertions
- [`EventSteps.cs`](tests-e2e/FamilyHQ.E2E.Steps/EventSteps.cs) - Seeding events into the simulator before a scenario
- [`UserSteps.cs`](tests-e2e/FamilyHQ.E2E.Steps/UserSteps.cs) - User provisioning and login via OAuth flow
- [`AuthenticationSteps.cs`](tests-e2e/FamilyHQ.E2E.Steps/AuthenticationSteps.cs) - Sign-in / sign-out assertions
- [`WebhookDataSteps.cs`](tests-e2e/FamilyHQ.E2E.Steps/WebhookDataSteps.cs) - Backdoor event mutations and webhook trigger for sync scenarios
- [`Hooks/MasterHooks.cs`](tests-e2e/FamilyHQ.E2E.Steps/Hooks/MasterHooks.cs) - Per-scenario browser setup/teardown
- [`Hooks/TemplateHooks.cs`](tests-e2e/FamilyHQ.E2E.Steps/Hooks/TemplateHooks.cs) - Loads `user_templates.json` once before the test run

#### FamilyHQ.E2E.Features
Contains Gherkin feature files written in natural language:

Key files:
- [`Dashboard.feature`](tests-e2e/FamilyHQ.E2E.Features/Dashboard.feature) - Dashboard calendar viewer scenarios (CRUD, multi-calendar, navigation)
- [`Authentication.feature`](tests-e2e/FamilyHQ.E2E.Features/Authentication.feature) - Sign-in and sign-out scenarios
- [`GoogleCalendarSync.feature`](tests-e2e/FamilyHQ.E2E.Features/GoogleCalendarSync.feature) - Webhook-triggered sync and live SignalR update scenarios

### Page Object Model Pattern

The tests use the **Page Object Model (POM)** pattern to encapsulate UI interactions:

```csharp
public class DashboardPage : BasePage
{
    private ILocator AddEventBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Add Event" });
    
    public async Task CreateEventAsync(string title)
    {
        await AddEventBtn.ClickAsync();
        // ... modal interaction
    }
}
```

Benefits:
- **Encapsulation** - UI details are hidden behind method calls
- **Maintainability** - UI changes only require updates in one place
- **Reusability** - Page methods can be used across multiple scenarios

### BDD/Reqnroll Framework

The tests use **Reqnroll** (a BDD framework compatible with SpecFlow) to write tests in natural language:

- **Feature files** - Human-readable scenarios in Gherkin syntax
- **Step definitions** - C# methods that implement each Gherkin step
- **Bindings** - Connect steps to methods using `[Binding]` attributes

Example Gherkin:
```gherkin
Scenario: Create a new event
  Given I view the dashboard
  When I create an event "Dentist Appointment"
  Then I see the event "Dentist Appointment" displayed on the calendar
```

---

## Adding New Tests

### Adding New User Templates

User templates define the calendars available to a test user. Each template key is used in the `Given I have a user like "..."` step. Edit [`user_templates.json`](tests-e2e/FamilyHQ.E2E.Data/Templates/user_templates.json):

```json
{
    "NewUserType": {
        "Calendars": [
            {
                "Id": "template_work",
                "Summary": "Work Calendar",
                "BackgroundColor": "#ea4335"
            },
            {
                "Id": "template_personal",
                "Summary": "Personal Calendar",
                "BackgroundColor": "#34a853"
            }
        ],
        "Events": []
    }
}
```

Each scenario gets a **unique isolated copy** of the template at runtime (unique username, new calendar IDs). Pre-seeded events are added via the `EventSteps` (`Given the user has an all-day event...`) rather than in the template's `Events` array.

Then use the template in a scenario:
```gherkin
Given I have a user like "NewUserType" with calendar "Work Calendar"
```

### Adding New Feature Scenarios

Add new scenarios to an existing `.feature` file or create a new one:

```gherkin
Scenario: New scenario description
  Given I have a user like "Test Family Member" with calendar "Family Events"
  And the user has an all-day event "New Event" tomorrow
  When I view the dashboard
  Then I see the event "New Event" displayed on the calendar
```

#### Which view a scenario starts on

A page load lands on the **Reminders timeline**, not the month grid — that is the kiosk's home view.
Two shared steps put a scenario back on the grid, which is where everything written before that
change assumed it starts:

- `I view the dashboard` — navigates and then selects the Month tab.
- `I login as the user "…"` — ends on the Month tab, so a Background that only logs in still leaves
  the grid (and its **Add Event** button, which the Reminders view does not have) on screen.

Both go through `DashboardPage.ShowCalendarGridAsync()`. A step that navigates to `/` itself must
call it too if what follows reads the grid or its Add Event button — `SyncResilienceSteps` has two
such places.

A scenario **about** the landing view must not come through either of those: use
`When the kiosk loads the dashboard` (`DashboardPage.LoadKioskHomeViewAsync()`), which taps nothing
and waits for the upcoming-reminders response as well as the events one.

Waits for "the dashboard is up" use `DashboardPage.AnyCalendarView` — any one of the four view
containers. Do not reintroduce a wait on `.month-table` alone for that purpose: it turns "the
dashboard loaded" into a claim about which view the kiosk opens on.

An idle kiosk returns to the home view after 15 minutes, the same threshold that pulls a stale date
back to today, so **any** scenario that forces idle and runs the idle check ends up on the Reminders
timeline unless a modal is open. A `Then` that reads another view's header after an idle check is
asserting against a view that is no longer on screen. `DayRollover.feature` is the worked example:
it re-selects the view under test with a tab step after the idle check, and two things come with
that.

- The tap is a real interaction, so it re-stamps `idle.js`'s monotonic clock. That is what stops the
  30-second poll from sending the kiosk home again mid-assertion — do **not** re-force idle after it.
- Re-selecting **Month or Agenda** costs the assertion nothing: those tabs do not touch the
  displayed month, so the label still reads what the snap put there. Re-selecting **Day** does,
  because tapping the Day View tab without a date opens on today by design
  (`Index.SwitchToView`) — so any "the Day view shows &lt;date&gt;" assertion after an idle check is a
  statement about the clock rather than about the snap. A Day-view idle scenario therefore asserts
  the kiosk going home as its own `Then`: that is the half which can still fail for the reason the
  scenario exists.

### Adding New Step Definitions

When a new step doesn't match existing step definitions, add a new method in the appropriate Steps class:

```csharp
[Given(@"the user has a recurring event ""([^""]*)"" every Monday")]
public async Task GivenTheUserHasARecurringEventEveryMonday(string eventName)
{
    // Implementation here
    var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
    // ... create recurring event
    await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
}
```

### Extending Page Objects

Add new methods to existing page objects or create new page object classes:

```csharp
// In DashboardPage.cs or a new page class
public async Task FilterByCalendarAsync(string calendarName)
{
    var filterDropdown = Page.GetByRole(AriaRole.Combobox, new() { Name = "Filter by calendar" });
    await filterDropdown.SelectOptionAsync(new[] { calendarName });
}
```

---

## Parallelism

E2E parallelism is controlled by a single knob — `maxParallelThreads` in
`tests-e2e/FamilyHQ.E2E.Features/xunit.runner.json`. Granularity is **per feature
file**: Reqnroll generates one xUnit test collection per `.feature`, and collections
run in parallel up to this cap; scenarios *within* a file run serially. Each scenario
launches its own Chromium (no browser reuse), so the thread count is effectively the
number of concurrent browsers.

**Current value: `6`** (raised from `2` in FHQ-147). The CI/test host runs the
browsers, the deployed dev app, Postgres and the Simulator together on one
4-core / 8-thread box, so the ceiling is CPU, not RAM. A sweep on that host measured
the Deploy-Dev E2E execution wall-clock:

| maxParallelThreads | E2E wall-clock |
|---|---|
| 2 (old) | ~14:05 |
| 4 | 10:13 |
| **6** | **08:26** |
| 8 | 09:25 (regressed — CPU oversubscription) |

6 takes ~40% off the baseline; 8 is *slower*, so the optimum is hardware-bound.
**Do not raise this without re-measuring on the same host.** Metric = xUnit
`Finished` − `Starting` delta from the Deploy-Dev "E2E Tests" stage log
(parallelism-aware, excludes fixed build/install overhead). Full data in the FHQ-147 ticket.

---

## Current Test Coverage

The suite currently contains **23 feature files (~180 scenarios / 182 executed tests)**
under `tests-e2e/FamilyHQ.E2E.Features/WebUi/`. The per-file tables below are a
historical snapshot of the earliest features and are **not exhaustive** — treat the
`WebUi/` directory as the source of truth for current coverage.

### Dashboard.feature (15 scenarios)

Has a `Background` that provisions a `TestFamilyMember` user and logs in before each scenario. Scenarios that need a different user provision their own user on top of the Background.

| Scenario | Category |
|----------|----------|
| View upcoming events on the dashboard month view | Display |
| Create a new event | CRUD |
| Update an existing event | CRUD |
| Delete an existing event | CRUD |
| View events from multiple calendars | Multi-Calendar |
| View all-day events | Event Types |
| View timed events | Event Types |
| View event details | Interaction |
| Update event after changing its calendar | Multi-Calendar / CRUD |
| Delete event after changing its calendar | Multi-Calendar / CRUD |
| Navigate to next month | Navigation |
| Create event in two calendars appears twice on grid | Multi-Calendar |
| Add calendar to existing event via chip | Multi-Calendar |
| Remove calendar chip from event | Multi-Calendar |
| Last chip is protected — cannot remove final calendar | Multi-Calendar |
| Delete event removes it from all calendars | Multi-Calendar / CRUD |
| Create modal requires an explicit calendar and never silently assigns the shared calendar | Multi-Calendar / CRUD (FHQ-32) |

### Authentication.feature (5 scenarios)

| Scenario | Category |
|----------|----------|
| User sees sign-in button when not authenticated | Auth |
| User can sign in and see their username | Auth |
| User can sign out and return to sign-in screen | Auth |
| Calendar is hidden when not authenticated | Auth |
| Calendar is visible when authenticated | Auth |

### GoogleCalendarSync.feature (6 scenarios)

Tests the full webhook → sync → UI update pipeline using the Simulator's backdoor API to mutate events and the `/api/sync/webhook` endpoint to trigger a sync.

| Scenario | Category |
|----------|----------|
| New event added in Google Calendar appears on dashboard after sync | Webhook Sync |
| Event updated in Google Calendar shows new title after sync | Webhook Sync |
| Event deleted in Google Calendar disappears after sync | Webhook Sync |
| New event added in Google Calendar appears live on open dashboard | Live Update (SignalR) |
| Event updated in Google Calendar shows live on open dashboard | Live Update (SignalR) |
| Event deleted in Google Calendar disappears live from open dashboard | Live Update (SignalR) |

### Recurring events (FHQ-18.11) — 6 feature files under `WebUi/DashboardCalendarViewer/`

`RecurringEventsDisplay`, `RecurringEventsCreate`, `RecurringEventsEdit`, `RecurringEventsDelete`, `RecurringEventsMembers`, `RecurringEventsEchoGuard` — cover ingest + ↻ indicator/subtitle, native create + custom weekday + toggle-off, the three edit scopes (incl. what an all-events rename does to an existing exception), the three delete scopes, multi-member series + 1↔N migration + non-All member refusal, and the self-echo guard (one outbound write per recurring write).

**Simulator recurrence emulation** (`tools/FamilyHQ.Simulator`): the Simulator emulates Google's recurring model — a seeded master with an RRULE expands into instances for `events.list?singleEvents=true` (compound ids `{master}_{stamp}`, `recurringEventId`, content-hash), `events.get(master)` returns the `recurrence` array, and `events.insert/patch/delete` honour recurrence (toggle on/off, instance exception overrides, instance cancellations). Renaming a master carries the new title onto that series' exceptions, because that is what real Google does — see "A series rename overwrites its exceptions' titles" in `simulator-external-dependencies.md` before expecting an override to keep its own title. Expansion is bounded to a `now-2mo..now+12mo` horizon when the caller omits `timeMin/timeMax` (the app's incremental sync does) — an unbounded series would otherwise expand to the engine cap and hang the sync.

**Gotchas learned the hard way (heed for any recurring/E2E work):**
- **Reqnroll keyword matching is type-sensitive** — a `[When]` binding does NOT match a `Given` step; use the binding's keyword or `[StepDefinition]`.
- **Never assert event counts in the Month grid** — it only renders a 6-week window; navigate the Day view to specific occurrence dates and assert per-date.
- **`RecurrenceRuleBuilder.Describe` appends end clauses** (", N times" / ", until …") — assert the subtitle CONTAINS the pattern.
- **Any new `SimulatedEvent` column needs a Simulator EF migration** (the Simulator `Migrate()`s on startup).
- **Scope every between-scenario backdoor reset to the scenario's isolated user** — a global reset races concurrent scenarios (see Intermittent Issues #3 and #5).

### Event reminders — `EventReminders.feature` (12 scenarios, 17 tests)

An event's reminders through the modal's Reminders tab: add, edit and remove; every transition between
the three states Google distinguishes — follows-the-calendar-default, explicit, explicitly-none — in
both directions; the tab badge; the All-day reset; the day-of-event floor; and the series scope
warning. One six-row `Scenario Outline` carries the transitions, which is why 12 scenarios are 17
tests.

One scenario is about the screen rather than the save. The Add form describes a reminder from the
moment the tab opens — nobody has to touch it — and that description used to sit on a line of its own
in the same voice as the note above it saying the event had none, so a family read it as a reminder
that existed. It is now part of the Add button's own label, and
*"An event taken off its calendar's reminders without adding one is left with none, and says so"*
asserts both halves: the empty-state warning still showing (it is true and must not be suppressed to
make the contradiction go away) and the description reading as an offer inside the button. Its last
step pins the save down as unchanged — turning the toggle off **is** a request for no reminders.

**A new assertion idiom lives here: assert what the save SENT, not what the screen shows.**
`EventWriteRecorder` (`FamilyHQ.E2E.Common/Helpers`) attaches to the scenario's own page and records
the `POST`/`PUT`/`DELETE` requests the browser makes to `/api/events`, body included. It exists
because no UI assertion can do this job: **a save that sends back the reminders it opened with and a
save that says nothing about them leave exactly the same screen behind** — and only the second leaves
a set made in the Google Calendar app on a phone alone. The request body is the one place the
difference is observable from outside the app. Scenario names of the form *"… says nothing about its
reminders"* are all of this kind.

Reach for the recorder whenever the property under test is the **absence** of a field in a write.
The recorder is attached per scenario by `EventWriteHooks`, so nothing leaks between scenarios under
the parallel runner.

**Comparing one write against another:** `EventWrite.Field(name)` reads any property as JSON text.
Compare the create's own body against the update's rather than against values written into the test —
that proves the update agrees with what the event was actually created as, not merely with the test's
expectation. Two rules when you do:

- **Require the field on the source side.** A null on both sides passes while proving nothing. The
  reminder-only-edit scenario types a note into the description for exactly this reason: it gives the
  one nullable field in its list a real value.
- **Compare timestamps as instants, never as text.** The data layer converts every `DateTimeOffset`
  to UTC on the way into PostgreSQL, so a value read back carries a zero offset where the create
  carried the browser's. The strings differ while describing the same moment. A text comparison goes
  red for a reason unrelated to the edit, and the tempting "fix" is to delete the assertion.

**What this feature file cannot prove, and where that lives.** The Simulator does not model Google's
`PUT` clearing unmapped fields, so "a field FamilyHQ never models survives a write" is not provable
here — a field the Simulator never stored cannot be observed to survive. An event's colour is the
clearest case. That half is asserted against real Google in the preprod smoke suite; see
`testing-strategy.md` for why a twin that asserted it here would be worse than no twin.

**Gotchas specific to the Reminders tab:**
- **Address a reminder row by its `data-reminder-method` and `data-reminder-minutes`, never by
  position.** Google returns an event's overrides in an order of its own, and the picker sorts for
  display, so position identifies a different reminder than you meant.
- **Choose the calendar chips BEFORE touching the tab.** Which calendar an event lands on decides
  whose default reminders the tab shows and copies in, and the modal re-reads them into an untouched
  tab — so a later chip change replaces what was just set.
- **Switch inheritance off before adding.** The Add form is not offered while the event follows the
  calendar, because Google rejects a write asking for the defaults and for specific reminders at once.
- **Switching inheritance off copies the calendar's defaults in as editable rows**, as the Google app
  pre-fills them. An event created that way carries those AND anything added afterwards — remove them
  first if a scenario needs an exact set.
- **The numeric amount commits on the DOM `change` event**, which a `Fill` alone does not raise. Blur
  it (`Tab`) and assert the value took before pressing Add — the same dance the recurrence interval
  needs.

### Reminders view — `RemindersView.feature` (16 scenarios)

The fourth dashboard tab's standalone timeline of events whose reminders were set **on the event** —
one row per EVENT, filed by when the event itself starts — as distinct from the event modal's own
Reminders tab above (which edits one event's reminders). Covers: the month grid still being reachable
from the timeline and vice versa (which tab comes first, and which view a load lands on, belong to
`KioskHomeView.feature` instead); a row under Today leading with
its event's start time and nothing else;
a row in a section covering several days leading with the day and date as well; an event following
its calendar's usual reminders never appearing; an explicitly-removed reminder and an event
inheriting from a calendar with no defaults both never appearing; an event whose last reminder has
already fired keeping its row; an event with widely-spaced reminders (one soon, one over two weeks
out) appearing exactly once, under its own start and in no other section; a shared event's row naming
the people it is shared with rather than the shared calendar; tomorrow's event staying under Tomorrow
rather than This week; an empty section collapsing to one line; the fully-empty view stating it
plainly; both permanent footnotes (all-day, and the one admitting the inherited exclusion); tapping a
row opening the **Day view** on that event's own day; and leaving the tab for another view, which
exercises the enter/leave path a closed tick-loop race guards but which no other scenario (and no
bUnit, which this repo does not have) touches.

**A row describes the event, not its reminders.** It reads `[when] · title · [who]`; the bell glyph
and the `1 reminder · next 45 min before` line were removed at the family's request, and the DTO
fields that fed them went with them rather than being left unrendered. So there is nothing on a row
for a scenario to assert about a reminder's count, method or lead time, and nothing for a reminder
firing to change — which is why the already-fired scenario now asserts only that the row is *there*.
Its subject survived the change; only its wording assertion did not. What makes this a reminders view
is the filter, so the scenarios that matter most here are the absence ones.

**`[when]` is two different things, and both halves need a scenario.** Today and Tomorrow show the
time alone — their headings already say which day they are — and This week, This month and Next month
lead with `ddd d MMM · HH:mm`. The Today assertion is an **equality** on the leading column rather
than a `Contain`, because the failure it guards is a date appearing where one is not wanted, and only
equality catches that. The further-out assertion resolves its expected date through
`DateExpressionResolver`, the same single source of truth the seeding step used, so the two cannot
disagree about what "next month" means.

**The tap scenario has to navigate BACK to the current month, and that step is the scenario.** Its
careful case is an event *outside* the month the dashboard has loaded, because that is what exercises
`SwitchToView`'s month fetch rather than just the view switch. But setting a reminder on a next-month
event requires navigating forward to edit it, which loads that month — so the scenario that claimed
this case before was not actually taking it. `I navigate back to the current month` after the save is
what makes it real; removing it leaves a scenario that still passes and no longer tests the thing it
names. The `Then` asserts the event's own **tile**, not that a Day view appeared: `DayView` renders
only the events filed under the day it is showing, so a visible container alone would pass for the
wrong day.

**There is no default-tag coverage any more, because there is no default tag.** The view lists only
events whose reminders were set on the event itself, so an inheriting event produces no row to label
— the DTO, the view model, the pill and the row's `data-is-default` attribute all went with it. What
replaced that scenario asserts the inheriting event is *absent*, and the footnote scenario asserts
the view says so. Both halves are the requirement: the exclusion is known to under-report what Google
will do, and was accepted only on condition the view admits it, so a reworded or removed footnote is
as much a failure as a missing exclusion.

**An absence scenario needs a control event in the same view.** "An event following its calendar's
usual reminders never appears" seeds a second event with a reminder of its own and asserts *that* one
is listed. Without it the scenario would pass just as well if the sync never ran or the tab rendered
empty — it would stop being able to fail for the reason it exists for. The same shape is worth
copying for any new negative assertion here.

**The exclusion does not hide all-day events, and must not be "fixed" to spare them.** An all-day
event *as Google creates it* does not inherit: Google materialises the calendar's defaults onto it,
so a birthday or bin-day event arrives carrying explicit overrides and the uniform filter never sees
it. (What Google returns for the revert-to-default body the kiosk's own All-day toggle sends is
recorded rather than known — preprod smoke RM4.) That property is pinned at the unit level
(`RemindersControllerTests.UpcomingReminders_StillListsAnAllDayEventCarryingTheMaterialisedCalendarDefaults`)
rather than here, because the Simulator is a test double for Google's create response and a green
scenario would not be evidence about what Google actually sends. The other half is pinned against
**real Google** by preprod smoke **RM5**, and the two halves are not interchangeable: the unit test
fixes how FamilyHQ treats an event arriving in that shape, while RM5 is the only check in the repo
that Google still produces that shape at all. Neither the unit test nor any E2E twin can tell you
that, which is why a change here should keep the RM5 pointer with it.

**Address a row by its `data-event-id`, never by position.** The view sorts by event start, which
depends on seeding order rather than on anything Google guarantees, so a row's position is not a
stable identifier — the same rule, and the same reason, as the modal's Reminders tab rows. One row
per event means the id alone identifies it. `DashboardPage.ReadReminderRowsAsync` reads a section's
rows as `ReminderRowSnapshot`s carrying that id and the row's text for assertions to key off; the
row's own start time is read separately, off the leading column, so that an assertion about it cannot
be satisfied by some other time rendered elsewhere in the row.

**A "today" scenario cannot run close to local midnight.** A row is filed by its event's start, so an
event seeded N minutes from now files under Tomorrow once "now" is within N minutes of midnight,
while the scenario still asserts Today. `GivenTheUserHasATimedEventStartingInMinutesInCalendar`
refuses that outright rather than letting the run fail as a missing row, so a failure there is a
statement about the clock, not about the view.

**The default view must stay Month.** `RemindersView.feature` never asserts this itself — the
~180 scenarios elsewhere that assume Month loads on login are the ones that would catch a regression
— but every scenario here still switches to the Reminders tab explicitly rather than relying on it
ever being the landing view, and one scenario asserts Month is what is showing before the tab is
touched at all. If adding this tab ever changes what an existing scenario sees, that is a sign the
tab was wired up wrongly, not a reason to amend the older scenario.

**Seeding order matters, and this feature file has no `Background` because of it.** A backdoor-seeded
event (`the user has a timed event "…" at "…" on "…" in "…"`) has to exist *before* the login that
triggers the first sync, exactly as the recurring-events and multi-calendar features already do it.
A calendar's default reminders (`the active calendar's usual reminders in Google are N minutes`) have
to be set *after* any backdoor seed but still *before* that same login, because seeding re-posts the
whole user template and the Simulator rebuilds the calendar rows from it — see
`CalendarDefaultRemindersSteps`. A shared `Background` would have forced one ordering on every
scenario; several of these need backdoor seeds and several do not, so each scenario spells out its
own setup instead.

**The last-day-of-week boundary is proven at the unit level, not here.** "Tomorrow's event stays
under Tomorrow on the last day of a week" cannot force the server's real clock onto that boundary day — unlike
the client-only kiosk day-rollover hook, nothing in this stack can move the WebApi's `TimeProvider`.
The scenario instead proves the general rule (Tomorrow is matched before This week, on every day),
which also covers the boundary on the roughly one run in seven that happens to land on it. The
deterministic edge itself is pinned with a controlled `DateOnly` in
`ReminderBucketingTests.File_OnTheLastDayOfAWeek_TomorrowIsStillTomorrowAndNotNextMonth`.

### Kiosk home view — `KioskHomeView.feature` (4 scenarios)

The view a page load lands on, the tab order, and the return an idle kiosk makes on its own. Covers:
the tabs reading **Reminders · Month View · Agenda · Day View** left to right; a load opening on the
timeline with a seeded row already filed under Today; a kiosk left on the Month view coming back to
the timeline after 16 forced minutes of idle; and an open create-event modal keeping it where it is.

**The row, not the panel, is the landing assertion.** An empty timeline is also what a view that
never fetched looks like — `RemindersView` renders "No reminders coming up" before the first fetch
resolves — so a scenario asserting only that the panel appeared would pass on a landing that picked
the view and skipped `EnterRemindersViewAsync`, which is the exact failure the feature risks. There
is no backdoor that seeds a reminder (the Simulator's event model carries no overrides), so the
reminder is set through the modal, which is why the scenario needs a login and the grid first.

**The tick running is not asserted here, and cannot be.** It fires once a minute; nothing a scenario
can watch inside its own runtime distinguishes a started loop from a stopped one. What covers it is
that the landing path and a tab tap share one entry point (`EnterRemindersViewAsync`), plus
`ReminderTickLoopTests` at the unit level.

### Test Categories

1. **Display** - Events render correctly on the calendar grid
2. **CRUD** - Create, Update, Delete operations through the UI
3. **Multi-Calendar** - Chip selector, per-calendar capsule rendering, last-chip protection
4. **Event Types** - All-day vs timed event display
5. **Navigation** - Month navigation
6. **Auth** - Sign-in / sign-out flows
7. **Webhook Sync** - Events added/updated/deleted externally appear after a webhook sync
8. **Live Update** - SignalR pushes cause the open dashboard to refresh without navigation
9. **Reminders** - The three reminder states, the all-day form, and what a save does and does not say
10. **Request-shape** - Assertions on the body the kiosk SENT, for properties no screen can show

---

## Maintenance Checklist

### When to Update Tests

Update E2E tests in these scenarios:

- **UI Changes** - When the dashboard UI is modified (buttons moved, classes changed)
- **New Features** - When new functionality is added to the dashboard
- **Bug Fixes** - When a bug is fixed, add a regression test
- **API Changes** - When the Simulator API contract changes

### Handling Test Failures

1. **Analyze the Failure**
   - Check if it's a genuine regression or a flaky test
   - Review Playwright's detailed error messages
   - Look at screenshots captured on failure (if configured)

2. **Common Issues**
   - **Timeout errors** - Increase timeout in `TestConfiguration.cs`
   - **Locator not found** - UI may have changed; update locators
   - **Service unavailable** - Ensure WebApi, WebUI, and Simulator are running

3. **Fix the Test**
   - Update locators if UI changed
   - Add waits if timing is an issue
   - Update step definitions for new functionality

### Debugging Tips

1. **Run in Headed Mode**
   ```bash
   # Set Headless = false in TestConfiguration or via environment variable
   ```

2. **Use Playwright Inspector**
   ```bash
   cd tests-e2e/FamilyHQ.E2E.Common
   dotnet playwright codegen
   ```

3. **Add Debug Output**
   ```csharp
   // Take screenshot on failure
   await Page.ScreenshotAsync(new() { Path = "failure.png" });
   ```

4. **Isolate Failing Tests**
   ```bash
   dotnet test --filter "FullyQualifiedName~DashboardSteps"
   ```

5. **Check Logs**
   - Browser console logs in Playwright
   - WebUI application logs
   - Simulator API logs

### Best Practices

- **Keep tests independent** - Each scenario should work in isolation
- **Use meaningful names** - Step definitions should clearly describe actions
- **Avoid hardcoded waits** - Prefer explicit waits for elements
- **Maintain the page object pattern** - Don't expose Playwright locators in step definitions
- **Keep scenarios focused** - One scenario per behavior being tested
- **Update templates carefully** - User templates affect multiple scenarios
- **Never use hardcoded dates** - Use relative expressions (`"tomorrow"`, `"in N days"`, `"today"`) instead of absolute dates like `"2026-03-15"`. Hardcoded dates break when the calendar rolls past the target month. The `DateExpressionResolver` class in `FamilyHQ.E2E.Steps` converts these expressions to `yyyy-MM-dd` at runtime. All step definitions that accept date parameters already support both formats.
- **A relative seed date must be inside the view the assertion reads** - a date expression says *when*, never *where*, so resolving one does not put it on screen. The **agenda renders exactly one calendar month** and nothing either side, so a cell keyed on a date outside it (`agenda-cell-<date>-<calendarId>`) does not exist: the assertion waits out its full 30s for an element that can never appear, and a *negative* assertion (`I do not see …`) passes vacuously against the missing cell. Which month a relative date lands in is a property of the **run date**, not of the expression — `"tomorrow"` is next month on the last day of every month — so precede an agenda assertion keyed on a date with `And I navigate the agenda to show "<date expression>"`. `"today"` is the one expression that needs no step, because the agenda opens on today's month; every other one does, including `"tomorrow"`. The step resolves the expression and drives the agenda's own prev/next until the live month-year label spans it (`DashboardPage.ShowAgendaMonthContainingAsync`), and throws rather than asserting against the wrong month if it cannot get there. Contrast the **month grid**, which renders six weeks including the adjacent months' edge days and so tolerates a date a day or two outside the current month — that tolerance is why this only ever bites the agenda. Nine agenda scenarios failed on a 30 September run for exactly this reason; the ones that already carried a navigation step passed alongside them.
- **Never call `DateTime.Today` / `DateTime.Now` in E2E code** - use `BrowserClock` (`FamilyHQ.E2E.Common/Helpers/BrowserClock.cs`). It owns the single timezone the whole suite agrees on (`Europe/London`, pinned onto the Playwright context by `PlaywrightDriver`) and is what `DateExpressionResolver` resolves against. A bare `DateTime.Today` is the **test host's** date, which is not the browser's during the 23:00–00:00 UTC window in BST — seeding or asserting through it puts the test on a different calendar day from the app for one hour a night (intermittent-issues #11). `DateTime.UtcNow` for a polling deadline is fine; it is not a date.
- **Seed timed events as instants, dates as dates** - a timed seed must go on the wire through `BrowserClock.ToUtcInstant(...)` so it means the same wall-clock time regardless of the server's zone (a naive `DateTime` is silently stamped with the Simulator container's zone). All-day seeds stay naive midnight — the Simulator serialises them as `date`-only, and converting them would shift the date.
- **Create modal has no default calendar (FHQ-32)** - The "Add new event" modal no longer pre-selects a calendar; the user must pick one and Save is blocked until they do. `DashboardPage.FillAndSaveEventAsync` therefore selects the first available calendar chip when none is active, and `CreateEventInCalendarAsync` selects a named chip. Day/agenda slot taps that pass an explicit `calendarId` keep their chip pre-selected, so those flows are unaffected.
