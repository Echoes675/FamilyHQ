# Event Multi-Calendar Support — Design Spec

**Date**: 2026-03-21
**Branch**: feature/events-multi-calendar
**Status**: Awaiting user approval

---

## Core Principle

Google Calendar is the source of truth. FamilyHQ reads from and writes to Google, preserving the natural Google Calendar experience. Events created or edited in Google Calendar on mobile devices must behave exactly as they would without FamilyHQ — FamilyHQ overlays a richer display and creation UI on top, but does not distort the underlying Google data model.

---

## Layer Architecture and Mapping Boundaries

Each layer owns its own types. No type from one layer is used directly in another. Mapping between layers is explicit and located at the boundary.

```
┌──────────────────────────────────────────────────────────────┐
│  Google / Simulator                                          │
│  Returns Google Calendar API JSON (GoogleApiEvent, etc.)     │
└───────────────────────────┬──────────────────────────────────┘
                            │ HTTP JSON
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  GoogleCalendarClient  (FamilyHQ.Services)                   │
│  Deserialises Google API JSON into Google response types     │
│  Maps Google response types → domain models                  │
│  IGoogleCalendarClient (FamilyHQ.Core) returns domain models │
└───────────────────────────┬──────────────────────────────────┘
                            │ CalendarEvent, CalendarInfo
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  Service / Repository layer  (FamilyHQ.Services / .Data)     │
│  All business logic operates on domain models                │
└───────────────────────────┬──────────────────────────────────┘
                            │ CalendarEvent, CalendarInfo
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  API Controllers  (FamilyHQ.WebApi)                          │
│  Maps domain models → API response DTOs                      │
│  Returns CalendarEventDto, MonthViewDto, etc. over HTTP      │
└───────────────────────────┬──────────────────────────────────┘
                            │ HTTP JSON (CalendarEventDto, etc.)
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  CalendarApiService  (FamilyHQ.WebUi)                        │
│  Deserialises API response DTOs                              │
│  Maps DTOs → ViewModels for Blazor components                │
└───────────────────────────┬──────────────────────────────────┘
                            │ CalendarEventViewModel, etc.
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  Blazor Components  (FamilyHQ.WebUi)                         │
│  Binds to ViewModels only                                    │
└──────────────────────────────────────────────────────────────┘
```

**Rules enforced by this architecture:**
- Domain models (`CalendarEvent`, `CalendarInfo`) never appear in API responses.
- API DTOs (`CalendarEventDto`, `MonthViewDto`) never appear in Blazor components.
- ViewModels never appear in API requests or responses.
- Google response types never escape `GoogleCalendarClient`.

---

## Overview

Extend FamilyHQ so that an event can be associated with multiple in-scope Google Calendars. On the calendar grid, the event appears once per internal attendee calendar, each in that calendar's colour. Users can add or remove internal calendars from an event via a chip selector in the create/edit modal.

---

## Google Calendar Mechanism

In Google Calendar, the natural way for one event to appear in multiple calendars is via the **attendee model**:

- An event is created in one calendar — this calendar is the **organiser**.
- Additional calendars are added as **attendees** using their `GoogleCalendarId` (email-like identifier, e.g. `family@group.calendar.google.com`).
- Because FamilyHQ operates under a single Google account that owns all in-scope calendars, invitations to same-account calendars are **auto-accepted** by Google.
- The event has **one `GoogleEventId`** and appears in every attendee calendar's `events.list` response under the same ID.
- Changes via `events.update` on the organiser calendar propagate to all attendee views — it is one event.
- Edits or deletes in Google Calendar mobile affect all calendar views automatically.

### Simulator responsibility

The simulator must return **Google-format JSON responses** (see Google API Response Types below), not domain models. This ensures `GoogleCalendarClient`'s mapping code is exercised against realistic data during unit and integration testing, and that switching to real Google requires no changes to the client.

Behavioural requirements for the simulator:
- `events.list(calendarId)` returns events where `calendarId` is the **organiser** OR appears in the event's **attendees list**.
- `events.patch` modifying `attendees` is reflected immediately in subsequent `events.list` calls.
- `events.move` changes the organiser calendar; the old calendar continues to see the event only if it remains as an attendee.

### Simulator data model additions

`SimulatedEvent` currently stores only `CalendarId` (the organiser) and has no attendee storage. The following additions are required:

