# Webhook E2E Test Coverage — Design Spec

**Date:** 2026-03-22
**Branch:** test/webhook
**Scope:** E2E acceptance tests for Google Calendar webhook-driven sync scenarios

---

## Background

FamilyHQ syncs calendar events from Google Calendar via two paths:

1. **Manual / triggered sync** — `POST /api/sync/trigger`
2. **Webhook-driven sync** — Google calls `POST /api/sync/webhook`; the WebApi runs `SyncAllAsync` and notifies connected clients via SignalR

The existing E2E suite (`Dashboard.feature`) covers CRUD operations performed through the UI but has no coverage for the webhook sync path — i.e., changes made directly in Google Calendar that propagate to the dashboard automatically.

---

## Goal

Add a `GoogleCalendarSync.feature` file with 6 scenarios covering:

- **Eventual consistency** — webhook fires, user navigates to dashboard, sees updated state
- **Real-time SignalR push** — dashboard is already open, webhook fires, UI updates live without navigation

---

## Architecture

### 1. Simulator Back-Door Controller

New `BackdoorEventsController` in `tools/FamilyHQ.Simulator`, route prefix `api/simulator/backdoor/events`.

> **Note:** The existing `WebhookController` sits at `[Route("simulate/push")]` — outside the `api/simulator/` namespace. The new back-door controller follows the `api/simulator/` convention used by `SimulatorConfigController`. Do not prefix it with `simulate/` by analogy with `WebhookController`.

Unlike the existing `EventsController` (which mimics the Google Calendar API and reads the user from a Bearer token), this controller accepts `userId` directly in the request body or query string. This allows test code to mutate simulator state without constructing a fake OAuth token.

**Endpoints:**

| Method | Route | `userId` location | Purpose |
|--------|-------|------------------|---------|
| `POST` | `/api/simulator/backdoor/events` | Request body | Add a new event |
| `PUT` | `/api/simulator/backdoor/events/{eventId}` | Request body | Update an event's summary |
| `DELETE` | `/api/simulator/backdoor/events/{eventId}?userId={userId}` | Query string | Delete an event |

`POST` returns the created event's ID (string) in the response body so the scenario context can store it.

#### `BackdoorEventRequest` DTO fields

Used by both `POST` and `PUT` bodies:

| Field | Type | Required for POST | Required for PUT |
|-------|------|:-----------------:|:----------------:|
| `UserId` | `string` | ✓ | ✓ |
| `CalendarId` | `string` | ✓ | — |
| `Summary` | `string` | ✓ | ✓ |
| `Start` | `DateTime` | ✓ | — |
| `End` | `DateTime` | ✓ | — |
| `IsAllDay` | `bool` | ✓ | — |

### 2. SimulatorApiClient Extensions

Four new methods added to `tests-e2e/FamilyHQ.E2E.Data/Api/SimulatorApiClient.cs`:

```csharp
Task<string> AddEventAsync(string userId, string calendarId, string summary, DateTime start, DateTime end, bool isAllDay)
Task UpdateEventAsync(string userId, string eventId, string newSummary)
Task DeleteEventAsync(string userId, string eventId)
Task TriggerWebhookAsync()
```

- `AddEventAsync` posts to `POST /api/simulator/backdoor/events` and returns the created event's ID
- `UpdateEventAsync` puts to `PUT /api/simulator/backdoor/events/{eventId}` with `userId` and `newSummary` in the request body (`BackdoorEventRequest`)
- `DeleteEventAsync` calls `DELETE /api/simulator/backdoor/events/{eventId}?userId={userId}`
- `TriggerWebhookAsync` calls `POST /simulate/push` on the Simulator — the **already-existing** `WebhookController` then forwards the request to `POST /api/sync/webhook` on the WebApi, mirroring real Google push notification behaviour. The relative path passed to `_httpClient.PostAsync` must be `"simulate/push"` (no leading slash) to correctly resolve against the `BaseAddress` set in the constructor
- All methods follow the existing `EnsureSuccessStatusCode()` pattern

> **Known limitation:** `WebhookController` hardcodes the WebApi URL as `https://localhost:7196/api/sync/webhook`. This is a pre-existing constraint and is out of scope for this spec. If the environment changes, the URL must be updated in `WebhookController` or moved to Simulator `appsettings.json`.

### 3. WebhookDataSteps.cs

New step definitions file in `tests-e2e/FamilyHQ.E2E.Steps/`.

`WebhookDataSteps` constructs its own `DashboardPage` instance from `scenarioContext.Get<IPage>()`, matching the pattern used in `DashboardSteps` and `AuthenticationSteps`. `DashboardPage` is not registered in the BoDi container — it is always constructed manually.

**Data mutation steps** (call back-door Simulator endpoints):

```gherkin
When a new event "X" is added to Google Calendar
When the event "X" is updated to "Y" in Google Calendar
When the event "X" is deleted from Google Calendar
```

**Webhook trigger step:**

```gherkin
When Google Calendar sends a webhook notification
```

**Real-time assertion steps** (Playwright polling, no navigation):

```gherkin
Then the dashboard live-updates to show "X"
Then the dashboard live-updates to remove "X"
```

#### Step implementation notes

