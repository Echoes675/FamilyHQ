# Driving the running system

Two channels for interacting with a locally-running stack (`dev-stack up`).

## UI — Playwright MCP

Use the in-session Playwright browser tools against the WebUi:
- Navigate to `https://localhost:7154`, then snapshot / click / type / screenshot.
- The frontend is Blazor WASM; wait for the app to hydrate before asserting.

## HTTP — API + Simulator (decision: native tooling, no helper)

Use `Invoke-RestMethod` / `curl`. The Simulator backdoor seeds test data; the canonical
payload reference is `tests-e2e/FamilyHQ.E2E.Data/Api/SimulatorApiClient.cs`.

### Health
- `GET https://localhost:7199/health` (Simulator)
- `GET https://localhost:7196/api/health` (WebApi)

### Simulator backdoor (seed / inspect)
- `POST /api/simulator/configure` — seed an isolated per-scenario user.
- `POST /api/simulator/backdoor/events` · `GET /api/simulator/backdoor/events?userId=&calendarId=&summary=`
- `PUT|DELETE /api/simulator/backdoor/events/{id}`
- `POST /api/simulator/backdoor/events/poison` — inject an oversized-title event to exercise per-event resilience.
- `POST /api/simulator/backdoor/weather` · `DELETE /api/simulator/backdoor/weather?latitude=&longitude=`
- `POST /api/simulator/backdoor/location` · `DELETE /api/simulator/backdoor/location?placeName=`
- `GET|DELETE /api/simulator/backdoor/webhooks`
- `POST|DELETE /api/simulator/backdoor/sync-failure-mode` — inject / clear per-user sync failure modes (`RefreshTokenInvalidGrant`, `CalendarApi401`, `CalendarApi403`).
- `GET /api/simulator/backdoor/write-counts/{eventId}` · `GET /api/simulator/backdoor/write-counts/user/{userId}/total` · `DELETE /api/simulator/backdoor/write-counts/user/{userId}` — outbound write counters for echo-guard assertions.

### Example
```powershell
Invoke-RestMethod -SkipCertificateCheck -Method Post `
  -Uri https://localhost:7199/api/simulator/backdoor/events `
  -ContentType application/json -Body (Get-Content ./event.json -Raw)
```

> Decision record: Playwright covers the UI; native HTTP tooling plus this backdoor
> catalogue covers the API/Simulator. No dedicated REST helper script is maintained — it
> would duplicate `SimulatorApiClient`.