**New `SimulatedEventAttendee` table** (`tools/FamilyHQ.Simulator/Models/SimulatedEventAttendee.cs`):

```csharp
public class SimulatedEventAttendee
{
    public int Id { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string AttendeeCalendarId { get; set; } = string.Empty;
}
```

`SimContext` gains `DbSet<SimulatedEventAttendee> EventAttendees`. EF Core migration adds the table with an index on `(EventId, AttendeeCalendarId)` unique.

**Updated simulator endpoints (`tools/FamilyHQ.Simulator/Controllers/EventsController.cs`)**:

| Method | Route | Change |
|--------|-------|--------|
| `GET` | `/calendars/{calendarId}/events` | Filter: `WHERE CalendarId = calendarId OR EXISTS(SELECT 1 FROM EventAttendees WHERE EventId = e.Id AND AttendeeCalendarId = calendarId)` |
| `GET` | `/calendars/{calendarId}/events/{eventId}` | **New.** Returns single event with `organizer` and `attendees` fields. Returns 404 if not found. Used by `IGoogleCalendarClient.GetEventAsync`. |
| `PATCH` | `/calendars/{calendarId}/events/{eventId}` | **New.** Accepts `{ attendees: [{ email: string }] }` body. Replaces all rows in `EventAttendees` for this `EventId`. The organiser calendar (`CalendarId`) is never added to the attendees table. |

**Response format for all event endpoints must include `organizer` and `attendees`:**

```json
{
  "id": "...",
  "status": "confirmed",
  "summary": "...",
  "start": { "dateTime": "..." },
  "end": { "dateTime": "..." },
  "organizer": { "email": "calendarId@group.calendar.google.com", "self": true },
  "attendees": [
    { "email": "othercalendar@group.calendar.google.com", "responseStatus": "accepted" }
  ]
}
```

The `self` field on `organizer` is always `true` in the simulator (single-account assumption). `attendees` is omitted from the response when the list is empty.

---

## Google API Response Types

These types live in `FamilyHQ.Services/Calendar/GoogleApi/` and are **internal to that namespace**. They mirror the Google Calendar API JSON schema exactly. `GoogleCalendarClient` deserialises HTTP responses into these types and then maps them to domain models. They never escape `GoogleCalendarClient`.

The simulator's `EventsController` serialises these same types as its HTTP responses, ensuring the mapping code is tested against the correct format.

```csharp
// Maps to Google Calendar API "Event" resource
internal record GoogleApiEvent(
    string Id,
    string? ICalUID,
    string? Status,          // "confirmed", "cancelled"
    GoogleApiEventDateTime Start,
    GoogleApiEventDateTime End,
    string? Summary,         // title
    string? Location,
    string? Description,
    GoogleApiOrganizer? Organizer,
    IReadOnlyList<GoogleApiAttendee>? Attendees);

internal record GoogleApiEventDateTime(
    string? DateTime,        // RFC3339, present for timed events
    string? Date,            // yyyy-MM-dd, present for all-day events
    string? TimeZone);

internal record GoogleApiOrganizer(
    string? Email,
    bool Self);              // true when organiser is the authenticated account

internal record GoogleApiAttendee(
    string Email,
    string? ResponseStatus); // "accepted", "declined", "needsAction", "tentative"

// Wrapper for events.list and events.watch responses
internal record GoogleApiEventList(
    IReadOnlyList<GoogleApiEvent> Items,
    string? NextPageToken,
    string? NextSyncToken);

// Maps to Google Calendar API "CalendarListEntry" resource
internal record GoogleApiCalendarListEntry(
    string Id,               // GoogleCalendarId — email-like identifier
    string? Summary,
    string? BackgroundColor,
    string? ForegroundColor,
    string? AccessRole);

internal record GoogleApiCalendarList(
    IReadOnlyList<GoogleApiCalendarListEntry> Items,
    string? NextPageToken,
    string? NextSyncToken);
```

---

## What Is Already in Place

- `CalendarEvent` ↔ `CalendarInfo` many-to-many via `CalendarEventCalendar` join table — **retained unchanged**.
- `ICalendarRepository` already includes `GetEventAsync`, `GetEventsAsync` (with `Calendars` navigation), `DeleteEventAsync`, and `RemoveCalendarAsync`.
- The existing `ReassignEventRequest` / `ReassignAsync` — **retired** (superseded by the new add/remove calendar endpoints).

---

## Data Model Changes

