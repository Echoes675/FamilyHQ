# Architecture & Structure

## Project Layout
- src/FamilyHQ.WebUi/: Blazor WASM UI.
- src/FamilyHQ.WebApi/: ASP.NET Core API.
- src/FamilyHQ.Services/: Business logic and orchestration.
- src/FamilyHQ.Data/: EF Core context and all provider-agnostic repositories (pure EF Core, no Npgsql). Includes the shared model convention that fails the build if CalendarSyncJob lacks a concurrency token (FHQ-146).
- src/FamilyHQ.Data.PostgreSQL/: PostgreSQL-specific only — NpgsqlModelCustomizer (xmin token), migrations, UniqueConstraintExceptionInterceptor, DI wiring, design-time factory.
- src/FamilyHQ.Core/: Shared Models, DTOs, and FluentValidation logic.

## Deployment Context
- **Kiosk device**: Raspberry Pi 3B+ running Chromium in kiosk mode (`--kiosk --touch-events=enabled`).
- **Display**: 27" 1080p touchscreen in **portrait orientation** (1080×1920 effective).
- **No physical keyboard/mouse**: all input is touch. Virtual keyboard (`matchbox-keyboard` or `onboard`) is invoked automatically by Chromium on input focus.
- **WebApi** is deployed to a separate web server; the Pi accesses it over the network.
- **Performance constraint**: avoid `backdrop-filter: blur()`, heavy JS animation loops, and canvas/WebGL — all are too expensive for the Pi 3B+ GPU/CPU.

## Dependency Rules
- Directional Flow: Dependencies must flow inward.
-- WebUi and WebApi -> Services -> Data -> Core.
-- Forbidden: Never add references from Core or Services back to the Web projects.
- Shared Logic: All DTOs, Enums, and Constants used by both Client and Server must reside in FamilyHQ.Core.

## Technical Principles
- Clean Architecture: Ensure the WebApi and WebUi projects only depend on Services or Core.
- Infrastructure Isolation: External integrations (e.g., Google Calendar) must be abstracted behind interfaces. See `.agent/docs/simulator-external-dependencies.md` for the external dependency mocking strategy.
- Shared Validation: Use FluentValidation in FamilyHQ.Core so it can be executed on both the Blazor client and the ASP.NET server.

## Key Entities
- **CalendarEvent**: Google Calendar event data.
- **DayTheme**: Stores the 4 time-of-day period boundaries (MorningStart, DaytimeStart, EveningStart, NightStart as TimeOnly) for a given Date, **per kiosk** — unique on (UserId, Date) since FHQ-177. Calculated once per day per kiosk by DayThemeSchedulerService from sunrise/sunset at that kiosk's **saved LocationSetting**. A kiosk with no saved location gets no row and keeps its default theme: the boundaries used to come from a server-side IP lookup, which geolocates the hosting VPS rather than the family, so guessing is choosing a known-wrong answer.
- **LocationSetting**: Stores the user's configured location (PlaceName, Latitude, Longitude). One row per UserId; when absent, the API falls back to IP-based geolocation.
- **DisplaySetting**: Stores user display preferences (SurfaceMultiplier as `double` 0–1.0, OpaqueSurfaces as `bool`, TransitionDurationSecs as `int`, ThemeSelection as `string`). One row per UserId. ThemeSelection is `"auto"` (time-of-day transitions) or a period name (`"morning"`, `"daytime"`, `"evening"`, `"night"`).
- **WeatherDataPoint**: Stores weather data (current, hourly, daily) for a location. Keyed by LocationSettingId + DataType + Timestamp. `Condition` is persisted as the `WeatherCondition` **ordinal**, so new enum members must be appended — inserting one re-labels every stored row (pinned by `WeatherConditionTests`).
- **WeatherSetting**: Stores weather preferences (Enabled, PollIntervalMinutes, TemperatureUnit, WindThresholdKmh). One row per UserId.
- **WebhookRegistration**: Tracks Google Calendar push notification watch channel registrations. One row per CalendarInfo. Stores ChannelId (UUID sent to Google), ResourceId (returned by Google), ExpiresAt, RegisteredAt.

