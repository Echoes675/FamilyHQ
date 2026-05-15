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

### 3. Reauth-flow E2E scenarios still flake on dev and staging (FHQ-27 fix incomplete)

**Status:** **Re-opened** after appearing resolved. Originally tracked as FHQ-27; the current investigation is FHQ-28 (branch `fix/FHQ-28-staging-reauth-banner-investigation`, PR not yet open). **Do not assume the FHQ-27 commits closed this.**

**Component:** Surface symptom in `tests-e2e/FamilyHQ.E2E.Features/WebUi/SyncResilience.feature`. Underlying mechanism somewhere across `src/FamilyHQ.Services/Auth/DatabaseTokenStore.cs`, `src/FamilyHQ.Services/Calendar/CalendarSyncService.cs`, `src/FamilyHQ.WebApi/Controllers/SyncController.cs`, the simulator's per-user failure-mode store, and the WebUi's connection-status fetch path.

**First seen:** Deploy-Dev #328 (2026-05-14). **Most recent occurrence:** Deploy-Dev #353 (2026-05-15).
**Occurrences:** #328, #330, #331, #332, #340, #350, #353. Two FHQ-27 verification runs claimed to be clean (#344–#348) and the FHQ-27 retrospective entry was written based on those — that was over-confident; the very next Deploy-Staging (#101) failed the same scenario, and Deploy-Dev #350 (FHQ-28 branch, before any new fix) and #353 (FHQ-28 branch with broadcaster + UI subscriptions in place) both failed.

**Symptom (current):**
On a non-trivial fraction of runs that exercise the reauth-flow scenarios, one of the four SyncResilience scenarios fails. The specific failing scenario *varies* between runs:
- "Reauth banner shows the Google-supplied reason when Calendar API returns 403" — failed Deploy-Staging #101 and Deploy-Dev #350 (banner-not-visible timeout).
- "Diagnostics page shows needs-reauth status with reconnect button" — failed Deploy-Dev #353 (label rendered `Active`, not `Needs Reauth`).
- Refresh-token-revoke banner and per-event resilience continue to pass in the failures observed so far.

The Deploy-Dev #353 failure is the most informative occurrence we have: the diagnostics page directly read connection status from the backend and got `Active`. This means **the WebApi did not persist `AuthStatus = NeedsReauth` on that run**, not "the UI failed to surface it". The failure is on the server side, despite FHQ-27 fixing the late-userId-resolution path and the HttpClient shared-header path.

**What FHQ-27 did fix:**
- Per-request `HttpRequestMessage.Headers.Authorization` instead of mutating `_httpClient.DefaultRequestHeaders.Authorization`. This is a real correctness improvement and was almost certainly *a* contributing factor, just not the only one.
- `userId` captured at the top of `SyncAllAsync` and threaded through `MarkUserNeedsReauthAsync(capturedUserId, ...)` — closes the lazy-resolve-in-catch path. Diagnostic logging confirmed zero divergence across the 5 verification runs, so this race did not bite in those runs (but the code path was demonstrably unsafe and the guard is still load-bearing).

**What FHQ-28 added** (already committed on the branch, kept regardless of investigation outcome):
- New `IConnectionStatusBroadcaster` + `SignalRConnectionStatusBroadcaster`; `DatabaseTokenStore` broadcasts `ConnectionStatusUpdated` whenever AuthStatus transitions in either direction.
- Dashboard (`Index.razor`) and Calendars-tab (`SettingsCalendarsTab.razor`) subscribe to the new SignalR event and re-fetch `/api/calendars/connection-status`.
- `Index.razor.RefreshDataFromSignalR` also re-fetches connection status as defence-in-depth.
- E2E reauth-flow `[Then]` steps navigate with `WaitUntilState.NetworkIdle`.
- E2E diagnostic instrumentation: on banner-timeout or wrong-badge-label, dump the WebApi's `/api/calendars/connection-status` response and the manual-sync HTTP status code into the xUnit Standard Output (look for `[FHQ-28 diagnostic]` prefix).

**Hypotheses still in play after FHQ-28:**
1. **The simulator's `/token` endpoint is racy under load.** The simulator looks up failure-mode keyed by userId (derived from the refresh_token). If the lookup misses (timing or state-store race), the simulator returns 200 with a fresh access token; WebApi syncs normally; AuthStatus stays Active.
2. **`SyncController.TriggerSync` has no `[Authorize]` attribute.** If the JWT cookie sometimes fails to authenticate the request, `CurrentUserService.UserId` is null, `SyncAllAsync` early-returns with a Warning log, and `SyncController` returns 200 OK "Sync completed successfully" — falsely. The test's `ClickSyncNowAsync` accepts any response status.
3. **Cross-user state in the simulator's failure-mode store.** If two scenarios run close in time and share the same simulator process, a race in `SyncFailureModeStore.Set` / `.Get` could mean the failure mode for user A is overwritten or not yet visible when A's sync arrives.
4. **Postgres write-then-read visibility for the same request.** SyncController calls `SyncAllAsync`, which calls `tokenStore.MarkNeedsReauthAsync` (different DbContext scope) which writes and commits. The same SyncController then returns. A subsequent GET `/api/calendars/connection-status` reads from a fresh DbContext. Single-node read-committed Postgres should make this visible, but is not 100% conclusively ruled out.

**Mitigation in place:**
- The 4 SyncResilience scenarios remain in `SyncResilience.feature` while we collect diagnostic data. We have NOT pulled them again — the diagnostic only fires on failure and adds zero overhead on the happy path.
- The FHQ-28 PR is not yet open. The 5-Deploy-Dev pre-PR gate is being re-established after the diagnostic landed (commit `ef1db6c`); the post-merge acceptance criterion remains 2 consecutive Deploy-Staging passes.

**Next data we need:**
The next time a SyncResilience scenario fails on Deploy-Dev, the `[FHQ-28 diagnostic]` Standard Output entries will tell us:
- The manual-sync HTTP response status (200 = silent success → Hypothesis 2 or 1; 409 = correctly rejected → bug is elsewhere).
- The current `/api/calendars/connection-status` response (`active` = not persisted; `needs_reauth` = persisted but UI didn't show it).

Together those two facts will discriminate cleanly between the remaining hypotheses. The very next post-fix Deploy-Dev failure on this branch should be triaged via `jk log FamilyHQ-Deploy-Dev <n> | grep "FHQ-28 diagnostic"` first.

**To remove the active note:**
Run Deploy-Dev five times consecutively *with no failures on any of the four SyncResilience scenarios*, then run Deploy-Staging twice consecutively with the same constraint. Only after that 7-run streak should this be moved back to *Resolved* — the FHQ-27 retrospective was written after a 5-Deploy-Dev streak that turned out to be insufficient evidence.

---

## Resolved issues

### (former #3 — Calendar API 403 path mark race — moved to Active above on 2026-05-15 after recurrence)

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