### New columns on `CalendarEvent`

| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| `OwnerCalendarInfoId` | `Guid` | No | FK to `CalendarInfo`. The calendar that is the Google organiser for this event. Hidden from the user. Used by the service layer to select the correct `calendarId` for `events.update`, `events.move`, and `events.delete`. |
| `IsExternallyOwned` | `bool` | No | `true` when Google's `organizer.Self = false` at sync time. Informational only. Default `false`. **Not used for delete decisions** — a live check is performed instead. |

`OwnerCalendarInfoId` FK uses `DeleteBehavior.Restrict` — ownership must be transferred before a calendar can be removed.

### EF Core configuration additions (`CalendarEventConfiguration`)

```csharp
builder.HasOne<CalendarInfo>()
       .WithMany()
       .HasForeignKey(e => e.OwnerCalendarInfoId)
       .OnDelete(DeleteBehavior.Restrict);

builder.Property(e => e.IsExternallyOwned)
       .IsRequired()
       .HasDefaultValue(false);
```

The existing many-to-many configuration is **unchanged**. The existing `IX_Events_GoogleEventId` unique index is also **retained unchanged** — under the attendee model, one `GoogleEventId` maps to exactly one `CalendarEvent` row; multi-calendar membership is expressed via the join table, not duplicate rows.

### Migration

1. Add `OwnerCalendarInfoId (uuid NOT NULL DEFAULT '00000000-...')` to `Events`.
2. Backfill from the existing join table (all current events have exactly one calendar):
   ```sql
   UPDATE "Events" e
   SET "OwnerCalendarInfoId" = (
       SELECT cec."CalendarsId"
       FROM "CalendarEventCalendar" cec
       WHERE cec."EventsId" = e."Id"
       ORDER BY cec."CalendarsId"
       LIMIT 1
   );
   ```
3. Add FK `Events.OwnerCalendarInfoId → Calendars.Id` (RESTRICT).
4. Add `IsExternallyOwned (boolean NOT NULL DEFAULT false)` to `Events`.
5. Drop temporary DEFAULT:
   ```sql
   ALTER TABLE "Events" ALTER COLUMN "OwnerCalendarInfoId" DROP DEFAULT;
   ```

---

## Type Changes per Layer

### Layer 1 — Google API response types (new, internal to `FamilyHQ.Services/Calendar/GoogleApi/`)

Defined above. Used only inside `GoogleCalendarClient` and the simulator.

### Layer 2 — Domain models (`FamilyHQ.Core/Models`)

`CalendarEvent` gains `OwnerCalendarInfoId` and `IsExternallyOwned` (see Data Model Changes). No other changes.

### Layer 3 — API inbound request types (`FamilyHQ.Core/DTOs`)

#### `CreateEventRequest` (updated)

```csharp
record CreateEventRequest(
    IReadOnlyList<Guid> CalendarInfoIds,   // min 1, no duplicates; first becomes organiser
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description);
```

#### `UpdateEventRequest` (updated)

`CalendarInfoId` removed — field edits apply to the event regardless of which calendar is being viewed.

```csharp
record UpdateEventRequest(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description);
```

#### `ReassignEventRequest` — removed.

### Layer 3 — API outbound response types (`FamilyHQ.Core/DTOs`)

#### `CalendarEventDto` (updated)

`CalendarInfoId` (formerly the "primary calendar" concept) is replaced by `Calendars` — a list of all linked calendars. Controllers map domain models to this type; it is the only event type that crosses the API boundary.

`GoogleEventId` is intentionally retained in the DTO for future deep-link and diagnostic use (e.g., linking to the event in Google Calendar). The Blazor layer must not use it to interact with Google directly — it is read-only context.

```csharp
record CalendarEventDto(
    Guid Id,
    string GoogleEventId,      // read-only; retained for diagnostics/deep-link
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    IReadOnlyList<EventCalendarDto> Calendars);
```

`EventCalendarDto` is unchanged: `record EventCalendarDto(Guid Id, string DisplayName, string? Color)`.

#### `MonthViewDto` (updated — also a pre-existing violation fix)

`Days` currently contains `Dictionary<string, List<CalendarEventViewModel>>` — a pre-existing violation where a ViewModel type leaks into an API response DTO in `FamilyHQ.Core`. This feature corrects that: `Days` becomes `Dictionary<string, List<CalendarEventDto>>`. ViewModels do not appear in API responses.

