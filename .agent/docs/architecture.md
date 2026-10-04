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
- **CalendarEvent.Reminders / CalendarInfo.DefaultReminders** (FHQ-189): Google's `reminders` object,
  stored as `jsonb`, in Google's own **key casing** as well as its own shape —
  `{ useDefault, overrides: [{ method, minutes }] }`. Mapped with a **value converter** plus a
  structural `ValueComparer` (`EventRemindersConversion`), *not* `OwnsOne(...).ToJson()`:
  `EventReminders` is a **value**, and modelling it as an owned entity graph gave its `Overrides`
  collection a shadow key (`__synthesizedOrdinal`) that cannot survive a detached `Update()` — which
  is how `CalendarRepository` saves a calendar, and which stopped all production syncing for five
  hours (FHQ-205). The stored JSON is byte-identical either way, so no data changed.
  `method` is the **string Google sent** (not an enum) and `minutes` is **signed**: the read path never
  validates, clamps, de-duplicates or reorders, because Google is the authority on its own values.
  Four states: `null` = not yet synced (drives the backfill); `useDefault:true` = inherits the
  calendar's defaults (**timed events only** — an all-day event never inherits, Google materialises
  the default into explicit overrides); explicit overrides; and `useDefault:false` with an empty list
  = explicitly none. **On an all-day event that last state is ambiguous** — Google reports it for
  reminders that fire *after* the event starts, which its API will not show at all (FHQ-193), so
  nothing may render it as "no reminders". Order is not meaningful: Google reorders the array, so
  compare with `EventReminders.SameAs`.
- **Writing reminders back is opt-in, and that is the whole safety property.** `CreateEventRequest`
  and `UpdateEventRequest` carry an optional `Reminders`: **absent** means the user did not touch
  reminders, **present** means "replace the event's reminders with exactly this set". The value is
  threaded to `IGoogleCalendarClient`'s write methods as an optional parameter defaulting to null, and
  `MapToGoogleEvent` emits the `reminders` key only when it is non-null — the same shape `recurrence`
  uses, dropped by `WhenWritingNull`. One body is shared by create, create-recurring and patch, so an
  always-populated member would make every title or time edit rewrite the account's reminders.
  `GoogleCalendarClientMappingTests` holds both halves: absent on an ordinary patch, present and
  complete on a reminder write. `overrides` is always emitted alongside `useDefault`, even empty —
  `{"useDefault":true}` on its own is rejected `400 cannotUseDefaultRemindersAndSpecifyOverride`, and
  PATCH replaces the whole array, so an omitted array would read as "no change" rather than "clear".
  Validation (≤ 5 overrides, 0–40320 minutes, `popup`/`email`) lives in `EventRemindersValidator` in
  `FamilyHQ.Core` and runs **only when the request carries reminders**; values read from Google never
  pass through it.
- **The Reminders tab's unadded Add value is committed on save, and only when the form was touched.**
  `ReminderPickerModel.CommitPendingFormReminder`, called once by `EventModal.SaveEvent` before
  anything reads `HasChanged`. The form describes a reminder from the instant it appears, so a family
  member who configured one and saved without pressing Add used to get an event with no reminders and
  no indication of it. The commit goes through `TryAdd`, so `HasChanged`/`ToEventReminders` remain the
  only gate on the wire. It fires only when **all** of: the form's own controls have moved
  (`IsFormTouched`, sticky — moved and moved back still counts); the event is not inheriting; the list
  is empty, which is what would make the save silent; and the form's value is not one `Remove` took
  away in this visit. Untouched, switching inheritance off and saving still yields explicitly-none,
  which is how a family asks for silence — that is why an unconditional commit was rejected.
- **After a reminder write, what Google returned is what is stored.** Google accepts almost any value
  with a `200` and then rewrites it: a negative `minutes` clamps to `0`, above 40320 clamps down,
  duplicates collapse, the array comes back reordered, and an unrecognised `method` is dropped
  entirely. The client maps the response through the same `MapReminders` the read path uses; a response
  that mentions no reminders leaves the stored value alone, because saying nothing is not saying none.
  **An all-in-series master patch has to apply that answer itself** —
  `CalendarEventService.ApplySeriesRemindersAsync`, from both branches of `PatchSeriesMasterAsync`.
  The master is a transient event built for the write, so Google's answer lands nowhere unless it is
  copied onto `seriesRows`. `ReconcileWindowAsync` afterwards covers rows **inside the stored sync
  window** only, and that window is the last *full* sync's — incremental syncs neither move it nor
  confine the rows they add to it — so a long-lived calendar holds rows beyond it, including possibly
  the occurrence just edited. It runs **before** the reconcile deliberately: the reconcile's
  per-instance answer then still wins, which matters because an exception instance can carry
  reminders the master's do not describe.