## Key Services
- **ISunCalculatorService / SunCalculatorService**: Calculates sunrise/sunset times for a lat/lon using the SunCalcNet NuGet package.
- **IDayThemeService / DayThemeService**: Calculates and persists today's DayTheme boundaries for one kiosk (every method takes a `userId`). Reads the kiosk's `LocationSetting` and derives the zone from its coordinates — never IP geolocation.
- **DayThemeSchedulerService** (IHostedService): On startup, ensures today's DayTheme exists for every kiosk with a saved location. Loops using Task.Delay to wake at the **earliest** upcoming boundary across all kiosks, then broadcasts a payload-free `ThemeChanged` signal via IHubContext<CalendarHub>. Each kiosk's calculation is guarded independently, so one bad location cannot deny the others a theme.
- **ILocationService / LocationService**: Returns the effective location — saved LocationSetting from DB if present, otherwise IP-based geolocation (ip-api.com free tier) as fallback. A `status != "success"` body is a hard failure, never retried: ip-api's three documented fail messages (`private range`, `reserved range`, `invalid query`) are all permanent for the querying IP. Its rate limiting is a separate HTTP 429 (+ `X-Ttl`) signal, absorbed by the retry handler below (FHQ-114).
- **IGeocodingService / GeocodingService**: Geocodes a place name string to lat/lon using the Nominatim (OpenStreetMap) API. No API key required. Base URL is config-driven — Nominatim in production, simulator in dev/staging.
- **TransientHttpRetryHandler** (`DelegatingHandler`, FHQ-114): retry for the three non-Google outbound clients — ip-api, Nominatim, Open-Meteo — all registered in `AddFamilyHqServices`. Retries idempotent (GET/HEAD) requests on 408/429/5xx and connection failures, honouring `Retry-After` (and ip-api's `X-Ttl`, **429 only** — `X-Ttl` is a rate-limit-window counter paired with `X-Rl` and ships on non-throttled responses too), otherwise exponential backoff with jitter. A **429 with no hint** (Open-Meteo's shape) is surfaced un-retried so caller-level backoff owns it rather than spending more of an exhausted quota. Every sleep is capped by `MaxRetryDelay` on **both** the response and the connection-failure path; anything longer surfaces immediately. Sleeps happen inside `SendAsync`, so each client's `Timeout` is the TOTAL budget for the attempt+backoff sequence — see `ExternalHttpResilienceOptions` for the worst-case arithmetic, and note the two interactive clients (ip-api, Nominatim — both awaited by `GET`/`POST /api/settings/location` with no client-side timeout) are budgeted near their pre-retry 10s ceiling rather than the background client's. (Contrast `ResilientGoogleCalendarClient`, which decorates an interface because the Google SDK is not a plain `HttpClient`.)
- **IDisplaySettingService / DisplaySettingService** (Blazor WASM): Loads display preferences from `GET /api/settings/display` on startup and applies `--user-surface-multiplier` and `--theme-transition-duration` CSS custom properties via JS interop. Saves changes via `PUT /api/settings/display`.
- **IWeatherProvider / OpenMeteoWeatherProvider**: Fetches weather data from Open-Meteo (or simulator). Base URL from config — same code in all environments. Open-Meteo's parallel value arrays are not guaranteed to match the length of `time`, so the hourly/daily loops run to the shortest array present (FHQ-110) and log one Warning per ragged section per parse.
- **IWmoCodeMapper / WmoCodeMapper** (singleton, pure lookup): Maps Open-Meteo WMO weather codes to `WeatherCondition`. An unrecognised code yields `WeatherCondition.Unknown` — never `Clear` (FHQ-115) — and is reported back to `OpenMeteoWeatherProvider`, which emits a single aggregated Warning naming every distinct unmapped code per parse. `WeatherCondition.Unknown` maps to the `"unknown"` icon (a dashed cloud in `WeatherIcon.razor`) and shows no overlay animation.
- **IWeatherService / WeatherService**: Reads stored weather data, applies temperature conversion, serves DTOs.
- **IWeatherRefreshService**: Shared between WeatherPollerService and the refresh endpoint. Extracts the poll logic (fetch, store, broadcast) into a reusable service.
- **WeatherPollerService** (IHostedService): Background poller that fetches weather data at configurable intervals and broadcasts `WeatherUpdated` via SignalR. `SettingsController.SaveLocation` also triggers an immediate weather refresh after saving. Each cycle refreshes only the users that are **due**; a user's interval doubles per consecutive failure up to `Weather:MaxFailureBackoffMinutes` and resets on the first success (FHQ-109), so a rate-limited Open-Meteo is no longer re-hit every 60s. Per-user state is pruned to the current enabled-user set each cycle, so it cannot grow with churn. The cycle sleep is clamped to `Weather:PollIntervalMinutes` so a backed-off user never stops the loop discovering someone who has just enabled weather. Only the escalation transition is logged at Error; once the interval plateaus at the cap it drops to Debug, with a Warning re-emitted every 10th consecutive failure so an ongoing outage stays visible in production.
- **IWebhookRegistrationService / WebhookRegistrationService**: Registers Google Calendar push notification watch channels per-calendar. Called after login and periodically by WebhookRenewalService. Config-gated via `Sync:WebhookRegistrationEnabled`.
- **WebhookRenewalService** (IHostedService, lives in WebApi): Background service that re-registers all webhook watch channels on startup (1-min delay) and every 6 days. Iterates all users via ITokenStore. Disabled when `Sync:WebhookRegistrationEnabled` is false.
- **IWeatherUiService / WeatherUiService** (Blazor WASM): Fetches weather data via HTTP, subscribes to SignalR `WeatherUpdated` events, exposes `OnWeatherChanged` for components.

