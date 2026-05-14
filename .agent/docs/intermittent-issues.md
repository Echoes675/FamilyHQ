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

### 3. Calendar API 403 path does not always mark UserToken as NeedsReauth

**Component:** `src/FamilyHQ.Services/Calendar/CalendarSyncService.cs` — `SyncAllAsync` outer catch around `GetCalendarsAsync`. Surfaced by an E2E scenario authored under FHQ-26.
**First seen:** Deploy-Dev #328 (2026-05-14).
**Occurrences:** Deploy-Dev #328, #330, #331, #332 — same scenario, roughly one run in two. The companion scenario for the `invalid_grant` refresh-token path uses the same `SyncAllAsync.catch (GoogleReauthRequiredException)` block + `tokenStore.MarkNeedsReauthAsync` flow and passes on every observed run, so the divergence is between the two failure entry points, not the marking machinery itself.

**Symptom:**
The simulator is configured to return `403 Forbidden` from `/users/me/calendarList` for the test user (per-user failure-mode store in `tools/FamilyHQ.Simulator/State/SyncFailureModeStore.cs`). The test triggers a manual sync via Settings → Sync Now and then navigates to `/diagnostics`. On a failing run the status badge renders **Active** instead of **Needs Reauth**, and `/api/calendars/connection-status` returns `status: "active"` — i.e. the WebApi did not actually persist `AuthStatus = NeedsReauth` for this user despite the sync attempt going through.

**What we know:**
- The simulator hook (`SyncFailureResponse.TryBuild`) is correctly scoped per-user (`_failureStore.Get(userId)`), and the userId extracted from the access token matches the userId the test set the failure mode under (verified by tracing `OAuthController.Token` + `CalendarsController.ExtractUserId`).
- `MarkNeedsReauthAsync` (`DatabaseTokenStore.cs:163`) commits via `SaveChangesAsync` and works reliably for the invalid_grant path, which goes through the same `SyncAllAsync` catch block.
- Per-event resilience (`SyncCoreAsync`) is not in play here — the 403 throws from `GetCalendarsAsync` *before* the per-event loop, so the change made for FHQ-26 cannot regress this path.
- Unit tests for both reauth paths pass (`CalendarSyncServiceTests`, `DatabaseTokenStoreTests`).

**Hypotheses worth investigating next:**
1. **`ICurrentUserService.UserId` is intermittently null** when the outer `catch` in `SyncAllAsync` runs. The `MarkCurrentUserNeedsReauthAsync` helper short-circuits without throwing when `UserId` is empty (only logs an error), so the symptom would be exactly what we see. The invalid_grant path may pre-load the user context earlier (during `GetRefreshTokenAsync`), masking the same race for that scenario.
2. **HttpClient `DefaultRequestHeaders.Authorization` shared across requests** — `GoogleCalendarClient.SetAuthorizationHeaderAsync` mutates `_httpClient.DefaultRequestHeaders.Authorization`. If `HttpClient` is shared (typed-client / factory), this mutation races against any other concurrent caller (e.g. a webhook receiver). For the 403 path this could cause the simulator to see a stale bearer token for a different user and return 200 (since their failure mode is unset) — sync would silently "succeed", leaving AuthStatus untouched.
3. **Postgres connection-pool / read-after-write delay** on `UserTokens` — unlikely with read-committed isolation on the same node, but worth ruling out before declaring it a logic bug.

**Mitigation in place:** *all three* reauth-flow E2E scenarios are **excluded** from `SyncResilience.feature`. They each rely on the sync-marks-user-NeedsReauth invariant the WebApi sometimes breaks, and observed flake rates were too high to gate on:
- "Reauth banner shows the Google-supplied reason when Calendar API returns 403" — ~50% flake.
- "Diagnostics page shows needs-reauth status with reconnect button" (invalid_grant variant) — ~25% flake even with a sync-trigger retry guard.
- "Reauth banner appears when Google revokes the refresh token" — ~10% flake (Deploy-Dev #340).

Only the per-event-resilience scenario remains in the feature — that one does not depend on the reauth marking and has passed every recent run. Static display logic for the reauth UI is unit-tested in `ReauthBannerTests`, `DiagnosticsViewTests`, `DiagnosticsControllerTests`, `CalendarSyncServiceTests`, `DatabaseTokenStoreTests`, and `SyncControllerTests`, so the reauth surface is not unprotected — it just lacks an end-to-end check.

The simulator's `sync-failure-mode` backdoor (`tools/FamilyHQ.Simulator/State/SyncFailureModeStore.cs`) and its hooks in the OAuth and Calendars controllers remain in place. Once the WebApi race is fixed, the excluded scenarios can be restored and the backdoor will be the right way to drive them.

**To remove the active note:** add the excluded scenarios back, run Deploy-Dev five times consecutively, and confirm all five pass.

---

## Resolved issues

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