### Layer 4 — ViewModels (`FamilyHQ.WebUi`)

`CalendarEventViewModel` gains an `AllCalendars` property — the full list of calendars the event belongs to — so the edit modal can render chips without holding a DTO reference. `CalendarApiService` populates this from `CalendarEventDto.Calendars` during mapping, projecting each `EventCalendarDto` into a `CalendarSummaryViewModel`.

```csharp
record CalendarEventViewModel(
    Guid Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    // The calendar this capsule represents (one ViewModel per calendar on the grid)
    Guid CalendarInfoId,
    string CalendarDisplayName,
    string? CalendarColor,
    // All calendars this event belongs to — used by edit modal for chip rendering
    IReadOnlyList<CalendarSummaryViewModel> AllCalendars);
```

`CalendarSummaryViewModel` (defined in `ICalendarApiService` section above: `record CalendarSummaryViewModel(Guid Id, string DisplayName, string? Color)`) is reused here. `EventCalendarDto` never appears in a ViewModel or component.

`CalendarApiService` creates one `CalendarEventViewModel` per `CalendarEventDto` entry received from the API. Blazor components bind to `CalendarEventViewModel` only — no DTO type appears in component code.

#### `MonthViewModel` (new, `FamilyHQ.WebUi`)

`ICalendarApiService.GetEventsForMonthAsync` returns a `MonthViewModel` (not `MonthViewDto`). `CalendarApiService` maps the `MonthViewDto` received from the API into this type, converting each `CalendarEventDto` in `Days` into a `CalendarEventViewModel`.

```csharp
record MonthViewModel(Dictionary<string, List<CalendarEventViewModel>> Days);
```

`MonthViewDto` (with `Dictionary<string, List<CalendarEventDto>>`) remains the API-layer type returned by the controller and deserialised by `CalendarApiService`. It never appears in Blazor component code.

#### `GoogleEventDetail` — inbound result type for external-attendee check

This type is returned by `IGoogleCalendarClient.GetEventAsync`. It lives in **`FamilyHQ.Core/Models`** alongside `CalendarEvent` and `CalendarInfo`.

**Architectural note:** `FamilyHQ.Core` is a shared assembly consumed by both `FamilyHQ.WebApi` and `FamilyHQ.WebUi`. Placing `GoogleEventDetail` in `FamilyHQ.Core/Models` makes it technically visible to the Blazor WASM project. This is an accepted limitation of the current two-project architecture — there is no server-only shared assembly. However, `GoogleEventDetail` contains no Google-specific or server-specific operational data, and Blazor components must not use it (convention and code review enforce this). A future refactor may introduce a `FamilyHQ.Server.Core` assembly to house backend-only shared types; that is out of scope here.

```csharp
public record GoogleEventDetail(
    string Id,
    string? OrganizerEmail,
    IReadOnlyList<string> AttendeeEmails);
```

`GoogleCalendarClient` maps `GoogleApiEvent` → `GoogleEventDetail` before returning it. `GoogleApiEvent` never escapes the Google client.

---

## Validator Changes

### `CreateEventRequestValidator` (updated)
`CalendarInfoIds`: not null; at least one item; no duplicate Guids; each item non-empty.

### `UpdateEventRequestValidator` (new)
Validates `Title` (not empty, ≤ 200 chars), `Start` (not empty), `End` (not empty, ≥ `Start`). Replaces the existing controller bridge that adapted `UpdateEventRequest` into `CreateEventRequest`.

---

## Backend API Changes

Multi-calendar operations are not scoped to a single calendar. New create, update, and delete endpoints use an event-centric route. The old calendar-scoped event routes are removed (no production deployment to preserve).

