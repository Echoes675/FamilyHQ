# Event Multi-Calendar Support — Design Spec

**Date**: 2026-03-21
**Branch**: feature/events-multi-calendar
**Status**: Awaiting user approval

---

## Core Principle

Google Calendar is the source of truth. FamilyHQ reads from and writes to Google, preserving the natural Google Calendar experience. Events created or edited in Google Calendar on mobile devices must behave exactly as they would without FamilyHQ — FamilyHQ overlays a richer display and creation UI on top, but does not distort the underlying Google data model.

---

## Overview

Extend FamilyHQ so that an event can be associated with multiple in-scope Google Calendars. On the calendar grid, the event appears once per internal attendee calendar, each in that calendar's colour. Users can add or remove internal calendars from an event via a chip selector in the create/edit modal.

---

## Google Calendar Mechanism

In Google Calendar, the natural way for one event to appear in multiple calendars is via the **attendee model**:

- An event is created in one calendar — this calendar is the **organiser**.
- Additional calendars are added as **attendees** using their `GoogleCalendarId` (which is an email-like identifier, e.g. `family@group.calendar.google.com`).
- Because FamilyHQ operates under a single Google account that owns all in-scope calendars, invitations to same-account calendars are **auto-accepted** by Google.
- The event has **one `GoogleEventId`** and appears in every attendee calendar's `events.list` response under the same ID.
- Changes made via `events.update` on the organiser calendar propagate to all attendee views because it is one event.
- If a user edits or deletes the event in Google Calendar mobile, the change affects all calendar views automatically.

This is the correct model for the stated requirements: Google-native experience, no duplicate independent events, changes propagate everywhere.

### Simulator responsibility

The Google Calendar simulator must implement the attendee routing faithfully:

- `events.list(calendarId)` returns events where `calendarId` is the **organiser calendar**, OR where `calendarId` appears in the event's **attendees list**.
- `events.patch` / `events.update` that modifies the `attendees` array is reflected immediately in subsequent `events.list` calls for each affected calendar.
- `events.move(calendarId, eventId, destinationCalendarId)` changes the organiser calendar; the event moves from the old organiser's exclusive ownership to the new one. The old calendar continues to see the event only if it remains as an attendee.

This ensures the API layer is correct before real Google integration testing begins.

---

## What Is Already in Place

- `CalendarEvent` ↔ `CalendarInfo` many-to-many via `CalendarEventCalendar` join table — **retained unchanged**.
- `CalendarEventViewModel.LinkedCalendars (IReadOnlyList<EventCalendarDto>)` — carries all linked calendars per event.
- `ICalendarRepository` already includes `GetEventAsync`, `GetEventsAsync` (with `Calendars` navigation), `DeleteEventAsync`, and `RemoveCalendarAsync` (unlinks calendar from event; deletes event if orphaned).
- The existing `ReassignEventRequest` / `ReassignAsync` moves an event from one calendar to another atomically — **retired** (superseded by the new add/remove calendar endpoints).

---

## Data Model Changes

### New columns on `CalendarEvent`

| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| `OwnerCalendarInfoId` | `Guid` | No | FK to `CalendarInfo`. The calendar that is the Google organiser for this event. Hidden from the user. Used by the service layer to know which `calendarId` to pass to Google for `events.update`, `events.move`, and `events.delete`. |
| `IsExternallyOwned` | `bool` | No | `true` when Google's `organizer.self = false` at sync time (event was created by a calendar outside FamilyHQ). Informational / sync hint. Default `false`. **Not used for delete decisions** — a live check is performed instead (see Delete). |

`OwnerCalendarInfoId` has a FK to `CalendarInfo` with `DeleteBehavior.Restrict`. Before a calendar can be removed it must no longer be the owner (ownership must be transferred via `events.move` first).

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

The existing many-to-many configuration is **unchanged**.

### Migration

1. Add `OwnerCalendarInfoId (uuid NOT NULL DEFAULT '00000000-...')` to `Events`.
2. Backfill `OwnerCalendarInfoId` from the existing join table (all current events have exactly one calendar — that becomes the owner):
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
3. Add FK constraint `Events.OwnerCalendarInfoId → Calendars.Id` (RESTRICT).
4. Add `IsExternallyOwned (boolean NOT NULL DEFAULT false)` to `Events`.
5. Drop temporary `DEFAULT` clause on `OwnerCalendarInfoId`:
   ```sql
   ALTER TABLE "Events" ALTER COLUMN "OwnerCalendarInfoId" DROP DEFAULT;
   ```

