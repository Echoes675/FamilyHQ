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

Unlike the existing `EventsController` (which mimics the Google Calendar API and reads the user from a Bearer token), this controller accepts `userId` directly in the request body. This allows test code to mutate simulator state without constructing a fake OAuth token.

**Endpoints:**

| Method | Route | Purpose |
|--------|-------|---------|
| `POST` | `/api/simulator/backdoor/events` | Add a new event for a user |
| `PUT` | `/api/simulator/backdoor/events/{eventId}` | Update an event's summary |
| `DELETE` | `/api/simulator/backdoor/events/{eventId}?userId={userId}` | Delete an event |

`POST` returns the created event's ID in the response body so the scenario context can store it for subsequent update/delete steps.

### 2. SimulatorApiClient Extensions

Four new methods added to `tests-e2e/FamilyHQ.E2E.Data/Api/SimulatorApiClient.cs`:

```csharp
Task<string> AddEventAsync(string userId, string calendarId, string summary, DateTime start, DateTime end, bool isAllDay)
Task UpdateEventAsync(string userId, string eventId, string newSummary)
Task DeleteEventAsync(string userId, string eventId)
Task TriggerWebhookAsync()
```

- `AddEventAsync` returns the new event's ID for storage in `ScenarioContext`
- `TriggerWebhookAsync` calls `POST /simulate/push` — the Simulator then forwards to `POST /api/sync/webhook` on the WebApi (mirrors real Google push notification behaviour)
- All methods follow the existing `EnsureSuccessStatusCode()` pattern

### 3. WebhookDataSteps.cs

New step definitions file in `tests-e2e/FamilyHQ.E2E.Steps/`:

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

**Real-time assertion steps** (Playwright polling loop, up to 5s, no navigation):
```gherkin
Then the dashboard live-updates to show "X"
Then the dashboard live-updates to remove "X"
```

The mutation steps read `UserTemplate` and calendar IDs from `ScenarioContext` (set during `Given I have a user like...`). The event ID returned by `AddEventAsync` is stored in `ScenarioContext["LastCreatedEventId"]` for use by update/delete steps.

The live-update assertion steps poll `_dashboardPage.GetVisibleEventsAsync()` in a short retry loop rather than using a hard wait, so they pass as soon as the SignalR-pushed DOM update lands.

### 4. GoogleCalendarSync.feature

New feature file in `tests-e2e/FamilyHQ.E2E.Features/`:

#### Eventual Consistency Scenarios

```gherkin
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
```

#### Real-Time SignalR Push Scenarios

```gherkin
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
