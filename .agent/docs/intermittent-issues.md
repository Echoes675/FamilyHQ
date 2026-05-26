# Intermittent Issues Tracker

A living record of intermittent / flaky failures observed in CI or local runs, what we know about each, what mitigation (if any) is in place, and what would need to happen to actually fix them.

**Add a new entry whenever:**
- A test fails in CI for a reason that doesn't reproduce reliably.
- A mitigation is applied (retry, longer wait, polling) without addressing the root cause.
- A known intermittent issue recurs — update the occurrences list, do not create a duplicate entry.

**Mark an entry resolved (don't delete it):**
- Move it to the *Resolved* section at the bottom with the commit / PR that fixed the root cause and the date. This preserves the history so we can recognise the same symptom if it comes back under a different guise.

---

## Active issues

### 3. Calendar API 403 path does not always mark UserToken as NeedsReauth (RECURRED)

**Re-opened:** 2026-05-26 after Deploy-Staging #110 reproduced the symptom on the merged FHQ-30 code path. Tracked as **FHQ-31**.
**Component:** see fix list below (FHQ-27 + FHQ-28 territory). FHQ-30 work does not touch these paths.
**First seen (post-resolution recurrence):** Deploy-Staging #110 (2026-05-20, ~22:23 UTC).
**Empirical flake rate post-FHQ-28:** 3 PASS / 1 FAIL across Deploy-Staging #102, #103, #109, #110 → ~25%.

**Recurrence symptom (Deploy-Staging #110):**
The scenario *"Diagnostics page shows needs-reauth status with reconnect button"* failed with the diagnostic capture:
```text
sync-response-status=200
connection-status={"status":401,"body":""}
page.bodyTextHead="… Connection status Active …"
```
The connection-status endpoint returned **HTTP 401** (not the expected `"active"` / `"needs_reauth"` JSON body) — a new signature that the prior triage rubric did not anticipate. Per the rubric, `sync-response-status=200` indicates a silent success of the manual-sync POST: either Google never returned a reauth condition, or the WebApi swallowed the exception before it could be raised to the controller.

**Background context (pre-FHQ-28 resolution, retained for reference):**
The prior resolution rested on Deploy-Staging #102 + #103 being clean. **#110 is the first Staging failure since.** Deploy-Staging #109 on `dev` (post-FHQ-30 merge, commit `5842844`) PASSED — so the merged code clears the gate at least once, and the failure is timing-dependent, not a uniform regression.

**ROOT CAUSE CONFIRMED (2026-05-26):** Not a WebApi bug at all — a **test-side parallel-execution race**. `SyncResilienceHooks` `[AfterScenario]` teardown called `ClearAllSyncFailureModesAsync`, which wipes *every* user's simulator failure mode. With `xunit.runner.json` `maxParallelThreads=2`, the two `SyncResilience.feature` scenarios that share the `RefreshTokenInvalidGrant` setup run concurrently; Scenario A's teardown `ClearAll` fires mid-flight against Scenario B, erasing B's failure mode before B's manual sync triggers its token refresh. B's refresh then succeeds (no `invalid_grant`), the user is never marked NeedsReauth, the badge stays `Active`, and the assertion fails. The ~25% rate matches the race window on a 2-thread runner.

**Fix (on branch `fix/FHQ-31-reauth-flake-recurrence`):** `SyncResilienceHooks` now clears only the current scenario's user via the existing per-user `ClearSyncFailureModeAsync(userId)` backdoor. (Also on the branch as independent hardening: `[Authorize]` + UserId guard on `SyncController.TriggerSync`.) Pending verification: 10 consecutive Deploy-Staging passes.

Hypotheses A, B, C were all refuted — see `Tickets/FHQ-31/FHQ-31.md` for the full investigation log.

### 4. WebhookEchoGuard "still shows updated title" reads dashboard mid-re-render

**First seen:** Deploy-Staging #115 (2026-05-26). Tracked on branch `fix/FHQ-31-reauth-flake-recurrence` (fixed alongside FHQ-31 as same-family E2E robustness).
**Component:** E2E test step only — `tests-e2e/FamilyHQ.E2E.Steps/WebhookEchoGuardSteps.cs`. No production code involved.
**Scenario:** *"A FamilyHQ-side edit produces exactly one outbound write to Google"* (`WebhookEchoGuard.feature`).

**Symptom:** `GetVisibleEventsAsync()` returned an empty list; assertion `Expected {empty} to have an item matching …'Team Lunch with Alice'` failed.

**Root cause:** The step did `capsule.WaitForAsync(Visible)` (the FHQ-29 mitigation) followed by a *single* `GetVisibleEventsAsync()` read. The wait passed, but after the FamilyHQ edit the dashboard processes a SignalR `EventsUpdated` notification and re-fetches events; the single read landed in the re-render window and saw zero capsules — the TOCTOU race documented on `DashboardPage.GetVisibleEventsAsync`.

**Fix:** Replaced the wait+single-read with a poll loop (5 s deadline / 250 ms interval) that tolerates transient empties, matching the already-stable sibling step `ThenTheDashboardShowsTheUpdatedTitle`. Pending the same 10-Staging-pass verification.

---

## Resolved issues

### 3-original. Calendar API 403 path does not always mark UserToken as NeedsReauth (now re-opened above)

**Resolved (then recurred):** branch `fix/FHQ-28-staging-reauth-banner-investigation`, PR #74, merge commit `d9bb603` (2026-05-15). Tracked as FHQ-27 (initial attempt) → FHQ-28 (follow-up that actually closed the loop). The FHQ-27 retrospective was written prematurely after only 5 Deploy-Dev passes; the very next Deploy-Staging failed. **Do not write a "Resolved" entry off Deploy-Dev alone.** This resolution was retired on 2026-05-26 after Deploy-Staging #110 reproduced the symptom — see Active issue #3 above for the open investigation.
**Component:** `src/FamilyHQ.Services/Auth/DatabaseTokenStore.cs`, `src/FamilyHQ.Services/Calendar/CalendarSyncService.cs`, `src/FamilyHQ.Services/Calendar/GoogleCalendarClient.cs`, `src/FamilyHQ.WebUi/Pages/Index.razor`, `src/FamilyHQ.WebUi/Components/Settings/SettingsCalendarsTab.razor`, `src/FamilyHQ.WebUi/Services/SignalRService.cs`, plus the new `IConnectionStatusBroadcaster` abstraction in `FamilyHQ.Core.Interfaces` / `FamilyHQ.WebApi.Hubs`.
**First seen:** Deploy-Dev #328 (2026-05-14).
**Occurrences:** Deploy-Dev #328, #330, #331, #332, #340, #350, #353; Deploy-Staging #101. Before FHQ-27 the three pulled scenarios flaked at observed rates ~50% (403 path), ~25% (diagnostics needs-reauth), ~10% (refresh-token banner). After FHQ-27 the symptom briefly disappeared across 5 Deploy-Dev runs (#344–#348) but reappeared on Deploy-Staging #101 and Deploy-Dev #350/#353.

**Symptom:**
After a sync attempt that hit a Google reauth-triggering condition, one of the four SyncResilience scenarios in `SyncResilience.feature` would fail intermittently — either the dashboard reauth banner never appeared within 30 s, or the diagnostics-page status badge rendered `Active` instead of `Needs Reauth`. The specific failing scenario varied between runs. The Deploy-Dev #353 occurrence carried hard evidence (the diagnostics page directly read backend connection status and got `Active`), proving at least one occurrence was server-side, not UI-side.

**Root cause (cumulative across FHQ-25 / FHQ-27 / FHQ-28):**
Three contributing mechanisms, none individually load-bearing, all closed together:

1. **Late `ICurrentUserService.UserId` resolution inside the catch block (FHQ-27 fix).** `CalendarSyncService.SyncAllAsync` originally resolved `currentUserService.UserId` lazily inside the catch handler, after several `await` boundaries. `IHttpContextAccessor.HttpContext` is AsyncLocal-backed; under certain async-flow conditions it would be unobservable, returning null and silently short-circuiting the mark.
2. **HttpClient `DefaultRequestHeaders.Authorization` shared across requests (FHQ-27 fix).** `GoogleCalendarClient.SetAuthorizationHeaderAsync` mutated `_httpClient.DefaultRequestHeaders.Authorization` — process-shared state on a typed client. Concurrent users could race on the header.
3. **No push-based connection-status surface (FHQ-28 fix).** The dashboard and Calendars-tab read `/api/calendars/connection-status` only inside `OnInitializedAsync`. Any AuthStatus transition that happened during a SignalR-connected session was invisible until a full page reload. The test relied on `page.GotoAsync("/")` triggering a fresh `OnInitializedAsync` — usually it did, occasionally it didn't, depending on Blazor's `NavigationManager` interceptor behaviour and the surrounding network state.

**Fix:**
- Capture `userId` at the top of `SyncAllAsync` and pass it explicitly to `MarkUserNeedsReauthAsync(capturedUserId, ...)`. Removed the late-resolve path.
- Build a fresh `HttpRequestMessage` per call site in `GoogleCalendarClient` (`BuildAuthorizedRequestAsync`) and attach the Authorization header there. `_httpClient.DefaultRequestHeaders.Authorization` is never mutated.
- New `IConnectionStatusBroadcaster` / `SignalRConnectionStatusBroadcaster` (mirrors `IThemeBroadcaster` / `SignalRThemeBroadcaster`). `DatabaseTokenStore.MarkNeedsReauthAsync` and `SaveRefreshTokenInternalAsync` broadcast `ConnectionStatusUpdated` after every AuthStatus transition.
- `SignalRService` exposes `OnConnectionStatusUpdated`; `Index.razor` and `SettingsCalendarsTab.razor` subscribe and re-fetch connection status on receipt.
- `Index.razor.RefreshDataFromSignalR` also re-fetches connection status as defence-in-depth.
- E2E reauth-flow `[Then]` steps navigate with `WaitUntilState.NetworkIdle`.
- E2E diagnostic instrumentation retained in the codebase: on banner-timeout or wrong-badge-label, the failing step dumps the WebApi's `/api/calendars/connection-status` response, the manual-sync HTTP status code, and page state into xUnit Standard Output (prefix `[FHQ-28 diagnostic]`). This is left in place so any future recurrence is immediately diagnosable.

**Verification:**
- 5 consecutive Deploy-Dev passes on the FHQ-28 branch with all 4 SyncResilience scenarios green: #354, #355, #356, #357, #358.
- 2 consecutive Deploy-Staging passes with all 4 SyncResilience scenarios green: #102 (clean run, all tests passed) and #103 (all 4 SyncResilience scenarios green; one unrelated test flake — `Remove calendar chip from event` — tracked as a separate issue FHQ-29).

This is the 7-run streak the previous entry version of this issue required as the bar to declare resolved.

**If the symptom returns:**
1. First triage: `jk log FamilyHQ-Deploy-Dev <n> | grep "FHQ-28 diagnostic"`. The diagnostic captures the manual-sync HTTP response status (200 = silent success → check `currentUserService.UserId` resolution; 409 = correctly rejected → bug is downstream) and the `/api/calendars/connection-status` response (`active` = not persisted → bug is in the WebApi mark path; `needs_reauth` = persisted but UI didn't surface it → bug is in SignalR client wiring or the page render path).
2. Cross-check that `GoogleCalendarClient` still uses `BuildAuthorizedRequestAsync` for every call site and `_httpClient.DefaultRequestHeaders.Authorization` is never mutated.
3. Cross-check that `DatabaseTokenStore` still calls `_connectionStatusBroadcaster.BroadcastConnectionStatusUpdatedAsync` after both AuthStatus transitions.
4. Cross-check that `Index.razor` and `SettingsCalendarsTab.razor` still subscribe to `SignalR.OnConnectionStatusUpdated` and unsubscribe in their DisposeAsync.

### 2. EventModalTimePicker scenario clicks wrong row when day-view auto-scrolls

**Resolved:** branch `fix/FHQ-17-day-grid-click-scroll-pin`, commit `9bab7f8` (2026-05-10). Tracked as FHQ-17.
**Component:** `tests-e2e/FamilyHQ.E2E.Common/Pages/DashboardPage.cs` → `ClickDayGridSlotAsync`.
**First seen:** Deploy-Staging #89 (2026-05-09).
**Occurrences:** Deploy-Staging #89 only. The same commit passed Deploy-Dev #307–#310 and the failed scenario's sibling `Adjusting the end time leaves the start time unchanged` passed on the same staging run, confirming an order- and timing-dependent race rather than a uniform regression.

**Symptom:**
```
Locator expected to have text '14'
But was: '18'
  - waiting for GetByTestId("start-time-picker").Locator(".time-picker-display").First
  -   14 × locator resolved to <div aria-live="polite" class="time-picker-display">18</div>
```

**What we knew:**
- The click step `ClickDayGridSlotAsync` used `col.ClickAsync(new() { Position = new Position { X = 10, Y = totalMinutes } })` on a `.calendar-col` element rendered at exactly 1440 px tall (1 px = 1 minute).
- The column lives inside `#day-view-container { height: 75vh; overflow-y: auto }` so only ~75 vh of the 1440 px column is ever in view.
- `DayView.OnAfterRenderAsync` asynchronously sets `#day-view-container.scrollTop = currentMins - clientHeight/4` whenever `SelectedDate == Today`, via a `JSRuntime.InvokeVoidAsync("eval", …)`.
- Playwright independently scrolls the scrollable ancestor to bring the `Position` click target into view before dispatching the click.
- When these two scrolls interleaved unfavourably, Playwright's `BoundingBoxAsync` snapshot disagreed with the dispatched click coordinates. The browser then computed `event.target` / `event.offsetY` against a different row, and the production handler `DayView.HandleGridClick` (`totalMinutes = (int)e.OffsetY`) opened the modal with the wrong start time.

**Root-cause fix:**
- `ClickDayGridSlotAsync` now pins `#day-view-container.scrollTop` deterministically from the test (`Page.EvaluateAsync(...)`) **before** dispatching the Playwright click. The pin centres the target row in the visible band, so both Playwright's geometry and the production click handler agree on what column-relative Y means. The app's auto-scroll-to-now feature is unchanged — it remains a real product behaviour for users.
- The dead `colBox` / `BoundingBoxAsync` null-check fallback (which would have produced wrong times if it ever triggered) was removed in the same change.

**If the symptom returns:**
1. The JSRuntime scroll in `DayView.OnAfterRenderAsync` may have changed shape — e.g. now writes after a `Task.Delay` or in response to a separate event. Move the test's scroll pin closer to the click, or pin twice (before *and* after a short wait).
2. Consider exposing a per-hour `data-testid` on the grid slots in `DayView.razor` and switching the test to address by testid rather than pixel offset. More invasive but eliminates pixel-based geometry from the test surface entirely.

### 1. Weather refresh returns 200 but `/current` returns 204

**Resolved:** branch `fix/weather-refresh-race` (2026-04-10)
**Component:** `tests-e2e/FamilyHQ.E2E.Steps/WeatherSteps.cs` → `WhenIWaitForWeatherDataToLoad`
**First seen:** Deploy-Dev #237 (2026-04-08)
**Occurrences:** Deploy-Dev #237, #240 — both on the `Disabling weather hides the weather strip` scenario in `Weather.feature`. Other weather scenarios in the same runs (e.g. `Weather strip shows current temperature and condition`) used the identical `Background` and `WhenIWaitForWeatherDataToLoad` step and passed.

**Symptom:**
```
System.InvalidOperationException : Weather API returned 204 after refresh
(refresh POST returned 200) — expected 200. The refresh may have failed
to store data.
```
The E2E poll ran `/api/weather/current` every 500ms for 30s and never saw a 200 — i.e. it was not a timing race within the poll window, the data was genuinely never visible.

**What we knew:**
- `POST /api/weather/refresh` returned **200** — the request reached the server and `WeatherRefreshService.RefreshAsync` did not throw.
- `GET /api/weather/current` returned **204** for the entire 30s window — `WeatherService.GetCurrentAsync` returned null. This can only happen if either `locationSettingRepository.GetAsync(userId)` returns null (no location for that user) or `weatherDataPointRepository.GetCurrentAsync(locationId)` returns null (location exists but no current data point stored).
- `WeatherRefreshService.RefreshAsync` previously silently returned with status 200 if `locationRepo.GetAsync(userId)` returned null. This was the most likely path to "refresh succeeded but stored nothing".
- The location IS saved before the refresh runs — `GivenTheUserHasASavedLocation` waits for `.settings-location-pill` to appear, which the Razor component only renders after `SaveLocationAsync` returns successfully.
- `POST /api/settings/location` internally called `_weatherRefreshService.RefreshAsync(userId, ct)` immediately after `_locationRepo.UpsertAsync`, so the refresh happened *inside* the same request as the location save. The DbContext is request-scoped and shared between repos, so this should not have raced — but PostgreSQL read-committed semantics combined with EF's change tracker behaviour left us unable to fully rule out a same-context visibility quirk.
- Per-scenario browser context isolation was verified (`MasterHooks.SetupBrowserAsync`/`TeardownBrowserAsync`), so cross-test JWT/user leakage was not in play.

**Previous mitigation** (commit `abaffae`, 2026-04-08):
- `WhenIWaitForWeatherDataToLoad` retried the refresh+poll cycle up to 3 times. This masked but did not fix the problem: #240 still failed through the retries because the same request path was exercised each time.

**Root-cause fix:**
- `WeatherRefreshService.RefreshAsync` now returns a structured `WeatherRefreshResult` (`Succeeded` / `SkippedWeatherDisabled` / `SkippedNoLocation`) instead of silently returning with status 200 on a no-op. An entry log line records `UserId`, and the success log records `LocationId`, resolved place name, lat/lon and the number of data points written — exactly the forensic trail that would have made root-causing this immediate.
- `WeatherController.Refresh` translates skipped outcomes into **409 Conflict** with a structured `message`, so a client call that runs before a location is committed fails loudly instead of reporting success.
- `WeatherController.Refresh` now also verifies that `WeatherService.GetCurrentAsync` returns non-null data *before* returning 200. If the refresh reported success but no current data point is visible to a subsequent read, the endpoint returns **503 Service Unavailable** with `locationSettingId` and `dataPointsWritten` diagnostics. This turns the intermittent downstream 204 into a single clear server-side failure on the refresh call itself.
- `SettingsController` still calls `RefreshAsync` after a location save, but the new structured result means any same-request visibility quirk is surfaced in logs rather than silently swallowed.
- Unit test coverage in `WeatherControllerTests` pins all four new outcome paths (success, 409 no-location, 409 disabled, 503 not-visible).

**If the symptom returns:**
1. A 503 on `POST /api/weather/refresh` with `dataPointsWritten > 0` confirms the same-request visibility quirk — check the `weatherdataPoint` INSERTs were committed before the `WeatherService.GetCurrentAsync` SELECT ran. Likely next step is to push the post-save refresh out of the same request (fire-and-forget or a follow-up client call).
2. A 409 with `SkippedNoLocation` confirms the caller's location was not visible at refresh time — check whether `POST /api/settings/location` returned before the E2E moved on, and confirm the same userId is used for both calls.
3. The E2E retry loop in `WhenIWaitForWeatherDataToLoad` is now backed by a fail-fast server, so a test failure here carries real diagnostic context in the WebApi logs.