## Google write paths — an event carries its own time zone (FHQ-170)

**Rule: a `CalendarEvent` handed to a Google write must carry the zone Google anchored it to. The family's configured zone (`DisplaySetting.IanaTimeZone`, via `ITimeZoneService.GetSendZoneAsync`) is a fallback for data Google never supplied — never a replacement for data it did.**

`GoogleCalendarClient.MapToGoogleEvent` sends an explicit `start.timeZone`/`end.timeZone` on every timed write, because that is the zone Google expands a series' future occurrences in (FHQ-43). `ResolveOutboundZone` therefore prefers the event's own `IanaTimeZone` and reaches for the family's only when the event has none. That makes the property the whole invariant: an event that arrives at a write without it does not fail — it is silently re-anchored, and every future occurrence moves by an hour at the next transition where the two zones differ, in the Google Calendar app, for everyone the calendar is shared with.

What that means for anyone touching these paths:

- **Any newly-built `CalendarEvent` bound for `CreateEventAsync`, `CreateRecurringEventAsync`, `PatchEventFieldsAsync` or `PatchEventFieldsPreservingTimesAsync` must state a zone** — the one the event or its series already carries, or an explicit `null` where there genuinely is no prior zone to preserve (a brand-new event, in `CalendarEventService.CreateAsync`). `tests/FamilyHQ.Core.Tests/OutboundZoneGuardTests.cs` fails the build on a construction that states neither. It is a lexical tripwire, not a proof — its XML doc lists what it cannot see.
- **A row loaded from the database already carries it**, so an in-place edit needs nothing extra. The hazard is specifically the freshly-constructed object: a patched series master, the forward half of a "this and following" split, a series moved between calendars.
- **All-day events carry no zone by design** (they are date-anchored, so DST cannot move them) and that branch never reads one.
- **Where the value comes from**: sync stores Google's `start.timeZone` for every event it touches and adopts each calendar's own default zone from the calendar list, so the backfill is lazy and costs no extra API call. When a series' row still has none, `CalendarEventService` asks Google rather than guessing — stored → series master → surviving instance → calendar default → fixed-UTC enumeration with a Warning (FHQ-164 Decision 2). A candidate the tz database cannot resolve is skipped rather than accepted, because the outbound write would reject it too.

## Google write paths — a series' origin is never guessed (FHQ-172)

**Rule: the earliest locally-synced row is not the series' origin, and nothing derived from it may reach a Google write.**

Two recurring write paths need the series master's DTSTART: the AllInSeries edit writes `anchor + shift` back as the master's new start, and the "this and following" COUNT split derives the forward series' remaining count from it. `CalendarEventService.ResolveSeriesAnchorAsync` supplies it, and falls back to the earliest local row when Google returns no master. That row is a **proxy**: when the master predates the sync window it sits *later* than the true origin, so writing it relocates the series forward — deleting every occurrence before the window from Google and from every device — and counting from it leaves the forward series too long. `startShift` is zero for a pure title edit, so renaming a series was enough to do the damage.

- **`GetSeriesMasterAsync` returns the start even with no `RRULE:` line.** `SeriesMaster.Rrule` is nullable; only a missing master (404) or an unparseable start yields null. An RDATE-only master (an ICS/CalDAV import) used to be discarded whole, which is what made the degraded path reachable while the master was alive in Google. `CalendarSyncService`'s RRULE cache treats "no rule" exactly as it treated "no master": cache nothing, warn, retry next sync.
- **AllInSeries with an unresolved anchor**: if the request changes the start, the duration or the all-day flag, it is refused (`SeriesOriginUnresolvedException`) because the new origin is a function of the unknown old one. Otherwise the edit lands through `PatchEventFieldsPreservingTimesAsync`, whose body carries **no `start`/`end` keys at all** — events.patch merges, so Google keeps its own DTSTART.
- **What actually fixes the reported defect is the bullet above about the RRULE-less master**, not the omit-times patch. Say so plainly, because the omit-times path is the eye-catching part: once `GetSeriesMasterAsync` stops discarding a usable DTSTART, the only remaining route to an unresolved anchor is a master `events.get` that 404s or yields no parsable start — and an `events.patch` to that same id would 404 too, so **no production shape is known in which the omit-times write both fires and succeeds**. It is kept as defence-in-depth: the failure it guards against is the irreversible deletion of a family's series history, which justifies a branch that may never run. Do not describe it as the mechanism that fixed FHQ-172.
- **The COUNT split with an unresolved anchor is refused outright**, and `SplitSeriesAsync` now resolves *before* it truncates, so the refusal leaves Google untouched. (The remaining window — truncate succeeds, forward-series create fails — is FHQ-173, closed by the section below.) A Never/UNTIL split needs no count and never resolves an anchor, so it is unaffected. The reorder also means the zone ladder's backfill commits before any Google write; that is benign — it caches a zone Google supplied, and records nothing about a write having happened.
- **The content hash for the omitted-times patch excludes the times *and* the all-day flag** (`ComputeHashWithoutTimes`). The hash is an opaque token round-tripped through `extendedProperties` for the echo guard, so the proxy would not have broken it — but the token must describe what was sent. All-day-ness reaches Google only through `start.date` vs `start.dateTime`, so a body with neither key sends no flag either; excluding it is safe because a flip is classified as a timing change and refused before this write.
- **One Warning per incident.** The anchor site logs at `Debug` — it reports a fact and decides nothing, and one caller handles the missing origin completely successfully. Each refusing caller logs exactly one `Warning`. `DomainExceptionHandler` logs a second when it maps the exception, but that line names only the status, method and path, so the service line is the only record of the cause.

