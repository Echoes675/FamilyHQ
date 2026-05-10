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

*(none currently — see Resolved issues below.)*

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
