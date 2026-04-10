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

### 1. Weather refresh returns 200 but `/current` returns 204

**Component:** `tests-e2e/FamilyHQ.E2E.Steps/WeatherSteps.cs` → `WhenIWaitForWeatherDataToLoad`
**First seen:** Deploy-Dev #237 (2026-04-08)
**Occurrences:** Deploy-Dev #237, #240 — both on the `Disabling weather hides the weather strip` scenario in `Weather.feature`. Other weather scenarios in the same runs (e.g. `Weather strip shows current temperature and condition`) used the identical `Background` and `WhenIWaitForWeatherDataToLoad` step and passed.

**Symptom:**
```
System.InvalidOperationException : Weather API returned 204 after refresh
(refresh POST returned 200) — expected 200. The refresh may have failed
to store data.
```
The poll runs `/api/weather/current` every 500ms for 30s and never sees a 200 — i.e. it is not a timing race within the poll window, the data is genuinely never written.

**What we know:**
- `POST /api/weather/refresh` returns **200** — the request reached the server and `WeatherRefreshService.RefreshAsync` did not throw.
- `GET /api/weather/current` returns **204** for the entire 30s window — `WeatherService.GetCurrentAsync` returns null. This happens if either:
  1. `locationSettingRepository.GetAsync(userId)` returns null (no location for that user), **or**
  2. `weatherDataPointRepository.GetCurrentAsync(locationId)` returns null (location exists but no current data point stored).
- `WeatherRefreshService.RefreshAsync` (`src/FamilyHQ.Services/Weather/WeatherRefreshService.cs:27-33`) silently returns with status 200 if `locationRepo.GetAsync(userId)` returns null. This is the most likely path to "refresh succeeded but stored nothing".
- The location IS saved before the refresh runs — `GivenTheUserHasASavedLocation` waits for `.settings-location-pill` to appear, which the Razor component only renders after `SaveLocationAsync` returns successfully.
- `POST /api/settings/location` (`src/FamilyHQ.WebApi/Controllers/SettingsController.cs:108`) internally calls `_weatherRefreshService.RefreshAsync(userId, ct)` immediately after `_locationRepo.UpsertAsync`, so the refresh happens *inside* the same request as the location save. The DbContext is request-scoped and shared between repos, so this should not race — but PostgreSQL read-committed semantics combined with EF's change tracker behaviour leave us unable to fully rule out a same-context visibility quirk.
- Per-scenario browser context isolation is verified (`MasterHooks.SetupBrowserAsync`/`TeardownBrowserAsync`), so cross-test JWT/user leakage is not in play.

**Suspected root causes (unverified, in rough order of likelihood):**
1. **DbContext / save-vs-refresh race inside `POST /api/settings/location`** — the post-upsert refresh sometimes sees no location for the user and silently no-ops, leaving zero data points. The next test refresh then ALSO sees nothing because the *real* failure was the absence of seeded weather data on the first refresh, and replacing nothing with nothing is a no-op.
2. **`WeatherRefreshService.RefreshAsync` silent no-op when location is null** is by itself a foot-gun: any call that runs before a location is committed will return 200 and store nothing.
3. **Lat/lon precision drift** between the seeded simulator location and the simulator's `/v1/forecast` lookup — possible but unlikely; the simulator uses 0.001 tolerance and the test offsets are in the 0.01–0.099 range.

**Mitigation in place** (commit `abaffae`, 2026-04-08):
- `WhenIWaitForWeatherDataToLoad` retries the refresh+poll cycle up to 3 times. Each iteration POSTs `/api/weather/refresh` then polls `/current` for up to 10s. Re-triggering the refresh from a fresh request is expected to resolve the race in practice.

**What a real fix looks like:**
- Replace the silent no-op in `WeatherRefreshService.cs:29-33` with either a fail-fast (throw) or a synchronous re-fetch of the location after a short delay. Failing loudly here would surface the real bug instead of letting it appear as a downstream test flake.
- Alternatively, decouple the post-save refresh from `POST /api/settings/location` entirely — schedule it as a fire-and-forget background job or remove it and let the next user-initiated refresh fetch the data.
- Add a server-side log line on `RefreshAsync` entry that includes the userId and resolved location ID — would have made root-causing this immediate.

**If this recurs:**
1. Pull the WebApi container logs for the failing run window — look for the `"No location configured for user {UserId}. Skipping weather refresh."` message. If present → root cause confirmed as path 1/2 above.
2. Check whether the failing scenario logs the same `userId` for both the location save and the refresh.
3. Don't dismiss as flake even if it's just one occurrence — two occurrences in this case were enough to confirm reproducibility.

---

## Resolved issues

*(none yet — when an issue is fixed, move it here with the resolving commit hash and date.)*
