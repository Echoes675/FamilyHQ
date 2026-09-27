# Simulator External Dependencies

## Principle

All external API dependencies are abstracted behind interfaces with config-driven base URLs. In production, URLs point to real services. In dev/staging/E2E testing, URLs point to the simulator. The same application code runs in all environments — only the config-driven base URL changes per environment.

## Current Instances

| External Service | Config Key | Production URL | Dev/Staging URL |
|---|---|---|---|
| Google Calendar API | `GoogleCalendar:CalendarApiBaseUrl` | `https://www.googleapis.com` | `https://localhost:7199` (simulator) |
| Google OAuth | `GoogleCalendar:AuthBaseUrl` | `https://accounts.google.com` | `https://localhost:7199` (simulator) |
| Google Calendar Watch | `GoogleCalendar:CalendarApiBaseUrl` | `https://www.googleapis.com` | `https://localhost:7199` (simulator) |
| Open-Meteo Weather API | `Weather:BaseUrl` | `https://api.open-meteo.com` | `https://localhost:7199` (simulator) |
| Nominatim Geocoding | `Geocoding:BaseUrl` | `https://nominatim.openstreetmap.org` | `https://localhost:7199` (simulator) |
| ip-api.com IP Geolocation | `Location:IpApiBaseUrl` | `http://ip-api.com` | `https://localhost:7199` (simulator) |

## Pattern

The simulator implements each external API's response format, for example:
- Nominatim `/search?q=...` response shape
- Open-Meteo `/v1/forecast` response shape

This means production and test code follow identical code paths. No test-only branches or conditional logic in the application.

## Simulator Backdoor Endpoints

For E2E test isolation, the simulator exposes `POST/DELETE /api/simulator/backdoor/*` endpoints that let tests inject and clear data:

| Endpoint | Purpose |
|---|---|
| `POST /api/simulator/backdoor/weather` | Seed weather data by lat/lon |
| `DELETE /api/simulator/backdoor/weather?latitude=X&longitude=Y` | Clear weather data |
| `POST /api/simulator/backdoor/location` | Seed geocoding result by place name |
| `DELETE /api/simulator/backdoor/location?placeName=X` | Clear geocoding result |
| `POST /api/simulator/configure` | Configure user templates |
| `POST /api/simulator/backdoor/events` | Seed calendar events |
| `GET /api/simulator/backdoor/webhooks` | Query registered watch channels |
| `DELETE /api/simulator/backdoor/webhooks` | Clear registered watch channels |
| `PUT /api/simulator/backdoor/calendars/{calendarId}/default-reminders` | Change a calendar's Google-side `defaultReminders` mid-run (body `{"overrides":[{"method":"popup","minutes":45}]}`, or `{"overrides":null}` to clear). FHQ-207: without a *change*, `RefreshCalendarDefaultsAsync` early-returns and CI never reaches the write path that caused the FHQ-205 outage. |

## Event Reminders

Google's reminder behaviour was captured from live API responses and is modelled in
`tools/FamilyHQ.Simulator/Google/ReminderSemantics.cs`. The three write paths (insert, update, patch)
and the read projection in `EventsController` all go through it.

### What is faithfully modelled

These are **Google's** behaviours, not this class's inventions:

| Request | What Google does |
|---|---|
| `minutes` below 0 | clamps to `0`, HTTP 200 |
| `minutes` above 40320 (four weeks) | clamps to `40320`, HTTP 200 |
| duplicate `(method, minutes)` | de-duplicates, HTTP 200 |
| `method` other than `popup`/`email` | drops that override **entirely**, HTTP 200 |
| a 6th override | `400 eventRemindersCountExceedsLimit` |
| `useDefault:true` **with** a non-empty `overrides` | `400 cannotUseDefaultRemindersAndSpecifyOverride` |
| `useDefault:true, overrides:[]` | accepted — this is how a client reverts to the calendar default |
| a PATCH carrying `reminders` | replaces the whole object; it does not merge into it |
| a PATCH omitting `reminders` | leaves the stored value untouched |

| Read | What Google returns |
|---|---|
| nothing stored, **timed** event | `{"useDefault":true}` |
| nothing stored, **all-day** event | `{"useDefault":false,"overrides":[…the calendar's defaults…]}` — Google *materialises* them, so an all-day event never inherits |
| stored `useDefault:false, overrides:[]` | `{"useDefault":false}` — **no `overrides` key** |
| any overrides | returned **reordered**; the order they were sent in is never preserved |
| an expanded instance of a series | the master's reminders |
| an exception occurrence | its own reminders |

Both rejections sit in Google's `calendar` error domain, and both carry Google's own `message` text.

### Why the Simulator accepts bad input

Because Google does. It answers `200` and silently rewrites. A Simulator that rejected the same input
would be the more correct-*looking* double and the more dangerous one: the kiosk's tests would then
assert an error path production never takes, and the silent-rewrite path — where a caller believes it
set a reminder that does not exist — would go untested.

### Known divergence: reminders that fire after the event starts

Google can store a reminder *after* an all-day event starts ("on the day at 09:00") and shows it in
its own UI, but **never returns it from the API**. The response is `{"useDefault":false}` with no
overrides — byte-for-byte identical to "explicitly no reminders". The Simulator cannot model a state
the API does not expose, and neither can FamilyHQ.

The consequence worth carrying: on an all-day event, `useDefault:false` with no overrides means *"no
reminders, **or** reminders the API will not show you"*. Nothing may describe that state to a user as
"no reminders".

### Known divergence: `etag` and `updated` are not modelled at all

Neither field exists on the simulated event, so neither behaviour can be observed here. Against real
Google, a reminder-only change **leaves `updated` untouched** and **moves the `etag`** (and does
appear in a sync-token delta). Anything that decided whether an event had changed by comparing
`updated` would therefore be wrong against real Google — and this double would not catch it.

## Seeded Locations

The simulator seeds the following locations on startup for manual testing. Enter any of these place names on the Settings page to save a location and see weather data.

| Place Name | Latitude | Longitude |
|---|---|---|
| Edinburgh, Scotland | 55.9533 | -3.1883 |
| London, England | 51.5074 | -0.1278 |
| Dublin, Ireland | 53.3498 | -6.2603 |
| New York, USA | 40.7128 | -74.0060 |
| Tokyo, Japan | 35.6762 | 139.6503 |
| Sydney, Australia | -33.8688 | 151.2093 |

The geocoding controller uses fuzzy matching (`ILIKE %q%`), so partial names like "Edinburgh" or "Tokyo" will also work.

## Adding a New External Dependency

1. Create an interface in `FamilyHQ.Core`
2. Implement against the real API in `FamilyHQ.Services`
3. Register with `AddHttpClient` using a config-driven base URL in `Program.cs`
4. Add a simulator controller that mimics the external API's response format
5. Add a backdoor controller for E2E test data injection
6. Set dev/staging config to point to the simulator