---

## DTO and ViewModel Changes

### `CreateEventRequest` (updated)

```csharp
record CreateEventRequest(
    IReadOnlyList<Guid> CalendarInfoIds,   // min 1, no duplicates; first entry becomes organiser
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description);
```

### `UpdateEventRequest` (updated)

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

### `ReassignEventRequest` — removed.

### `CalendarEventDto` (updated)

`CalendarEventDto` is the wire type returned by the API. `CalendarEventViewModel` is a UI concern — it is never passed into or out of the API. The `CalendarApiService` (Blazor) maps `CalendarEventDto` → `CalendarEventViewModel`.

`LinkedCalendars` moves from the view model onto the DTO so the wire response carries all linked calendar metadata:

```csharp
record CalendarEventDto(
    Guid Id,
    string GoogleEventId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    IReadOnlyList<EventCalendarDto> Calendars);   // all linked calendars (id, name, colour)
```

`EventCalendarDto` is unchanged: `record EventCalendarDto(Guid Id, string DisplayName, string? Color)`.

`MonthViewDto.Days` currently holds `List<CalendarEventViewModel>` — this is updated to `List<CalendarEventDto>` to remove the ViewModel from the API surface. The Blazor `CalendarApiService` maps each `CalendarEventDto` to one or more `CalendarEventViewModel` entries (one per linked calendar) for grid rendering.

### `CalendarEventViewModel`

No API surface change. `LinkedCalendars` is **removed** from the view model — the grid expansion logic in `CalendarApiService` now creates one `CalendarEventViewModel` per entry in `CalendarEventDto.Calendars`, each carrying that calendar's colour and ID. The edit modal receives the originating `CalendarEventDto` (held alongside the view model) to access the full `Calendars` list for chip rendering.

---

## Validator Changes

### `CreateEventRequestValidator` (updated)
`CalendarInfoIds`: not null; at least one item; no duplicate Guids; each item non-empty.

### `UpdateEventRequestValidator` (new)
Validates `Title` (not empty, ≤ 200 chars), `Start` (not empty), `End` (not empty, ≥ `Start`). Replaces the existing controller bridge that adapted `UpdateEventRequest` into `CreateEventRequest` to reuse `CreateEventRequestValidator` — that adapter code is deleted.

---

## Backend API Changes

Multi-calendar operations are not scoped to a single calendar, so the new create, update, and delete endpoints move to an event-centric route prefix. The old calendar-scoped routes for these operations are **removed** — there is no production deployment to maintain backward compatibility with.

The `GET /api/calendars/events` query endpoint is unchanged (it is calendar-aware by design and returns all events across all visible calendars).

### Endpoint changes

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/api/calendars/events?year=&month=` | **Unchanged**. Returns `MonthViewDto` (now using `CalendarEventDto` instead of `CalendarEventViewModel`). |
| `GET` | `/api/calendars` | **Unchanged**. |
| `POST` | `/api/events` | **New** (replaces `/api/calendars/{calendarId}/events`). Creates event across one or more calendars. Returns 201 + `CalendarEventDto`. |
| `PUT` | `/api/events/{eventId}` | **New** (replaces `/api/calendars/{calendarId}/events/{eventId}`). Field edits (title, times, location, description). Returns 200 + `CalendarEventDto`. |
| `DELETE` | `/api/events/{eventId}` | **New** (replaces `/api/calendars/{calendarId}/events/{eventId}`). Full event delete with live external-attendee check. Returns 204. |
| `POST` | `/api/events/{eventId}/calendars/{calendarId}` | **New**. Add an internal calendar to an existing event. `calendarId` = `CalendarInfo.Id`. Returns 200 + `CalendarEventDto`. Idempotent. |
| `DELETE` | `/api/events/{eventId}/calendars/{calendarId}` | **New**. Remove one calendar from an event (not a full delete). Returns 204; 404 if calendar not linked. |
| `POST` | `/api/calendars/{calendarId}/events` | **Removed**. |
| `PUT` | `/api/calendars/{calendarId}/events/{eventId}` | **Removed**. |
| `DELETE` | `/api/calendars/{calendarId}/events/{eventId}` | **Removed**. |
| `POST` | `/api/calendars/{calendarId}/events/{eventId}/reassign` | **Removed**. |

### Controller

A new `EventsController` handles the five event-centric routes (`POST /api/events`, `PUT`, `DELETE`, `POST .../calendars/{calendarId}`, `DELETE .../calendars/{calendarId}`). The existing `CalendarsController` retains only the `GET /api/calendars` and `GET /api/calendars/events` endpoints.

### Response types

All event-mutating endpoints return `CalendarEventDto` (or 204 for deletes). `CalendarEventViewModel` is never in an API response.

---

## `ICalendarEventService` (revised)

```csharp
// Creates Google event in first calendar; patches attendees for additional calendars
Task<CalendarEvent> CreateAsync(CreateEventRequest request, CancellationToken ct);