- **A "this and following" split carries the original series' reminders onto the forward series**, for
  the same reason it carries the anchor zone: the forward half is a continuation, not a fresh choice.
  Without it Google applies the calendar's defaults and a phone-set reminder disappears from the tail.
  A request that changes reminders itself wins; a series with none learned yet still sends nothing.
  `CalendarInfo.DefaultReminders` is kept current for calendars FamilyHQ already knows about too —
  `CalendarSyncService.RefreshCalendarDefaultsAsync` adopts it (alongside `IanaTimeZone`) from the
  `calendarList` response every sync already fetches, the same way `AddCalendarAsync` seeds it for a
  brand-new calendar — because in production every calendar already exists.
- **What `RefreshCalendarDefaultsAsync` adopts** (FHQ-211): `DisplayName`, `Color`, `IanaTimeZone` and
  `DefaultReminders` — the only path that refreshes an EXISTING calendar from Google, on idempotent
  terms (write only when Google's value differs; an absent or blank value never blanks a stored one).
  `DisplayName` and `Color` were missing until FHQ-211, so a calendar renamed or recoloured in the
  Google Calendar app kept its original values in FamilyHQ forever. That was not cosmetic:
  `MemberTagParser` resolves members by matching calendar **display names** in an event's
  description, so a stale name broke event-to-member assignment in both directions — a description
  naming the new name stopped resolving and one naming the old name still did. Google is the system
  of record and there is no local rename to protect (`CalendarSettingsRequest` carries only
  `IsVisible`/`IsShared`), so its value is unambiguously authoritative. Self-healing: the next sync
  adopts the difference, no backfill needed. **Change count**: a name or colour change is counted as
  material (the dashboard renders both, and `CalendarSyncWorker` broadcasts `EventsUpdated` — whose
  kiosk handler refetches the calendar list — only when `SyncResult.HadChanges`); a zone or reminder
  change stays bookkeeping, because neither is rendered. Note that events whose **Google
  description** still carries the old name inside a `[members: …]` tag lose that membership when
  Google next re-sends them, since sync re-derives `Members` from the description each time — the
  intended consequence of the name no longer existing, not a separate defect.
- **SyncState.RemindersSyncedAt** (FHQ-189): when this calendar was first synced with `reminders` in
  the field mask. Null forces exactly one full sync, because incremental sync never re-sends an
  unchanged event and the events already in production would otherwise never gain reminders.
- **DayTheme**: Stores the 4 time-of-day period boundaries (MorningStart, DaytimeStart, EveningStart, NightStart as TimeOnly) for a given Date, **per kiosk** — unique on (UserId, Date) since FHQ-177. Calculated once per day per kiosk by DayThemeSchedulerService from sunrise/sunset at that kiosk's **saved LocationSetting**. A kiosk with no saved location gets no row and keeps its default theme: the boundaries used to come from a server-side IP lookup, which geolocates the hosting VPS rather than the family, so guessing is choosing a known-wrong answer.
- **LocationSetting**: Stores the user's configured location (PlaceName, Latitude, Longitude). One row per UserId; when absent, the API falls back to IP-based geolocation.
- **DisplaySetting**: Stores user display preferences (SurfaceMultiplier as `double` 0–1.0, OpaqueSurfaces as `bool`, TransitionDurationSecs as `int`, ThemeSelection as `string`). One row per UserId. ThemeSelection is `"auto"` (time-of-day transitions) or a period name (`"morning"`, `"daytime"`, `"evening"`, `"night"`).
- **WeatherDataPoint**: Stores weather data (current, hourly, daily) for a location. Keyed by LocationSettingId + DataType + Timestamp. `Condition` is persisted as the `WeatherCondition` **ordinal**, so new enum members must be appended — inserting one re-labels every stored row (pinned by `WeatherConditionTests`).
- **WeatherSetting**: Stores weather preferences (Enabled, PollIntervalMinutes, TemperatureUnit, WindThresholdKmh). One row per UserId.
- **WebhookRegistration**: Tracks Google Calendar push notification watch channel registrations. One row per CalendarInfo. Stores ChannelId (UUID sent to Google), ResourceId (returned by Google), ExpiresAt, RegisteredAt, and `RegisteredAddressHash` — SHA-256 hex of the full address the channel was registered with (FHQ-196). The hash, not the address: a RelayRobin address embeds its route key, which is the credential authorising a notification POST. Nullable, because rows written before FHQ-196 have no value; a null reads as a mismatch, so those channels re-register once and fill it in.

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
- **IWebhookRegistrationService / WebhookRegistrationService**: Registers Google Calendar push notification watch channels per-calendar. Called after login and periodically by WebhookRenewalService. Config-gated via `Sync:WebhookRegistrationEnabled`. A channel with more than 24h left is skipped **only** when `WebhookRegistration.RegisteredAddressHash` equals the hash of the address configured now (FHQ-196) — so a changed `Sync:WebhookBaseUrl` takes effect on the next renewal pass (within the hour) instead of being ignored for up to ~6 days, via the normal new-channel-then-stop-old sequence. Any log line carrying the address masks the path segment after `/h/` (`WebhookAddress.Mask`). When registration is enabled, `Sync:WebhookBaseUrl` must be an absolute http(s) URL or `AddFamilyHqServices` refuses to boot (`SyncOptions.Validate`); http is deliberately allowed, because dev and staging register against the Simulator at `http://webapi:8080`.
- **WebhookRenewalService** (IHostedService, lives in WebApi): Background service that polls for watch channels due for renewal, starting 1 minute after boot and then every `Sync:WebhookRenewalPollInterval` (default 1 hour). Iterates all users via ITokenStore; each pass just calls `RegisterAllAsync`, which re-registers only the channels inside their final `WebhookRegistrationService.RenewalWindow` (24h) and leaves the rest alone, so a pass with nothing due is one query per calendar and no Google call. Disabled when `Sync:WebhookRegistrationEnabled` is false.
  - **FHQ-213**: the cadence is a *poll*, not a renewal period, and it must stay shorter than `RenewalWindow` — `SyncOptions.Validate` refuses to boot otherwise. It used to be `Sync:WebhookRenewalInterval` (6 days) measured from process start, while Google's ~7-day channel clock runs from registration: a restart reset the 6-day timer and the next pass could land days after the channels had expired, with the startup pass correctly skipping because more than 24h remained. Nothing errored — push simply stopped and sync fell back to the hourly periodic pass. `Sync:WebhookRenewalInterval` is still bound so a stale env file boots, but the value is ignored and one Warning at startup asks for it to be removed. A channel found **already expired** is logged at Warning; that state was previously indistinguishable from a quiet calendar.
  - **Telemetry shape (FHQ-213).** Exactly **one Information line per pass**: `Webhook renewal pass complete for N user(s): C calendar(s) checked, R channel(s) re-registered, E found already expired. Next pass in …`. `RegisterAllAsync`/`RenewAllAsync` return a `WebhookRegistrationTally` (`CalendarsChecked`, `ChannelsRegistered`, `ChannelsFoundExpired`) purely so that line can be honest — the counts are only known per-calendar, and only the renewal loop knows it is a scheduled pass. The per-calendar "still valid, skipping" line is Debug. **Do not** demote the summary to Debug to quieten it: no FamilyHQ environment emits Debug (issue 14 in `intermittent-issues.md`), so Debug deletes a signal rather than lowering it, and a renewal loop that silently stopped would then look identical to one working perfectly.
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
- **AllInSeries with an unresolved anchor is refused** (`SeriesOriginUnresolvedException`), because the new origin is a function of the unknown old one. It only gets that far when the request actually changes the start, the duration or the all-day flag: an edit that changes no timing sends no start, so it never resolves an anchor and never reaches the refusal. See the section below.
- **What actually fixes the reported defect is the bullet above about the RRULE-less master.** Say so plainly: once `GetSeriesMasterAsync` stops discarding a usable DTSTART, the only remaining route to an unresolved anchor is a master `events.get` that 404s or yields no parsable start, and what that now produces is a refusal rather than a relocation. There is no "degraded write" in this path any more — the write that omits `start`/`end` is the *ordinary* path for every timing-unchanged edit, for reasons that have nothing to do with a missing anchor.
- **The COUNT split with an unresolved anchor is refused outright**, and `SplitSeriesAsync` now resolves *before* it truncates, so the refusal leaves Google untouched. (The remaining window — truncate succeeds, forward-series create fails — is FHQ-173, closed by the section below.) A Never/UNTIL split needs no count and never resolves an anchor, so it is unaffected. The reorder also means the zone ladder's backfill commits before any Google write; that is benign — it caches a zone Google supplied, and records nothing about a write having happened.
- **The content hash for the omitted-times patch excludes the times *and* the all-day flag** (`ComputeHashWithoutTimes`). The hash is an opaque token round-tripped through `extendedProperties` for the echo guard, so no local start would have broken it — but the token must describe what was sent. All-day-ness reaches Google only through `start.date` vs `start.dateTime`, so a body with neither key sends no flag either; excluding it is safe because a flip is classified as a timing change and never reaches this write.
- **One Warning per incident.** The anchor site logs at `Debug` — it reports a fact and decides nothing. Both of its callers refuse on it, and each logs exactly one `Warning` naming what it refused. `DomainExceptionHandler` logs a second when it maps the exception, but that line names only the status, method and path, so the service line is the only record of the cause.

## Google write paths — an edit that changes no timing says nothing about timing

**Rule: an all-in-series master patch sends `start` and `end` only when the request actually changes the start, the duration or the all-day flag. A rename, a location change, a description change or a reminder change sends neither key.**

`CalendarEventService.DescribeTimingChange` is the decision, and `PatchSeriesMasterFieldsOnlyAsync` is the write. Both live next to `PatchSeriesMasterAsync`.

- **Why sending them was wrong even when arithmetically a no-op.** Google holds DTSTART as an *instant*. What leaves this process is an offset-less `dateTime` plus a `timeZone`, which Google **re-resolves** against that zone. The two are not interchangeable: a wall clock inside a DST gap names no instant, and one inside the repeated hour names two. So re-rendering Google's own anchor back to it is a **lossy round trip**, and for a series anchored in the repeated hour the resolution can return the *other* instant. DTSTART moves, every occurrence moves with it, and the family sees it in the Google Calendar app weeks later with nothing connecting it to the edit. This is the prime directive's "round-trip, don't substitute" and "does this request alter anything the user did not ask us to alter" in one place.
- **No amount of care in deriving the wall clock helps.** The ambiguity is in Google's reading of the pair, not in FamilyHQ's writing of it. Omitting both keys is the whole fix: `events.patch` merges, so an absent key leaves the resource's value untouched — which is the only way to express "this write says nothing about when the series happens". A `"start": null` would not do; Google treats a present key as an instruction.
- **The timing-unchanged path never reads the master.** The master's origin is an input to exactly one thing — a start derived from it — so an edit that sends no start has no use for it. Fetching it anyway would put a Google call and its transient failures in front of every rename for a value that is then discarded, and it would be the shape of the defect: reading Google's anchor in order to hand it back. It follows that a rename succeeds on a series whose master cannot be read at all.
- **No zone is sent either**, because the zone is only expressible as `start.timeZone`. That is strictly stronger than getting the zone right (see the FHQ-170 section): there is nothing to re-anchor. The construction still states the series' own stored zone rather than `null`, so the day this path is ever given a start to send it sends it anchored correctly instead of falling through to the family's configured zone.
- **Reminders are unaffected** — they are a field of their own — so a reminder-only edit still lands in full through this path. It also applies Google's answer to the series' local rows; see the reminders bullet above.
- **Existing production data.** Nothing to migrate and nothing to backfill: this changes only which keys a write sends, so every series already on the account is protected from the next timing-unchanged edit onwards. It is **not** retrospective — a series whose DTSTART an earlier rename already moved stays moved, because Google is the system of record and the instant it held beforehand is not recoverable from here. Such a series has to be corrected in the Google Calendar app, or by an all-in-series time change setting the intended time deliberately.
- **Where it is proved.** `tests/FamilyHQ.Services.Tests/Calendar/SeriesRenameAnchorPreservationTests.cs` composes the real `CalendarEventService` and the real `GoogleCalendarClient` over a mocked `HttpMessageHandler` that models Google's re-resolution, and asserts the master's anchor **instant** is unmoved by a rename. It runs two zones whose clocks go back on different dates to different offsets, computed from NodaTime's bundled tz database against a named zone, so no host offset can satisfy both — a one-zone version would pass on a British machine in BST and fail in CI. The Simulator does not model this, so there is no honest E2E twin.

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

## Google write paths — nothing between a successful write and its response

**Rule: once a write action has called `CalendarEventService`, the rest of the action does no I/O. Anything the response needs is loaded before the service call.**

`EventsController`'s four write actions (`CreateEvent`, `UpdateEvent`, `UpdateRecurringEvent`, `SetMembers`) load the calendars with `GetCalendarsAsync` before they write, then resolve the event's owning calendar from that list in memory via the `MapToDto(CalendarEvent, IReadOnlyList<CalendarInfo>)` overload.

- **Why the order is the point.** The write reaches Google first and the local database alongside it. A read that fails *after* that returns 500 for work which actually succeeded; the person at the kiosk is told the save failed and presses save again, and a retried create leaves a second event on the family's calendar — visible in the Google Calendar app long after anyone remembers why. The failure does not have to be likely to matter; the consequence is a duplicate in the system of record.
- **It costs nothing.** One repository round trip per request either way — the lookup moves rather than multiplying — and it is what `CalendarEventService` already does internally: it loads all the calendars before writing and finds the owner among them.
- **`GetEvent` deliberately differs** and keeps its single-row `GetCalendarByIdAsync` after the event load. It is a read, so a failing lookup costs only the error, and fetching every calendar to answer for one event is the more expensive way round. The inconsistency is intentional, and commented at both sites, so that a later tidy-up does not unify them and put the I/O back.
- **Both routes still resolve the owner**, so `OwningCalendarId` and `OwningCalendarDefaultReminders` mean the same thing on every response that carries a `CalendarEventDto`. Tolerating a null owner on the write paths would have been the wrong fix: a nullable field cannot tell a caller "not populated here" from "no owner".
- **Where it is proved.** `tests/FamilyHQ.WebApi.Tests/Controllers/EventsControllerWriteOrderingTests.cs` records the order of the calendar load and the write for each of the four actions, with the repository mock strict so that *any* repository call after the write fails the test rather than only the one method that used to be called there.

## Google write paths — the post-write reconcile removes what Google no longer has

**Rule: `CalendarEventService.ReconcileWindowAsync` deletes a stored row when, and only when, the row's `Start` falls inside the window it has just fetched in full and the fetch did not name the row's `GoogleEventId`.**

The upsert loop only adds and updates, and the tombstone branch needs Google to name an id, so before this nothing removed a row Google had simply stopped returning. That is safe while instance ids are stable and stops being safe when a series' slot identity changes: an exception's id is `{masterId}_{originalStartStamp}`, so an all-in-series timing change renames every instance and strands every row carrying an old id. The family then sees two tiles on one slot, and cannot delete the stale one from the kiosk — Google no longer holds the event that row names — until a full sync tombstones it.

- **Why absence is evidence here.** The reconcile fetches `[SyncState.SyncWindowStart, SyncWindowEnd]` with **no sync token**, so the answer is a statement of the whole window rather than a delta. This is the only place in the write paths where that holds.
- **Why only rows starting inside the window.** Google's `timeMin`/`timeMax` select on overlap — `end > timeMin` and `start < timeMax`. A row whose `Start` is at or after the window start and whose `End` is after it satisfies both, so Google would have had to list it. A row starting *before* the window qualifies only on the strength of its stored `End`, which is the one value a stale row may have wrong, so those rows are left alone even though some are orphans too. The upper bound is the window end **truncated to the second**, because that is what the client puts on the wire (`yyyy-MM-ddTHH:mm:ssZ`) while the stored window end carries the fraction of a second the full sync wrote it at.
- **Two answers are refused rather than acted on.** An empty fetch (`GetEventsAsync` returns an empty list without failing when a page body does not deserialise) and a fetch at or above `GoogleCalendarClient.MaxWindowFetchEvents` — `MaxSyncPages × EventsPageSize`, the point past which the client stops paging and returns what it has. The count test is an upper bound, not proof of completeness, so it only ever refuses a prune. `CalendarSyncService`'s own full-sync tombstone diff shares that page-cap exposure and has no such guard.
- **The fetch itself now states whether it is complete**, so the two refusals above are local heuristics standing in for a fact the client reports. `GoogleEventFetch.IsComplete` is false when `GetEventsAsync` hit the page cap with a page token still outstanding, or skipped a page whose body did not deserialise; `GoogleCalendarFetch.IsComplete` says the same for the calendar list, which follows `nextPageToken` up to `MaxCalendarListPages` and reports a body carrying no readable `items` as incomplete rather than as an account holding no calendars. False means absence from the result is not evidence of deletion, and only a caller that turns absence into a delete needs to read it.
- **A series master row is excluded explicitly.** `singleEvents=true` expands series into instances and never returns the master resource, so a row whose `GoogleEventId` is a bare series id is missing for that reason alone. Such a row exists during the recurrence-on reconcile: the event is promoted in place, so the pre-promotion single row's id *is* the new master id. `ToggleRecurrenceOnAsync` removes it itself afterwards, having established that the expansion replaced it.
- **Candidates come from `GetEventsByOwnerCalendarAsync`**, the same calendar-and-range-scoped query the full sync's tombstone diff uses, so both places that act on Google's silence start from the same set. Its predicate is overlap, which is wider than the prune may act on, so the narrowing is applied in the service — it is the condition that authorises a delete, and stating it there is what lets a unit test exercise its boundaries.
- **The candidate rows are read before the fetch, in both places.** Only a row that was already stored when the fetch was answered can be judged by that fetch; a row stored afterwards — a kiosk write, or an event created on a phone that a concurrent sync has just ingested — is missing from the answer for that reason alone. Deleting one loses a real event that no incremental sync restores, because an unchanged event is never re-sent, so only a later full sync would bring it back. The two failure modes are not symmetrical: reading early can only miss an orphan, which the next full sync removes anyway. `CalendarSyncService.SyncCoreAsync` therefore hoists its candidate read above the Google fetch too, and keeps it conditional on the sync being a full one so the incremental path pays for no extra query. Proved by `tests/FamilyHQ.Services.Tests/Calendar/CalendarSyncServiceFullSyncDiffTests.cs`, which models the concurrent insert as a callback on the fetch stub.
- **The obsolete-*calendar* prune reads early for the same reason, and does not share its snapshot.** `CalendarSyncService.SyncAllAsync` removes a local calendar the Google calendar-list fetch did not name, and `CalendarRepository.RemoveCalendarAsync` takes every event that calendar owns and its `SyncState` with it, so the same late-read hazard costs a whole column of the kiosk rather than one tile. The concurrent writer here is another whole-account sync: pass 1 of that method is the only caller of `AddCalendarAsync`, and two passes can run at once because `SyncController.TriggerSync` runs `SyncAllAsync` inline on the HTTP request while `CalendarSyncWorker` runs it from the job queue, with nothing serialising the two. The prune therefore takes a **dedicated** pre-fetch read (`calendarsBeforeFetch`) and pass 1 keeps its own post-fetch read: pass 1 decides whether to insert, and an insert for a calendar a concurrent sync has already added violates `CalendarInfo`'s unique index on `(GoogleCalendarId, UserId)` and fails the whole account's sync — so the two reads must not be collapsed into one, in either direction. Proved by `tests/FamilyHQ.Services.Tests/Calendar/CalendarSyncServiceCalendarPruneTests.cs`, whose third case is the regression guard for the collapse.
- **The removal is committed through `CommitRowRemovalAsync`.** The rows this prune deletes are exactly the ones a concurrent sync of the same window is tombstoning, so the delete race is the likely case here, not the remote one. The condemned rows are re-read through the tracking path first, because the candidate query is `AsNoTracking` and detaching an untracked copy would leave the real `Deleted` entry in the change tracker.
- **Where it is proved.** `tests/FamilyHQ.Services.Tests/Calendar/CalendarEventServiceReconcilePruneTests.cs`. Each negative case puts a row the fetch did not mention somewhere the fetch proves nothing about and asserts it survives a reconcile that prunes a real orphan in the same breath, so none of them can pass against a service that prunes nothing.

## Which calendar an event lands on — one rule, two readers

**Rule: the selection-to-owning-calendar decision is stated once, in `OwningCalendarRule.OwningCalendarFor` (`src/FamilyHQ.Core/Calendar/`): one chosen member means that member's own calendar, several mean the household's shared one.**

Two places need the answer. `CalendarEventService.CreateAsync` applies it for real. The event modal has to *predict* it for an event that is not saved yet, because default reminders belong to a calendar — the Reminders tab cannot say what inheriting the calendar's reminders will do, or pre-fill those values when the family stops inheriting, until it knows the destination. An existing event needs no prediction: the server reports its owner and `EventModalLogic.OwningCalendarDefaults` reads it. A new one has no owner while its chips are still being chosen.

- **Why it sits in `FamilyHQ.Core`.** Both `FamilyHQ.WebUi` and `FamilyHQ.Services` reference it, and neither references the other. The rule takes `OwningCalendarCandidate` (an id and an `IsShared` flag) rather than `CalendarInfo` or `CalendarSummaryViewModel`, so neither side's types leak into Core and no future caller can route on a field the other side cannot see.
- **The server still holds its own copy of the branch, deliberately.** Having `CreateAsync` call the rule would be the cleaner end state, but it is the path that decides which Google calendar an event is written to, and a mistake there is visible to the family in the Google Calendar app. The rule was extracted and the server pinned to it by assertion instead; moving the call site is a separate, deliberate change.
- **Where it is proved.** `tests/FamilyHQ.Services.Tests/Calendar/CalendarEventServiceOwningCalendarAgreementTests.cs` drives the real `CreateAsync` for each selection shape the server accepts and asserts the resulting `OwnerCalendarInfoId` — and the Google calendar actually written to — against what the Core rule returns for the same inputs. Every expectation is derived from the rule, never restated, so a change to either side alone turns it red.
- **The tie-break between two shared calendars is pinned although it is unreachable.** `CalendarsController` clears the previous shared calendar whenever one is designated, so there is only ever one. The rule takes the lowest `Guid` anyway: the dashboard holds its calendars in display order and the repository's query applies no ordering at all, so "the first shared one I was handed" would make the two sides name different calendars the moment that enforcement slipped. An intrinsic total order is the only tie-break both sides can honour.
- **Null is "no answer available", never a second opinion.** The rule returns null for an empty selection, for a single selection naming a calendar it was not given, and for a multi-member selection with no shared calendar. The server refuses all three — `CreateEventRequestValidator` rejects a memberless create, and `CreateAsync` throws `UnknownCalendarException` / `InvalidOperationException` for the other two — so there is no server answer for the client to disagree with.

## Webhook echo guard

FamilyHQ writes to Google Calendar via `CalendarEventService` and `CalendarMigrationService`. Each write computes a SHA256 over `(title, start, end, isAllDay, description)` via `EventContentHash` and stores the hex hash as `extendedProperties.private["content-hash"]` on the Google event. Google's resulting push notification then arrives at `SyncController.GooglePushWebhook`, which enqueues a durable sync job; `CalendarSyncWorker` later dispatches it to `CalendarSyncService.SyncAsync` / `SyncAllAsync` (see "Durable calendar sync queue" below).

The guard is implemented in two halves:

1. **Outbound** — every successful Google write records `(GoogleEventId, hash)` in a singleton `IOutboundWriteHashCache` with a 60-second TTL. Failed writes do not record.
2. **Inbound** — `CalendarSyncService.SyncCoreAsync` reads the content-hash from each inbound `CalendarEvent.ContentHash` (carried through from `GoogleApiEvent.ExtendedProperties.Private.ContentHash` via the `events.list` `fields=` allowlist) and consults the cache via `IsSelfEcho`. On a hash match the event is **usually** skipped — no DB write, no further Google write, single "Self-echo skipped" Information-level log entry — but the hash covers `(title, start, end, isAllDay, description)` plus **the reminders a write actually sent** — never the ones an event merely holds, so an edit that did not touch reminders is stamped byte-identically to the way it was before reminders were modelled, and the events already in production keep matching. A phone's reminder-only change therefore still does not move the stamp, so a hash match is NOT automatically an echo: `IsSelfEcho` also compares reminders, and treats a locally-unlearned reminder set (`existing.Reminders is null`) paired with an inbound value as new information rather than an echo, so that event is processed (a DB write) even though its hash matched.

#### The stamp proves authorship, not freshness

A matching `(id, hash)` pair says FamilyHQ wrote that event in the last 60 seconds. It does **not** say the content is still the content FamilyHQ wrote, because the hash lives in `extendedProperties.private` and Google leaves that alone when the event changes underneath it. Two ways that happens, both real:

- patching a series master rewrites the `summary` of every exception of that series, while their extended properties keep the stamp the single-occurrence write left there (see `simulator-external-dependencies.md`);
- an edit made in the Google Calendar app changes whatever the user changed and touches no extended property at all.

So `IsSelfEcho` treats the stamp as **nominating a candidate** and resolves it against the locally-stored row: an echo carries what we wrote, and what we wrote is what we stored, so an inbound event whose `(title, start, end, isAllDay, description, location)` differ from the row is a real change and is processed. Location is in that set although it is not hashed — the stamp says nothing about it, but the row does. Without this, the change is discarded silently and **permanently** — incremental sync never re-sends an unchanged event, so the row keeps the stale value until a full sync happens to rebuild it.

Every write path that records a hash also persists the same content in the same operation — the kiosk create/update paths store the request they sent, and the recurring reconcile and the series migration store the very event they read the echoed hash off — so a true echo still compares equal and is still skipped.

**This is not a recompute of the hash from the inbound event.** `EventContentHash.Compute` describes what a write *sends*, not what an event *holds*; comparing a recomputed digest against the stamp would stop recognising FamilyHQ's own writes and bring back the webhook loop the guard exists to prevent.

**Read cost.** The stored row is fetched for every hash candidate, which is one indexed single-row read, paid only by an event whose id and stamp both match a write FamilyHQ itself made inside the TTL — never a whole sync page. An event that is not a candidate costs nothing extra: it reaches the update/create branch, which fetches the row anyway. The earlier optimisation that skipped the lookup on the echo path is gone, deliberately: it bought a read on a rare event at the price of dropping inbound changes.

`ResolveSeriesRecurrenceRulesAsync` (recurrence pass 2) still decides on the hash alone, passing no stored row. That is the cheap, quota-preserving choice for a pass whose only job is to avoid a master fetch; when the persistence loop then finds a candidate is not an echo, the instance keeps whatever RRULE its row already holds and a row with none picks one up next sync.

### Production verification

To verify the guard is active in any environment:

1. Make a single edit to a calendar event through the FamilyHQ UI.
2. Within ~5 seconds, the application log should contain:
   - At Debug: one or more `Recorded outbound write hash for event ...` entries.
   - At Information: at least one `Self-echo skipped for event ... (hash ...)` entry.
3. Zero "Self-echo skipped" entries across a day of writes suggests the guard isn't being hit — investigate.

### Why this matters for recurring events (FHQ-18)

A single PATCH on a series master can produce webhooks for every expanded instance Google touches. The guard ensures all such echoes are skipped cleanly, eliminating the latent loop risk that single-event writes avoid only through convergent upserts.

### The echo guard does not close the delete race (FHQ-214)

The hash guard suppresses an inbound *update* that echoes our own write. It cannot suppress an inbound **delete**: a cancelled event carries no content hash (`GoogleCalendarClient` maps it to a bare `CANCELLED_TOMBSTONE`), so a targeted sync draining Google's push for a delete FamilyHQ itself made will remove the same local row — and on preprod/production that push arrives within seconds, well inside the gap between the Google `DELETE` returning and FamilyHQ's own `SaveChangesAsync`. EF then finds its `DELETE` affected zero rows and raises `DbUpdateConcurrencyException`, which the kiosk saw as **HTTP 500 on a delete that had entirely succeeded**.

So **every local event-row removal that follows a Google write is committed through `CalendarEventService.CommitRowRemovalAsync`, not `SaveChangesAsync` directly.** The desired end state is "these rows do not exist", and another writer reaching it first is success: the helper confirms the rows are genuinely absent, logs the race at `Information`, and returns, so the controller still answers `204`.

- **Zero rows is unambiguous for an event.** `CalendarEvent` carries no optimistic-concurrency token — `ConcurrencyTokenModelGuard` requires one on `CalendarSyncJob` and `UserToken` only — so a `Deleted` event row affecting zero rows can only mean the row is gone, never "present but changed".
- **It re-reads rather than swallowing.** A failed `SaveChanges` rolls its whole batch back and a series delete removes N rows at once, so rows the other writer did *not* remove are still there and get re-deleted (bounded by `MaxRowRemovalAttempts`). A conflict raised by any other entity leaves all these rows in place and therefore rethrows.
- **The Google write is never repeated.** The helper touches local rows only.
- **The tracked delete is kept deliberately** rather than swapped for `ExecuteDelete` by predicate. `EventMembers` is the only FK pointing at `Events` and its constraint is `ON DELETE CASCADE`, so nothing is lost either way — but `ExecuteDelete` commits immediately, outside the pending change set, and `ICalendarRepository.DeleteEventAsync` is shared with `CalendarSyncService`, whose `changeCount += SaveChangesAsync(...)` return value decides whether the placement reconcile runs.

**Not exposed, and why** (audited with FHQ-214, restate if the ordering changes): the update/patch paths, because a patch never makes Google tombstone the event, so the push it generates makes the sync *update* the row rather than delete it; both `CalendarMigrationService` migrations, because each deletes the source event from Google **after** its local commit, so the tombstone push cannot arrive before it (FHQ-212 is proposing to reorder this — re-check then); `ReconcileWindowAsync`'s tombstone branch, because the reconcile fetches a window with neither a sync token nor `showDeleted=true`, so Google never returns a cancelled item to it; and `CalendarSyncService`'s own delete-then-save paths, which are the mirror image of the race but have no HTTP response to spoil — the per-event loop already catches and continues, and the full-sync tombstone batch aborts before stamping the sync token, so the next sync redoes it.

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
- `POST /api/auth/issue-token` `{ userId }` → `{ token }` — **preprod smoke only**, see "Deployment tier" below. Mints a FamilyHQ JWT for a user whose Google refresh token is already stored; never calls Google.
- `POST /api/auth/issue-google-access-token` `{ userId }` → `{ accessToken, expiresAt }` — **preprod smoke only**. Refreshes that stored grant through the normal refresh path. `409 { error: "reauth_required" }` when Google reports the grant revoked — the one answer that is not a 404, because a smoke run must react to it ("sign in to preprod again") rather than retry.

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

### Clock Override (dev/staging only)

`FeatureClockOverride` is wired identically — `appsettings.json` key, flipped at container startup from `FEATURE_CLOCK_OVERRIDE_ENABLED` — and reaches `KioskTimeProvider.OverrideEnabled` via `FeatureFlags.ClockOverrideEnabled`. When it is on, `Index` attaches `window.familyHqKiosk` (`wwwroot/js/idle.js` `attachDevBridge`), which lets a caller shift the displayed date by whole days and force an immediate idle evaluation. That is what makes the day-rollover and kiosk-home-view E2E scenarios run in milliseconds instead of waiting out a real fifteen minutes; it is also why the bridge must never exist on a kiosk on a wall.

**The `environment:` block in each compose file is not the gate.** Every `docker-compose.*.yml` also passes `env_file: .env`, which forwards the whole file into the container — so a `FEATURE_CLOCK_OVERRIDE_ENABLED=true` line in any environment's env file takes effect whether or not that compose file lists the variable. Containment is: the shipped `appsettings.json` says `false`, the entrypoint flips it only on an exact `"true"`, and no production env file sets it. `tests/FamilyHQ.Core.Tests/ClockOverrideBridgeGuardTests.cs` guards the repository's half of that (ships off, read with no fallback, one guarded call site); the deployed env files are a deployment control and no test can reach them.

## Deployment tier & the preprod smoke endpoints (FHQ-139)

`Deployment__Tier` (`dev | staging | preprod | prod`) is the only thing that distinguishes preprod from prod at runtime. **Preprod deliberately runs `ASPNETCORE_ENVIRONMENT=Production`** so it loads prod settings and takes prod code paths, so `IsProduction()` cannot gate anything preprod-only; and the same image is promoted preprod → prod, so nothing can be compiled out. **A missing or unrecognised tier counts as not-allowed** — forgetting the key fails safe, and no environment fails to boot for lacking it.

The two smoke endpoints above are guarded by the named policy `PreprodSmokeAccess`, applied with `[Authorize(Policy = ...)]` on `PreprodSmokeTokenController` (class level, so a new action cannot be added unguarded). Three requirements, all of which must pass:

1. **Available** — `Auth__IssueTokenEndpoint__Enabled=true` **and** the tier is set and is not `prod` (`SmokeEndpointAvailableRequirement`).
2. **Shared secret** — `Authorization: Bearer <Auth__IssueTokenEndpoint__Secret>`, validated by its own `SmokeClient` authentication scheme with a constant-time compare over SHA-256 digests. Separate from the JWT scheme on purpose: the secret can never satisfy a user login (its principal carries no `sub` claim), and a valid user JWT can never satisfy the policy (the policy accepts only `SmokeClient`).
3. **Smoke account** — the body's `userId` must equal `Smoke__UserId`. Unset is a deny, never a wildcard. This is the requirement that prevents token theft: even with flag, tier and secret all wrong, only the test account can be minted for.

Every failure answers **404** with no `WWW-Authenticate` (the scheme's challenge/forbid handlers), so a probe cannot tell a shut endpoint from an unknown route — which is also why these two endpoints are deliberately **not** rate limited: a 429 would announce the route. Each decision is audit-logged; no secret, JWT or access token is ever logged.

Three layers keep this out of production:

- `Jenkinsfile.deploy-prod` → `Validate Production Config`, before the env file is shipped: fails if the flag is truthy or the tier is not `prod`, printing only which rule failed.
- Startup: `AddPreprodSmokeAccess` → `PreprodSmokeAccessGuard.Validate` refuses to boot on tier `prod` + enabled, or enabled without a secret (the `SyncOptions.Validate()` fail-fast precedent).
- The policy itself re-checks the tier, so prod refuses even with the flag on.

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