## Google write paths — a split never leaves a hole (FHQ-173)

**Rule: of the two Google mutations a "this and following" edit makes, the one that can only ADD is written first — and the second is undone only on positive evidence that it never committed.**

`CalendarEventService.SplitSeriesAsync` truncates the original master (`PatchSeriesRecurrenceAsync`, `UNTIL` = split − 1s) and inserts the forward series (`CreateRecurringEventAsync`). There is nothing transactional between them, so whichever runs first has already committed when the second fails — and the order alone decides what the family is left with.

- **Create first, truncate second.** Truncating first meant a failed create left the series chopped at the split with *nothing* replacing it: every occurrence from the split onwards gone, on every device, irreversibly — FamilyHQ is not the system of record. Creating first degrades that to a *duplicate overlapping series*: visible, non-destructive, and fixable by the family in the Google Calendar app. Nothing about the create depends on the truncation (`freshRule` comes from the reshape, which runs before either), so the swap costs nothing. The duplicate window on the success path is a fraction of a second and converges on the next sync.
- **A failed truncation is compensated ONLY when the failure proves Google did not process it** — the forward series is then deleted **by the id the create returned**, never a derived or guessed id, on a calendar where a wrong guess deletes a real family event. In that case, and only that case, the calendar is restored to exactly its pre-edit state.
- **Under ambiguity, prefer the recoverable outcome: leave the duplicate.** A 5xx, a timeout, a dropped connection or an unrecognised exception all leave it possible that Google applied the truncation and lost the response. Deleting the forward series then leaves the original truncated with its replacement removed — the hole this section exists to prevent, actively created rather than merely risked. A duplicate is visible and user-correctable; a hole is silent, permanent data loss on the system of record.
  - **This is the case the original design argument missed, so it is named here to stop it coming back.** That argument ran: "if the compensating delete fails in turn we land in the duplicate state, so compensation can never be worse than not compensating." It enumerates only the delete *failing*. The delete **succeeding against a truncation that actually committed** is the destructive case, and `PatchSeriesRecurrenceAsync` runs under `RetryPolicy.Full`, where a 5xx is explicitly modelled as "may have been processed".
  - **Re-reading the master's RRULE to settle the ambiguity was considered and rejected.** A stale read produces the hole directly, and Google's real read-after-write behaviour cannot be verified against the Simulator (see the prime directive in `AGENTS.md`).
- **"May have been processed" is one shared predicate, not two copies.** `GoogleWriteOutcome.MayHaveBeenProcessed` (`src/FamilyHQ.Services/Auth/`) answers it for both `ResilientGoogleCalendarClient.ShouldRetry` — which repeats a 5xx only for idempotent operations — and this compensator, which refuses to undo one. Two statements of the same rule would drift silently, and the two sites turn on it in opposite directions. It sits beside `GoogleApiException` rather than in `FamilyHQ.Core` because the exception types it classifies live in `FamilyHQ.Services.Auth`; `FamilyHQ.Core` gains no dependency. **The default answer is "yes, it may have been processed"** — only a status code Google itself returned (a 4xx) counts as evidence of a rejection.
- **Reauth and genuine cancellation are not compensated, and never swallowed.** A `GoogleReauthRequiredException` means the credentials themselves are the failure, so the delete would be rejected identically. A genuinely cancelled caller token leaves nothing to write with, and reaching for `CancellationToken.None` would issue a fresh write for an abandoned request. Both propagate to the caller (reauth is what raises the reconnect banner), and the residual duplicate is reported.
  - **The cancellation test is on the TOKEN (`ct.IsCancellationRequested`), not the exception type.** `TaskCanceledException` derives from `OperationCanceledException`, and FHQ-91's per-attempt HttpClient timeout arrives as exactly that with the caller's token untouched (`ResilientGoogleCalendarClient` identifies it that way; `DomainExceptionHandler` maps it to 504). A type test alone would call a timeout a cancellation. A timeout must skip compensation because it *may have been processed*, not because anything was cancelled — the two rules compose, and they are tested separately.