// Applies field edits to the Google event via the owner calendar
Task<CalendarEvent> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct);

// Adds an internal calendar as a Google attendee; idempotent
Task<CalendarEvent> AddCalendarAsync(Guid eventId, Guid targetCalendarInfoId, CancellationToken ct);

// Removes an internal calendar; transfers ownership first if it is the organiser
Task RemoveCalendarAsync(Guid eventId, Guid calendarInfoId, CancellationToken ct);

// Deletes event with live external-attendee check
Task DeleteAsync(Guid eventId, CancellationToken ct);
```

`ReassignAsync` removed from interface and implementation.

---

## `IGoogleCalendarClient` Changes

### Additions

```csharp
// Patches the attendees array on the event (adds or removes; full replacement of the list)
Task PatchEventAttendeesAsync(
    string organizerCalendarId,
    string googleEventId,
    IEnumerable<string> attendeeGoogleCalendarIds,
    CancellationToken ct);

// Fetches live event detail for external-attendee check at delete time
Task<GoogleEventDetail?> GetEventAsync(
    string googleCalendarId,
    string googleEventId,
    CancellationToken ct);   // null = event not found (404)
```

`MoveEventAsync` is retained unchanged (used when owner calendar is removed).

### `GoogleEventDetail` — new type in `FamilyHQ.Core/DTOs`

```csharp
public record GoogleEventDetail(
    string Id,
    string? OrganizerEmail,
    IReadOnlyList<string> AttendeeEmails);