### Endpoint changes

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/api/calendars/events?year=&month=` | Unchanged route. Returns `MonthViewDto` (updated to use `CalendarEventDto`). |
| `GET` | `/api/calendars` | Unchanged. |
| `POST` | `/api/events` | New. Creates event. Body: `CreateEventRequest`. Returns 201 + `CalendarEventDto`. |
| `PUT` | `/api/events/{eventId}` | New. Field edits. Body: `UpdateEventRequest`. Returns 200 + `CalendarEventDto`. |
| `DELETE` | `/api/events/{eventId}` | New. Full event delete with live external-attendee check. Returns 204. |
| `POST` | `/api/events/{eventId}/calendars/{calendarId}` | New. Add calendar to event. `calendarId` = `CalendarInfo.Id`. Returns 200 + `CalendarEventDto`. Idempotent. |
| `DELETE` | `/api/events/{eventId}/calendars/{calendarId}` | New. Remove calendar from event. Returns 204; 404 if not linked. |
| `POST` | `/api/calendars/{calendarId}/events` | Removed. |
| `PUT` | `/api/calendars/{calendarId}/events/{eventId}` | Removed. |
| `DELETE` | `/api/calendars/{calendarId}/events/{eventId}` | Removed. |
| `POST` | `/api/calendars/{calendarId}/events/{eventId}/reassign` | Removed. |

### Controller

A new `EventsController` handles the five event-centric routes. `CalendarsController` retains only `GET /api/calendars` and `GET /api/calendars/events`. Controllers map domain models → DTOs; they never reference ViewModels.

---

## `ICalendarEventService` (revised)

Services operate on domain models. They return domain models to controllers, which map to DTOs.

```csharp
Task<CalendarEvent> CreateAsync(CreateEventRequest request, CancellationToken ct = default);
Task<CalendarEvent> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct = default);
Task<CalendarEvent> AddCalendarAsync(Guid eventId, Guid targetCalendarInfoId, CancellationToken ct = default);
Task RemoveCalendarAsync(Guid eventId, Guid calendarInfoId, CancellationToken ct = default);
Task DeleteAsync(Guid eventId, CancellationToken ct = default);
```

`ReassignAsync` removed.

---

## `IGoogleCalendarClient` Changes

The interface returns domain models. Mapping from Google API response types to domain models happens inside `GoogleCalendarClient` — callers never see Google response types.

### Additions

```csharp
// Full replacement of the attendees array on the event.
// attendeeGoogleCalendarIds must contain ALL calendars EXCEPT the organiser —
// Google Calendar keeps organiser and attendees as separate fields,
// and including the organiser in the attendees list is ignored or rejected by the API.
Task PatchEventAttendeesAsync(
    string organizerCalendarId,
    string googleEventId,
    IEnumerable<string> attendeeGoogleCalendarIds,
    CancellationToken ct);

// Returns null if the event is not found (404)
Task<GoogleEventDetail?> GetEventAsync(
    string googleCalendarId,
    string googleEventId,
    CancellationToken ct);