**`userId` and `calendarId` resolution:**
- `userId` is read from `ScenarioContext["UserTemplate"].UserName`
- `calendarId` for `AddEventAsync` is read from `ScenarioContext["CurrentCalendarId"]` (set by `UserSteps.GivenIHaveAUserLikeWithCalendar`)

**Event ID resolution for update/delete steps:**
- When the event was created by `AddEventAsync` (the add scenarios), the `When a new event "X" is added to Google Calendar` step implementation writes the returned ID into `ScenarioContext["LastCreatedEventId"]` immediately after the `AddEventAsync` call
- When the event was pre-seeded via `Given the user has an all-day event "X" tomorrow` (the update/delete scenarios), the step implementation resolves the ID by searching `ScenarioContext["UserTemplate"].Events` by `Summary == eventName`
- This means `EventSteps` steps store events in the template with their generated IDs, which is already the case

**Live-update polling:**
- The `Then the dashboard live-updates to show/remove "X"` steps poll `_dashboardPage.GetVisibleEventsAsync()` in a retry loop
- Timeout: 5 seconds. `TestConfiguration.DefaultTimeoutMs` exists but is set to 30 000 ms — do **not** use it here. Use a local constant `LiveUpdateTimeoutMs = 5000` within `WebhookDataSteps`
- Poll interval: 250ms
- Failure message: `"Dashboard did not live-update within 5s after webhook notification"`

**Navigation race note:** The eventual consistency scenarios re-navigate via `And I view the dashboard` after the webhook fires. `DashboardPage.NavigateAndWaitAsync` waits for `NetworkIdle` after `GotoAsync`, ensuring the `api/calendars/events` response has completed before assertions run. The SignalR push from the webhook is therefore irrelevant for eventual consistency scenarios — the full page reload always retrieves fresh data.

### 4. GoogleCalendarSync.feature

New feature file in `tests-e2e/FamilyHQ.E2E.Features/`.

No `Background` block is used because the real-time and eventual consistency scenarios differ in their flow after login (one keeps the dashboard open; the other re-navigates). Each scenario states its full setup explicitly.

```gherkin
Feature: Google Calendar Webhook Sync
  As a family member
  I want my dashboard to reflect changes made in Google Calendar
  So that I always see an up-to-date view of my schedule

  Scenario: New event added in Google Calendar appears on dashboard after sync
    Given I have a user like "TestFamilyMember" with calendar "Family Events"
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When a new event "Dentist Appointment" is added to Google Calendar
    And Google Calendar sends a webhook notification
    And I view the dashboard
    Then I see the event "Dentist Appointment" displayed on the calendar

  Scenario: Event updated in Google Calendar shows new title after sync
    Given I have a user like "TestFamilyMember" with calendar "Family Events"
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When the event "School Holiday" is updated to "School Holiday (Cancelled)" in Google Calendar
    And Google Calendar sends a webhook notification
    And I view the dashboard
    Then I see the event "School Holiday (Cancelled)" displayed on the calendar

  Scenario: Event deleted in Google Calendar disappears after sync
    Given I have a user like "TestFamilyMember" with calendar "Family Events"
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When the event "School Holiday" is deleted from Google Calendar
    And Google Calendar sends a webhook notification
    And I view the dashboard
    Then I do not see the event "School Holiday" displayed on the calendar

  Scenario: New event added in Google Calendar appears live on open dashboard
    Given I have a user like "TestFamilyMember" with calendar "Family Events"
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When a new event "Dentist Appointment" is added to Google Calendar
    And Google Calendar sends a webhook notification
    Then the dashboard live-updates to show "Dentist Appointment"

  Scenario: Event updated in Google Calendar shows live on open dashboard
    Given I have a user like "TestFamilyMember" with calendar "Family Events"
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When the event "School Holiday" is updated to "School Holiday (Cancelled)" in Google Calendar
    And Google Calendar sends a webhook notification
    Then the dashboard live-updates to show "School Holiday (Cancelled)"

  Scenario: Event deleted in Google Calendar disappears live from open dashboard
    Given I have a user like "TestFamilyMember" with calendar "Family Events"
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When the event "School Holiday" is deleted from Google Calendar
    And Google Calendar sends a webhook notification
    Then the dashboard live-updates to remove "School Holiday"
```

---

## Files Changed / Created

| Action | File |
|--------|------|
| **New** | `tools/FamilyHQ.Simulator/Controllers/BackdoorEventsController.cs` |
| **New** | `tools/FamilyHQ.Simulator/DTOs/BackdoorEventRequest.cs` |
| **Modified** | `tests-e2e/FamilyHQ.E2E.Data/Api/SimulatorApiClient.cs` |
| **New** | `tests-e2e/FamilyHQ.E2E.Steps/WebhookDataSteps.cs` |
| **New** | `tests-e2e/FamilyHQ.E2E.Features/GoogleCalendarSync.feature` |

---

## Out of Scope

- Webhook authentication/validation (Google signature headers) — not relevant to the Simulator
- Multi-calendar webhook scenarios — can be added in a follow-up
- Recurring event changes — out of scope for this iteration
- Making the WebApi URL in `WebhookController` configurable — pre-existing constraint