```

---

## Service Behaviour

### Ownership verification

Calendar ownership uses `GetCalendarsAsync()` (user-scoped by `ICurrentUserService`) and checks whether the target `CalendarInfo.Id` is present. `GetCalendarByIdAsync` is **not** used for ownership checks — it does not filter by user.

Event ownership is verified by loading the event via `GetEventAsync(id)` and confirming its `CalendarInfo` records belong to the current user via the same `GetCalendarsAsync()` set.

### External-attendee check (used in `DeleteAsync`)

Call `GetEventAsync(ownerCalendarInfo.GoogleCalendarId, googleEventId)`. Form the union of `OrganizerEmail` + `AttendeeEmails` from the response. Compare each email against the current user's `CalendarInfo.GoogleCalendarId` set. Any email not in that set is an external party.

- **External parties present**: delete from local DB only (unlink all calendars; if no links remain, delete `CalendarEvent` row). Do **not** call `events.delete` on Google — the event persists for external attendees.
- **No external parties**: call `events.delete` on Google, then delete locally.
- **`GetEventAsync` returns null** (event already gone from Google): skip Google delete; delete locally.

### Transaction and error-handling model

Google API calls are made **before** opening a DB transaction. If Google succeeds but the DB transaction fails, log a reconciliation error (operation name, event ID, calendar IDs affected) and rethrow. No automatic Google cleanup is attempted — this matches the existing `ReassignAsync` pattern.

### `CreateAsync`

1. Call `GetCalendarsAsync()` to get all user-owned calendars. Deduplicate and validate `CalendarInfoIds` against this set. Throw `ValidationException` for any unrecognised ID.
2. Take `CalendarInfoIds[0]` as organiser. Call `CreateEventAsync(organiserGoogleCalendarId, event)` → `GoogleEventId`.
3. If additional calendar IDs provided: call `PatchEventAttendeesAsync(organiserGoogleCalendarId, googleEventId, [additionalGoogleCalendarIds])`.
4. In a DB transaction: insert `CalendarEvent` with `OwnerCalendarInfoId = CalendarInfoIds[0]`, `IsExternallyOwned = false`; insert join table entries for all calendars.
5. Return inserted event (with `Calendars` navigation populated).
6. On DB failure after Google success: log reconciliation error, rethrow.

### `UpdateAsync`

1. Load event via `GetEventAsync(eventId)` (includes `CalendarInfo` navigation, includes `OwnerCalendarInfo`). Verify event belongs to current user. Throw `NotFoundException` if not found or not owned.
2. Call `UpdateEventAsync(ownerCalendarInfo.GoogleCalendarId, event)` on Google.
3. In a DB transaction: update event fields, call `SaveChangesAsync`.
4. On DB failure after Google success: log reconciliation error, rethrow.

### `AddCalendarAsync`

1. Load event and verify ownership (as above).
2. Verify `targetCalendarInfoId` is in user's calendar set. Throw `NotFoundException` if not.
3. If `targetCalendarInfoId` is already in the event's `Calendars` collection: return event immediately (idempotent).
4. Build updated attendee list: current attendees + `targetCalendarInfo.GoogleCalendarId`.
5. Call `PatchEventAttendeesAsync(ownerGoogleCalendarId, googleEventId, updatedAttendeeList)`.
6. In a DB transaction: insert join table entry. On unique-constraint violation (concurrent duplicate): re-fetch and return existing (idempotent). On other DB failure: log reconciliation error, rethrow.
7. Return updated event with all linked calendars.

### `RemoveCalendarAsync`

1. Load event and verify ownership.
2. Find the `CalendarInfo` entry matching `calendarInfoId` in the event's `Calendars` collection. Throw `NotFoundException` if not found.
3. Count linked calendars.
4. **If this is the last linked calendar**: delegate to `DeleteAsync(eventId)`.
5. **If this is the owner calendar** (i.e. `calendarInfoId == event.OwnerCalendarInfoId`) and others remain:
   - Pick any remaining linked calendar as the new owner.
   - Call `MoveEventAsync(currentOwnerGoogleCalendarId, googleEventId, newOwnerGoogleCalendarId)`.
   - Update `event.OwnerCalendarInfoId = newOwner.Id` in the DB transaction (step 6).
6. Build updated attendee list: all linked `GoogleCalendarId` values **except** the one being removed.
7. Call `PatchEventAttendeesAsync(newOrCurrentOwnerGoogleCalendarId, googleEventId, updatedAttendeeList)`.
8. In a DB transaction: remove join table entry; update `OwnerCalendarInfoId` if changed.
9. On DB failure after Google success: log reconciliation error, rethrow.

### `DeleteAsync`

1. Load event and verify ownership. Load `OwnerCalendarInfo`.
2. Perform external-attendee check (see above).
3. If no external parties: call `events.delete(ownerGoogleCalendarId, googleEventId)` on Google.
4. In a DB transaction: remove all join table entries for this event; delete `CalendarEvent` row.

---

## Grid Query Change

`GetEventsForMonthAsync` is modified to **expand multi-calendar events**: for an event with N linked calendars, return N `CalendarEventViewModel` entries, each populated with the colour and display name of its respective `CalendarInfo`. The existing `LinkedCalendars` property on `CalendarEventViewModel` continues to carry all linked calendars (so the edit modal can show all chips).

Each capsule on the grid represents one calendar's view of the event. Clicking any capsule opens the edit modal showing the full event with all linked calendar chips.

---

## UI Changes

### Chip selector (replaces calendar dropdown)

All in-scope calendars rendered as chips (colour dot + display name).

**Create mode**: chips start deselected. Client accumulates selections; no API call until Save. At least one chip required.

**Edit mode**: chips for calendars in `LinkedCalendars` are active; others inactive.
- Tap inactive chip → `POST .../calendars/{targetCalendarId}` (immediate API call); activates on 200.
- Tap active chip → `DELETE .../calendars/{targetCalendarId}` (immediate API call), unless last active chip.
- **Last chip protection**: last active chip renders without ✕. Tap is no-op. Tooltip: "Use Delete to remove this event entirely."

### Delete button

Unchanged visually. Calls existing `DELETE /api/calendars/{calendarId}/events/{eventId}`. Backend handles external-attendee check transparently. Returns 204.

### Grid

No structural changes. The expanded query response naturally produces multiple capsules.

---

## Testing

### Unit tests (TDD — written before implementation)

**Validators**
- `CreateEventRequest`: rejects null list; rejects empty list; rejects duplicates; rejects empty Guid; accepts one or more valid distinct Guids.
- `UpdateEventRequestValidator`: validates Title (not empty, ≤ 200), Start, End (≥ Start).

**Service — CreateAsync**
- Calls `CreateEventAsync` for organiser calendar.
- Calls `PatchEventAttendeesAsync` with additional calendar IDs when more than one calendar selected.
- Does not call `PatchEventAttendeesAsync` when only one calendar selected.
- All join table entries created; `OwnerCalendarInfoId` set to first calendar.
- `ValidationException` on unknown `CalendarInfoId`.
- Reconciliation error logged on DB failure after Google success.

**Service — UpdateAsync**
- Calls `UpdateEventAsync` on organiser's Google calendar ID.
- Updates DB fields in transaction.
- Reconciliation error logged on DB failure.
- `NotFoundException` when event not found or not owned by current user.

**Service — AddCalendarAsync**
- Calls `PatchEventAttendeesAsync` with updated full attendee list.
- Inserts join table entry.
- No Google call and no DB write when calendar already linked (idempotent).
- Returns existing event on concurrent unique-constraint violation.
- `NotFoundException` when `targetCalendarInfoId` not in user's calendar set.

**Service — RemoveCalendarAsync**
- Non-owner, non-last: calls `PatchEventAttendeesAsync` without the removed calendar; removes join entry.
- Owner, non-last: calls `MoveEventAsync` first, then `PatchEventAttendeesAsync`; updates `OwnerCalendarInfoId`.
- Last calendar: delegates to `DeleteAsync`.
- `NotFoundException` when `calendarInfoId` not in event's linked calendars.
- Reconciliation error logged on DB failure after Google success.

**Service — DeleteAsync**
- External attendee present: `GetEventAsync` called; `events.delete` skipped; local rows deleted.
- No external attendees: `events.delete` called; local rows deleted.
- `GetEventAsync` returns null: skips Google delete; deletes locally.

**Controller — EventsController**
- `POST /api/events`: auth enforced; returns 201 + `CalendarEventDto` with `Calendars` populated.
- `PUT /api/events/{eventId}`: auth enforced; returns 200 + `CalendarEventDto`.
- `DELETE /api/events/{eventId}`: auth enforced; returns 204.
- `POST /api/events/{eventId}/calendars/{calendarId}`: auth enforced; returns 200 + `CalendarEventDto`; idempotent (200 if already linked).
- `DELETE /api/events/{eventId}/calendars/{calendarId}`: auth enforced; returns 204; 404 on `NotFoundException`.

**Grid query**
- `GetEventsForMonth_EventLinkedToTwoCalendars_ReturnsTwoDtos`: `MonthViewDto.Days` contains two `CalendarEventDto` entries for the same event (one per linked calendar), each with the correct `Calendars` list populated.

**CalendarApiService (Blazor)**
- Maps `CalendarEventDto` → one `CalendarEventViewModel` per entry in `Calendars` for grid rendering.
- Edit modal receives the originating `CalendarEventDto` for chip rendering.

### E2E / acceptance tests (BDD)

- Create event in single calendar → appears once on grid in that calendar's colour.
- Create event in two calendars → appears twice on grid, once per calendar colour.
- Edit event → add calendar via chip → event appears in both calendars on grid.
- Edit event → remove non-last calendar chip → event removed from that calendar, remains on other.
- Last chip protection → tapping last active chip does nothing; Delete is the only exit.
- Delete event, no external attendees → deleted from Google and FamilyHQ.
- Edit event fields (title) → change visible on all capsules for that event.
- **Edge case**: event created externally, internal calendar invited, external later removes itself → user deletes → full Google delete, no zombie event.
- **Edge case**: event created externally, external still an attendee → user deletes → local-only delete; Google event persists.
- **Simulator**: `events.list(calendarId=B)` returns event where B is an attendee (not organiser), confirming simulator routes by attendee membership.

---

## Out of Scope

- Events across multiple Google accounts.
- Displaying external attendee names in the FamilyHQ UI.
- Sync-time deduplication of events discovered via attendee routes (current sync processes each calendar independently; a future iteration may deduplicate by `iCalUID`).