```

`MoveEventAsync` is retained. `events.move` preserves the `GoogleEventId` — the event ID does not change after a move. The return value of `MoveEventAsync` is informational and may be discarded. `GoogleCalendarClient` maps the Google API response for `GetEventAsync` into `GoogleEventDetail` before returning; `GoogleApiEvent` stays internal.

---

## Service Behaviour

### Ownership verification

`GetCalendarByIdAsync` is not user-scoped and must not be used for ownership checks. Use `GetCalendarsAsync()` (filters by `ICurrentUserService.UserId`) and check whether the target `Id` is present in the returned set.

### External-attendee check (`DeleteAsync`)

Call `IGoogleCalendarClient.GetEventAsync`. Form union of `OrganizerEmail` + `AttendeeEmails`. Compare against the user's `CalendarInfo.GoogleCalendarId` set. Any email not in that set is an external party.

- **External parties present**: delete locally only. Do not call `events.delete` — Google event persists for external attendees.
- **No external parties**: call `events.delete`, then delete locally.
- **`GetEventAsync` returns null**: Google event already gone; delete locally only.

### Transaction and error model

Google API calls are made **before** opening a DB transaction. DB failure after Google success: log reconciliation error (operation, event ID, calendar IDs) and rethrow. No automatic Google rollback.

### Sync service (`CalendarSyncService`)

`CalendarSyncService.SyncAsync` creates `CalendarEvent` objects from Google responses. With the new columns:

- **`OwnerCalendarInfoId`**: set to the `CalendarInfo.Id` of the calendar currently being synced **when** `GoogleApiOrganizer.Self = true`. When `Self = false` (the event was created by another calendar/user), `OwnerCalendarInfoId` is set to the synced calendar's `CalendarInfo.Id` as a provisional owner (the closest we have without a cross-calendar lookup at sync time). The correct owner is the one that actually calls `events.update` — determined by `OwnerCalendarInfoId` at write time.
- **`IsExternallyOwned`**: set to `true` when `GoogleApiOrganizer.Self = false`; `false` otherwise.

`GoogleApiEvent.Organizer` and `GoogleApiEvent.Attendees` fields (defined in the Google API Response Types section above) are used for this mapping. The `CalendarSyncService` must be updated to pass the `GoogleApiEvent` organiser and attendees through to the `CalendarEvent` construction path.

### `CreateAsync`

1. `GetCalendarsAsync()` → validate all `CalendarInfoIds` exist in set. Throw `ValidationException` for unknowns. Deduplicate; retain matched `CalendarInfo` objects.
2. `CalendarInfoIds[0]` is organiser. Call `CreateEventAsync(organiserGoogleCalendarId, event)` → `GoogleEventId`.
3. If additional calendars: call `PatchEventAttendeesAsync(organiserCalendarId, googleEventId, additionalCalendarIds)`.
4. DB transaction: insert `CalendarEvent` (`OwnerCalendarInfoId = CalendarInfoIds[0]`, `IsExternallyOwned = false`); insert join table entries for all calendars.
5. Return `CalendarEvent` with `Calendars` navigation populated.
6. DB failure after Google success: log reconciliation error, rethrow.

### `UpdateAsync`

1. Load event from repository (which includes `OwnerCalendarInfoId`). Call `GetCalendarsAsync()` to retrieve the user's calendars. Verify that `OwnerCalendarInfoId` is present in that set — do not call `GetCalendarByIdAsync` (not user-scoped). Throw `NotFoundException` if the event is missing or the owner calendar is not in the user's set.
2. Call `UpdateEventAsync(ownerGoogleCalendarId, event)`.
3. DB transaction: update fields, `SaveChangesAsync`.
4. DB failure: log reconciliation error, rethrow.

### `AddCalendarAsync`

1. Load event, verify ownership.
2. Verify `targetCalendarInfoId` in user's calendar set. Throw `NotFoundException` if absent.
3. If already in `event.Calendars`: return event (idempotent, no Google call).
4. Updated attendees = existing attendees + `targetCalendarInfo.GoogleCalendarId`.
5. Call `PatchEventAttendeesAsync`.
6. DB transaction: insert join table entry. Catch unique-constraint violation on `PK_CalendarEventCalendar` specifically → re-fetch and return (concurrent idempotent). Do not catch other DB exceptions — other constraint violations must surface. Log reconciliation error and rethrow on other DB failure after Google success.
7. Return updated event.

### `RemoveCalendarAsync`

1. Load event, verify ownership.
2. Find `CalendarInfo` matching `calendarInfoId` in `event.Calendars`. Throw `NotFoundException` if absent.
3. If last linked calendar: delegate to `DeleteAsync(eventId)`. The full `DeleteAsync` semantics apply — including the live external-attendee check and all its branches (skip Google delete if external parties present, etc.). This is intentional: removing the last calendar is equivalent to deleting the event, and the same safety rules apply regardless of how the delete was triggered.
4. If owner calendar and others remain: call `MoveEventAsync` to promote a remaining calendar as new owner; note new `OwnerCalendarInfoId` for DB step.
5. Updated attendees = all linked `GoogleCalendarId` values, minus the removed calendar, minus the new owner calendar. The new owner becomes the organiser and must not appear in the attendees list (Google keeps organiser and attendees as separate fields).
6. Call `PatchEventAttendeesAsync(newOrExistingOwnerGoogleCalendarId, googleEventId, updatedAttendees)`.
7. DB transaction: remove join table entry; update `OwnerCalendarInfoId` if changed.
8. DB failure after Google success: log reconciliation error (event ID, removed calendar ID, new owner calendar ID if changed), rethrow.

### `DeleteAsync`

1. Load event + `OwnerCalendarInfo`, verify ownership.
2. External-attendee check.
3. If no external parties: call `events.delete`.
4. DB transaction: remove join table entries; delete `CalendarEvent` row.

---

## Grid Query Change

Responsibilities per layer, consistent with the layer diagram:

1. **Repository / service**: `GetEventsForMonthAsync` returns `IReadOnlyList<CalendarEvent>` with `Calendars` navigations fully populated (all linked `CalendarInfo` records eager-loaded). No DTO construction or expansion here.
2. **Controller**: Iterates the returned `CalendarEvent` list. For each event with N linked calendars, emits N `CalendarEventDto` entries — one per calendar — each with a single-entry `Calendars` list for that calendar (colour, display name) plus the full `Calendars` list so the edit modal can render all chips. Groups entries by date and builds `MonthViewDto.Days`.
3. **`CalendarApiService`**: Receives `MonthViewDto`, maps each `CalendarEventDto` in `Days` to a `CalendarEventViewModel`, and returns a `MonthViewModel` to Blazor components. No expansion logic — the expansion already happened in the controller.

---

## `ICalendarApiService` Changes (Blazor UI service layer)

The Blazor-side service interface gains new method signatures to match the event-centric routes. All methods return ViewModels — DTOs are an internal mapping intermediate inside `CalendarApiService` and never surfaced through this interface. The old calendar-scoped create/update/delete methods are removed.

**Note on inbound request types:** `CreateEventRequest` and `UpdateEventRequest` live in `FamilyHQ.Core/DTOs` and are used directly as parameters in this interface. This is an intentional design decision: these types represent the user's intent (form data) and are structurally identical to what the API expects. Sharing them across the Blazor–API boundary avoids a redundant `CreateEventFormModel`/`UpdateEventFormModel` mapping step that would add no value. This is the accepted exception for _inbound_ request types. Unlike response types — where the mapping from domain model to DTO is the point of the layer — request types simply carry user input. Both the Blazor component and the controller validate and consume the same shape.

```csharp
// Create event in one or more calendars → returns the new event ViewModel (single calendar view)
Task<CalendarEventViewModel> CreateEventAsync(CreateEventRequest request, CancellationToken ct);