- **The original truncation exception is what reaches the caller.** A clean-up failure must not replace the failure the user's edit actually hit, so it is logged rather than thrown — both are preserved: the truncation failure in the response, the clean-up failure as the `Error`'s exception argument, with its type and stack, in Seq.
- **A create that may have been processed is reported too.** `CreateRecurringEventAsync` runs under `RetryPolicy.RejectedOnly`, so a 5xx, a timeout or a dropped connection is never repeated and no id comes back: Google may hold a forward series FamilyHQ has no record of and no handle on. That residual state logs `Error` naming the original series and the owning calendar. A definitely-rejected create wrote nothing and logs nothing — the exception the caller already gets *is* the report.
- **Local rows are pruned only when BOTH writes land.** `RemoveSeriesRowsFromSplitAsync` after a failed truncation would delete the family's occurrences locally to match a truncation that never happened.
- **No outbound-hash un-recording.** The hash recorded for the created series is left in the 60-second cache when the compensation deletes it: a Google delete comes back as a `CANCELLED_TOMBSTONE` carrying no `content-hash`, so `IsSelfEcho` never consults the entry; the id can never be reused; and suppressing an echo of the pre-delete state would be *correct* anyway, since it really was our write. `IOutboundWriteHashCache` has no removal method and gains nothing from one.
- **One Error from this service per incident.** Successful compensation logs `Information` — nothing is degraded and nothing is left behind, which the logging standard classes as expected-and-handled. Only a residual state logs `Error`, naming both series ids and the calendar by FamilyHQ's own id (a Google calendar id is an email address, FHQ-166). This is a rule about not double-reporting *from `CalendarEventService`*, not a claim about what Seq shows for the request: `ResilientGoogleCalendarClient` warns once per retry attempt, `DeleteEventAsync` warns on a 404, and `DomainExceptionHandler` warns again when it maps the rethrown failure.

## Webhook echo guard

FamilyHQ writes to Google Calendar via `CalendarEventService` and `CalendarMigrationService`. Each write computes a SHA256 over `(title, start, end, isAllDay, description)` via `EventContentHash` and stores the hex hash as `extendedProperties.private["content-hash"]` on the Google event. Google's resulting push notification then arrives at `SyncController.GooglePushWebhook`, which enqueues a durable sync job; `CalendarSyncWorker` later dispatches it to `CalendarSyncService.SyncAsync` / `SyncAllAsync` (see "Durable calendar sync queue" below).

The guard is implemented in two halves:

1. **Outbound** — every successful Google write records `(GoogleEventId, hash)` in a singleton `IOutboundWriteHashCache` with a 60-second TTL. Failed writes do not record.
2. **Inbound** — `CalendarSyncService.SyncCoreAsync` reads the content-hash from each inbound `CalendarEvent.ContentHash` (carried through from `GoogleApiEvent.ExtendedProperties.Private.ContentHash` via the `events.list` `fields=` allowlist) and consults the cache. On match, the event is skipped: no DB write, no further Google write, single "Self-echo skipped" Information-level log entry.

### Production verification

To verify the guard is active in any environment:

1. Make a single edit to a calendar event through the FamilyHQ UI.
2. Within ~5 seconds, the application log should contain:
   - At Debug: one or more `Recorded outbound write hash for event ...` entries.
   - At Information: at least one `Self-echo skipped for event ... (hash ...)` entry.
3. Zero "Self-echo skipped" entries across a day of writes suggests the guard isn't being hit — investigate.

### Why this matters for recurring events (FHQ-18)

A single PATCH on a series master can produce webhooks for every expanded instance Google touches. The guard ensures all such echoes are skipped cleanly, eliminating the latent loop risk that single-event writes avoid only through convergent upserts.

## Durable calendar sync queue (FHQ-37)

Google Calendar push notifications no longer run the sync on the HTTP request thread. Doing so (FHQ-36) meant Google's short webhook-ack deadline could elapse mid-sync; nginx then aborted the upstream connection, the request `CancellationToken` tripped, and `SaveChangesAsync` was cancelled mid-write — so the change never persisted and the kiosk never updated, even though a manual sync worked. The webhook is now a fast producer onto a durable queue:

1. **Enqueue + ack** — `SyncController.GooglePushWebhook` validates the notification, enqueues a `CalendarSyncJob` (targeted when the channel maps to a calendar, else a sync-all job per user), releases the in-process `ISyncJobSignal`, and returns `200` immediately. It never runs a sync inline and never passes the request `CancellationToken` into sync work. Enqueue failures are logged but still ack `200` (a 200 stops Google's retries; the periodic safety net reconciles).
2. **Durable store** — `CalendarSyncJob` (table `CalendarSyncJobs`, EF-mapped, migration `AddCalendarSyncJobQueue`) holds `UserId`, optional `CalendarInfoId` (null = sync-all), `Status` (Pending/InProgress/Completed/Failed), `Source` (Webhook/Periodic), attempt count, last error, and timing columns. A partial unique index keeps at most one Pending job per `(UserId, CalendarInfoId)` for coalescing. All queue operations are EF Core (no raw SQL); `ICalendarSyncJobQueue` / `CalendarSyncJobRepository` provides enqueue (coalescing), claim, complete, fail (retryable backoff vs terminal), orphan recovery, prune, and recent-failures read.
3. **Consumer** — `CalendarSyncWorker` (IHostedService in WebApi) is a single sequential consumer. It waits on the signal (with a poll backstop), recovers orphaned `InProgress` jobs, then drains `Pending` jobs one at a time, each in its own DI scope. Per job it sets `BackgroundUserContext.Current`, runs `CalendarSyncService.SyncAsync`/`SyncAllAsync` with **`CancellationToken.None`** (decoupled from any request/shutdown token — the FHQ-36 fix), and on success persists then broadcasts `EventsUpdated` over SignalR so the kiosk re-fetches. A genuine auth failure (`GoogleReauthRequiredException` — a 401, or a non-rate-limit 403) marks the user needs-reauth and fails the job terminally; other exceptions — including 429/5xx and rate/quota 403s, which surface as a transient `GoogleApiException` rather than a reauth signal (FHQ-83) — fail retryable with exponential backoff, honouring Google's `Retry-After` as a floor when supplied, until `MaxSyncAttempts`. Ahead of this job-level layer, every Google API call passes through a per-request retry decorator (`ResilientGoogleCalendarClient`, FHQ-154) that absorbs momentary 429 / rate-403 / 5xx blips — idempotency-aware, so a create/watch/move is never retried on a 5xx — and honours `Retry-After` (rethrowing rather than sleeping past a short in-request cap); a rate-limit 403 that reaches a foreground caller surfaces as **503 + `Retry-After`** (folded in with 429). Failed jobs are terminal audit rows that never block new enqueues.

Tunables live in `SyncOptions`: `WorkerPollInterval`, `OrphanRecoveryThreshold`, `MaxSyncAttempts`, `RetryBackoffBaseSeconds`, `TerminalJobRetention`.

The periodic safety-net timer feeds the same queue (**FHQ-38**): `SyncOrchestrator` (IHostedService) wakes every `PeriodicSyncInterval`, enumerates registered users via `ITokenStore.GetAllUserIdsAsync`, and enqueues one coalesced `Periodic` sync-all `CalendarSyncJob` per user, then releases the signal — the same producer pattern as the webhook fallback. (Previously it called `SyncAllAsync` directly with no user context and aborted on the null-user guard — a complete no-op.) Because both the webhook and the periodic timer now produce the same job type drained by the same worker, they share the user-context, cancellation-decoupling, and broadcast-after-persist behaviour automatically.

### Run-level failure diagnostics

Distinct from the per-event `SyncEventFailure` subsystem (individual events that could not be saved), the queue records whole-run failures. `GET /api/diagnostics/failed-sync-runs` returns the current user's recent terminally-failed runs (`FailedSyncRunDto`), surfaced on the Diagnostics tab of the Settings page in a third "Recent failed sync runs" section (`data-testid="diagnostics-runs-table"`). These re-run automatically on the next change, so they are informational, not action items.

## API Endpoints
- `GET  /api/daytheme/today` → DayThemeDto (Date + 4 boundary times + current period) — **requires auth**; the caller's identity selects the kiosk. `204 No Content` when that kiosk has no saved location. Anonymous access was removed in FHQ-177: the response's timezone and solar times together disclose roughly where the family lives.
- `GET  /api/settings/location` → LocationSettingDto or 404
- `POST /api/settings/location` `{ placeName }` → geocodes, saves, returns LocationSettingDto
- `PUT  /api/settings/timezone/kiosk` `{ ianaTimeZone }` → records the zone the KIOSK's own OS reports (FHQ-178). Ignored when an explicit zone is set; sent on every kiosk load, so a change to the kiosk's timezone propagates without polling. Server-side IP geolocation is never used for the zone — it resolves the hosting VPS, and this value is stamped onto new Google events via `GetSendZoneAsync`.
- `GET  /api/settings/display` → DisplaySettingDto (SurfaceMultiplier 0–1.0, OpaqueSurfaces, TransitionDurationSecs, ThemeSelection) — requires auth; returns defaults if no row exists for the user
- `PUT  /api/settings/display` `{ surfaceMultiplier, opaqueSurfaces, transitionDurationSecs, themeSelection }` → upserts the user's DisplaySetting row; requires auth
- `GET  /api/weather/current` → CurrentWeatherDto (condition, temperature, wind)
- `GET  /api/weather/hourly?date=yyyy-MM-dd` → List<HourlyForecastItemDto>
- `GET  /api/weather/forecast?days=5` → List<DailyForecastItemDto>
- `GET  /api/settings/weather` → WeatherSettingDto — requires auth; scoped to current user
- `PUT  /api/settings/weather` → upserts user's weather settings; requires auth
- `POST /api/weather/refresh` — triggers immediate weather data poll and SignalR broadcast

### Rate limiting (FHQ-101)
Four named fixed-window policies (`Configuration/RateLimitingConfiguration.cs`, applied via `[EnableRateLimiting]`; NO global limiter — kiosk polling and the SignalR hub must never be limited). Rejections return 429 + Retry-After + a ProblemDetails body, logged at Warning. All limits/windows configurable via the `RateLimiting` config section (env-var overridable per environment); defaults sized ≥5× over observed Deploy-Dev E2E peaks:
- `auth-per-ip` — `GET /api/auth/login` + `GET /api/auth/callback`, per client IP (shared bucket), 300/min
- `webhook-per-ip` — `POST /api/sync/webhook`, per client IP, 30/min
- `sync-trigger-per-user` — `POST /api/sync/trigger`, per JWT `sub` (IP fallback when unauthenticated), 10/min
- `weather-refresh-per-user` — `POST /api/weather/refresh`, per JWT `sub` (IP fallback), 15/min

`UseRateLimiter` sits after `UseAuthentication` (per-user partitioning needs the `sub` claim) and before `UseAuthorization` (limits apply regardless of auth outcome).

## SignalR (CalendarHub — /hubs/calendar)
- **EventsUpdated**: existing — triggers calendar refresh on all clients.
- **ThemeChanged()**: pushed by DayThemeSchedulerService when a period boundary passes. **Carries no payload** since FHQ-177 — the period is per-kiosk, so a single broadcast value would be wrong for any kiosk elsewhere. Each client responds by re-reading its own `GET /api/daytheme/today`.
- **WeatherUpdated**: pushed by WeatherPollerService when new weather data is stored. No parameters — UI fetches fresh data via HTTP.

## UI Layer Architecture
The DOM is structured in three stacked layers to support time-of-day theming and future weather overlays:

```
<body data-theme="morning|daytime|evening|night">
  <div id="theme-bg" />        ← layer 0: full-bleed gradient background (CSS @property transition, 45s)
  <div id="weather-overlay" /> ← layer 1: future weather animations (empty/hidden for now)
  <div id="app">...</div>      ← layer 2: all Blazor UI content (unchanged behaviour)
</body>
```

Theme switching is driven by the `data-theme` attribute on `<body>`. CSS custom properties registered via `@property` (typed as `<color>`) allow the browser to smoothly interpolate gradient colours over a user-configurable duration (default 15s, controlled by `--theme-transition-duration`). See `.agent/docs/ui-design-system.md` for full CSS variable reference.

The UI uses a **glassmorphism-lite** design — semi-transparent `.glass-surface` components with white border glow and layered box-shadows. Bootstrap has been removed; all styles live in `wwwroot/css/app.css`. The DM Sans font is self-hosted.

### Event time formatting

`CalendarEventViewModel.Start` / `.End` are `DateTimeOffset` values returned from the API in UTC. **All views must render times via `evt.StartLocal()` / `evt.EndLocal()`** (in `FamilyHQ.WebUi.ViewModels.CalendarEventViewModelExtensions`) — never call `.ToString(...)` directly on the `DateTimeOffset`, which formats the stored offset (UTC) and produces the wrong time for users outside UTC.

## Pages & Navigation
- `/` — Dashboard (Month / Day / Agenda views)
- `/settings` — Settings page — tabbed layout (General, Location, Weather, Display). Settings cog only shown when authenticated.
  - **General tab**: signed-in username, Sign Out button.
  - **Location tab**: current location with Auto/Saved badge, override input.
  - **Weather tab**: replaces the old `/settings/weather` sub-page.
  - **Display tab**: Surface Opacity (0–100%), Opaque surfaces toggle, Theme subsection (auto/manual selection, theme tiles, transition speed).
- Settings accessed via a gear icon (⚙️) in the DashboardHeader. User name and sign-out are on the Settings page, not the header.

## Feature Flags

Runtime feature flags are exposed to Blazor WASM via a `FeatureFlags` POCO, registered as a singleton in `Program.cs` and bound from `wwwroot/appsettings.json`. Flags are injected into the published bundle at container startup by `docker/webui/docker-entrypoint.sh`, which `sed`-substitutes values based on environment variables. Local `dotnet run` reads `wwwroot/appsettings.Development.json` instead.

### Weather Override (dev/staging only)

The Settings page has a fifth tab, **Weather Override**, rendered only when `FeatureFlags.WeatherOverrideEnabled` is true. The flag is sourced from the WebUi's `appsettings.json` key `FeatureWeatherOverride`, which is injected into the published bundle at container startup by `docker/webui/docker-entrypoint.sh` based on the `FEATURE_WEATHER_OVERRIDE_ENABLED` environment variable. Dev and staging set this to `true`; preprod and production set it to `false`. Local `dotnet run` inherits `true` from `wwwroot/appsettings.Development.json`.

When the tab's "Override active" pill is on, a developer can tap any `WeatherCondition` and optionally toggle the Windy modifier to immediately force the full-screen weather animation (`WeatherOverlay`) to that condition. The override is purely client-side transient state held in a scoped `IWeatherOverrideService` and is never persisted — refreshing the browser reverts to the real weather pipeline. The `WeatherStrip`, backend API, user `WeatherSetting`, and real weather data flow are untouched.

## Versioning

Application version is a SemVer string (`MAJOR.MINOR.PATCH`) derived at build time by [MinVer](https://github.com/adamralph/minver). MAJOR/MINOR are pinned in `Directory.Build.props` via `<MinVerMinimumMajorMinor>`; PATCH auto-increments based on git tags pushed by Jenkins on master builds. See `.agent/docs/ci-cd.md` for the full pipeline mechanics and `.agent/skills/git-workflow/SKILL.md` for when to bump MAJOR/MINOR.

Surfacing:
- **`/api/health`** returns the WebApi version in a `version` field with `Cache-Control: no-store`. The endpoint is anonymous, so it publishes the SemVer core (plus any pre-release label) with build metadata stripped — no commit SHA (FHQ-103). Deployed images build without a `.git` directory and so emit no metadata today; the strip keeps that true regardless. **The pre-release label must stay**: `VersionService.VersionsMatch` strips metadata only, so a server value stripped any further could never match the client and would re-trigger the reload below on every reconnect.
- **WebUi footer** (`Components/Footer.razor`) renders `v{ClientVersion}` in the bottom-right corner. The version is read from `AssemblyInformationalVersionAttribute` on the WebUi assembly.

Auto-reload of active clients on a new prod deploy:
- `IVersionService` / `VersionService` (singleton, registered in `Program.cs`) caches the WASM build's `ClientVersion` and the latest `ServerVersion` from `/api/health`.
- On startup, `InitializeAsync()` fetches `/api/health` once.
- `SignalRService` exposes a `Reconnected` event (via `ISignalRConnectionEvents`); `VersionService` subscribes and calls `CheckAsync()` on every reconnect. A WebApi deploy restarts the server, dropping the `CalendarHub` connection — when the auto-reconnect succeeds, `CheckAsync` runs and compares versions.
- On a SemVer-core mismatch (build metadata stripped), `UpdateAvailable` fires (showing `<UpdateBanner />` with "New version available — reloading…"), then `IJSRuntime.InvokeVoidAsync("location.reload")` runs after a 5s delay (via `TimeProvider`, so testable with `FakeTimeProvider`).
- A `_updateTriggered` flag enforces fire-once semantics so transient SignalR blips never trigger multiple banners or reload cycles. Note it is instance state on the WASM singleton, so `location.reload()` resets it — it bounds re-firing within one page life, not across reloads. A *permanent* version mismatch therefore loops, which is why the health endpoint and the client must strip exactly the same amount (above).

## Performance Targets
- Responsiveness: API endpoints should target < 200ms response time.
- EF Core Efficiency:
-- Use AsNoTracking() for read-only queries.
-- Avoid N+1 issues by using .Include() for required navigation properties.
-- Always implement pagination for list-based endpoints using Skip and Take.
-- Async Execution: Always pass CancellationToken from the Controller through to EF Core async methods (e.g., ToListAsync(ct)).
-- Transactions: Use explicit transactions (IDbContextTransaction) for operations involving multiple SaveChangesAsync calls to ensure atomicity.
- Blazor Optimization: Use @key in loops to help the diffing engine and avoid unnecessary re-renders of heavy components.