// Update event fields (title, times, etc.) → returns updated event ViewModel
Task<CalendarEventViewModel> UpdateEventAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct);

// Delete event entirely (backend handles external-attendee check)
Task DeleteEventAsync(Guid eventId, CancellationToken ct);

// Add a calendar to an event → returns updated event ViewModel (AllCalendars reflects new state)
Task<CalendarEventViewModel> AddCalendarToEventAsync(Guid eventId, Guid calendarId, CancellationToken ct);

// Remove a calendar from an event
Task RemoveCalendarFromEventAsync(Guid eventId, Guid calendarId, CancellationToken ct);

// Existing — returns a lightweight calendar summary ViewModel for chip rendering
Task<IReadOnlyList<CalendarSummaryViewModel>> GetCalendarsAsync(CancellationToken ct);

// Existing — now returns MonthViewModel (CalendarApiService maps MonthViewDto → MonthViewModel)
Task<MonthViewModel> GetEventsForMonthAsync(int year, int month, CancellationToken ct);
```

`CalendarSummaryViewModel` is a new lightweight ViewModel: `record CalendarSummaryViewModel(Guid Id, string DisplayName, string? Color)`. This also resolves the pre-existing `CalendarInfo` domain model leakage in `GetCalendarsAsync`.

`GetEventsForMonthAsync` now returns a `MonthViewModel` (defined in the Layer 4 section below). `CalendarApiService` deserialises the `MonthViewDto` from the API and maps each `CalendarEventDto` in `Days` into a `CalendarEventViewModel` before returning. No DTO type appears in Blazor component code.

---

## UI Changes

### Chip selector (replaces calendar dropdown)

All in-scope calendars rendered as chips (colour dot + display name). The modal binds to `IReadOnlyList<CalendarSummaryViewModel>` returned by `ICalendarApiService.GetCalendarsAsync`. No `CalendarInfo` domain model or `CalendarEventDto` appears in component code.

**Create mode**: chips start deselected. Accumulate client-side; single `POST /api/events` on Save carrying all selected `CalendarInfoIds`.

**Edit mode**: the modal holds the `CalendarEventDto` from the API response. Chips whose `Id` appears in `CalendarEventDto.Calendars` are active.
- Tap inactive chip → `POST /api/events/{eventId}/calendars/{calendarId}`; activates on 200.
- Tap active chip → `DELETE /api/events/{eventId}/calendars/{calendarId}`, unless last active chip.
- **Last chip protection**: last active chip has no ✕. Tap is no-op. Tooltip: "Use Delete to remove this event entirely."

### Delete button

Calls `DELETE /api/events/{eventId}`. Returns 204. Backend external-attendee check is transparent.

### Grid

No structural changes. Expanded `MonthViewDto` naturally produces one capsule per calendar per event.

---

## Testing

### Unit tests (TDD — written before implementation)

**Validators**
- `CreateEventRequest`: rejects null list; empty list; duplicates; empty Guid; accepts one or more valid distinct Guids.
- `UpdateEventRequestValidator`: Title not empty ≤ 200; Start not empty; End ≥ Start.

**GoogleCalendarClient mapping**
- `GoogleApiEvent` with organiser and attendees maps correctly to `GoogleEventDetail`.
- `GoogleApiEvent` with `Status = "cancelled"` is handled as deletion during sync.
- `GoogleApiEventDateTime` with `Date` only maps to all-day event on domain model.

**Service — CreateAsync**
- Calls `CreateEventAsync` for organiser; `PatchEventAttendeesAsync` for additional calendars.
- Does not call `PatchEventAttendeesAsync` when only one calendar selected.
- `OwnerCalendarInfoId` set to first calendar; join entries for all.
- `ValidationException` on unknown `CalendarInfoId`.
- Reconciliation error logged on DB failure after Google success.

**Service — UpdateAsync**
- Calls `UpdateEventAsync` using owner calendar's Google ID.
- DB fields updated in transaction.
- `NotFoundException` on missing or unowned event.

**Service — AddCalendarAsync**
- `PatchEventAttendeesAsync` called with full updated attendee list.
- Idempotent: no Google call when already linked.
- Handles concurrent unique-constraint violation gracefully.
- `NotFoundException` when `targetCalendarInfoId` not in user's set.

**Service — RemoveCalendarAsync**
- Non-owner, non-last: `PatchEventAttendeesAsync` without removed calendar; join entry removed; `MoveEventAsync` not called.
- Owner, non-last: `MoveEventAsync` called first; `PatchEventAttendeesAsync` called with new owner and remaining attendees (new owner excluded from attendees list); `OwnerCalendarInfoId` updated in DB.
- Last calendar (non-owner): delegates to `DeleteAsync` (no `PatchEventAttendeesAsync` call).
- **Last calendar (owner)**: also delegates to `DeleteAsync`; `MoveEventAsync` is NOT called before delegation (no calendar to move to).
- `NotFoundException` when calendar not in event's linked set.

**Service — DeleteAsync**
- External attendee present: Google delete skipped; local rows deleted.
- No external attendees: Google delete called; local rows deleted.
- `GetEventAsync` null: Google delete skipped; local rows deleted.

**Controller — EventsController**
- Each endpoint maps domain model → `CalendarEventDto` correctly (no ViewModel fields in response).
- `POST /api/events`: 201 + `CalendarEventDto` with `Calendars` populated.
- `POST /api/events/{id}/calendars/{calendarId}`: 200 + `CalendarEventDto`; 200 idempotent.
- `DELETE /api/events/{id}/calendars/{calendarId}`: 204; 404 on `NotFoundException`.

**Grid query**
- Event linked to two calendars returns two `CalendarEventDto` entries in `MonthViewDto`, each with correct `Calendars` list and colour.

**CalendarApiService (Blazor)**
- Two `CalendarEventDto` entries for one event map to two `CalendarEventViewModel` entries with correct calendar colours.
- No DTO type appears in the resulting ViewModels.

### E2E / acceptance tests (BDD)

- Create event in single calendar → appears once on grid in that calendar's colour.
- Create event in two calendars → appears twice on grid, once per calendar colour.
- Edit event → add calendar via chip → event appears in both calendars on grid.
- Edit event → remove non-last calendar chip → event removed from that calendar, remains on other.
- Last chip protection → tapping last active chip does nothing; Delete is the only exit.
- Delete event, no external attendees → deleted from Google and FamilyHQ.
- Edit event fields (title) → call `GET /api/calendars/events` after the `PUT /api/events/{eventId}` → both `CalendarEventDto` entries in `MonthViewDto.Days` for that event carry the updated title.
- **Edge case**: remove owner calendar chip when other calendars remain → `events.move` promotes a new owner; event is still visible on both remaining and removed calendar views as expected; `OwnerCalendarInfoId` is updated in DB.
- **Edge case**: externally created event, internal calendar invited, external removes itself → user deletes → full Google delete, no zombie.
- **Edge case**: external still attendee → user deletes → local delete only; Google event persists.
- **Simulator**: `events.list(calendarId=B)` returns event where B is attendee not organiser, confirming simulator routes by attendee membership correctly.

---

## Out of Scope

- Events across multiple Google accounts.
- Displaying external attendee names in the FamilyHQ UI.
- Sync-time deduplication by `iCalUID` (deferred).
