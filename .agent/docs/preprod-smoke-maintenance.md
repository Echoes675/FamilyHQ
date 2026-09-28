# Preprod Smoke Suite Maintenance Guide

The preprod smoke suite (`tests-smoke/`, FHQ-141) exists to defend production from an outage or a
FamilyHQ bug **in the parts of the system that talk to third parties**: Google OAuth, the Google
Calendar API, Google push notifications relayed by RelayRobin, and Open-Meteo.

It is **not** a second E2E suite. Internal UI behaviour stays covered by
[`e2e-testing-maintenance.md`](e2e-testing-maintenance.md) against the Simulator, and the Simulator is
a test double — a green E2E run is not proof of compatibility with Google. Smoke proves each *kind* of
third-party interaction works for real, against the real preprod environment and the real Google
account.

## Table of Contents

1. [Running the suite](#running-the-suite)
2. [Configuration](#configuration)
3. [In the preprod pipeline](#in-the-preprod-pipeline)
4. [Principles you must not quietly relax](#principles-you-must-not-quietly-relax)
5. [Project structure](#project-structure)
6. [Preflight: what each check proves and what each failure means](#preflight-what-each-check-proves-and-what-each-failure-means)
7. [Scenarios: what each one proves](#scenarios-what-each-one-proves)
8. [Re-signing in when the Google grant is revoked](#re-signing-in-when-the-google-grant-is-revoked)
9. [Diagnosing a failure](#diagnosing-a-failure)
10. [Adding a scenario](#adding-a-scenario)
11. [Maintenance checklist](#maintenance-checklist)

---

## Running the suite

Every preprod deploy runs it already — see [in the preprod pipeline](#in-the-preprod-pipeline). What
follows is how to run it by hand, which is what you do when a pipeline run went red or when you are
changing the suite.

### Prerequisites

- **.NET 10 SDK.**
- **Chromium for Playwright.** Installed once per machine:
  ```bash
  dotnet build tests-smoke/FamilyHQ.Smoke.Features/FamilyHQ.Smoke.Features.csproj -c Release
  pwsh tests-smoke/FamilyHQ.Smoke.Features/bin/Release/net10.0/playwright.ps1 install chromium
  ```
- **Network line of sight to preprod.** The target is on the LAN
  (`https://preprod.familyhq.alphaepsilon.co.uk:8400`). The Jenkins agent is on the same network.
- **Trust for the internal CA.** Preprod's certificate validates from the LAN today, and the suite
  validates it. `Smoke__AllowUntrustedCertificate=true` exists for an exploratory run from a machine
  that cannot trust the CA; do not set it in a pipeline — the fix there is to trust the CA.

There is **no database dependency at all**: no Npgsql, no connection string, no Data Protection
certificate. Everything the suite knows it learns from preprod's API or from Google.

### The whole suite

```bash
Smoke__BaseUrl=https://preprod.familyhq.alphaepsilon.co.uk:8400 \
Smoke__IssueTokenSecret=<shared secret> \
Smoke__UserId=<smoke account Google sub> \
Smoke__ExpectedLocation=dublin \
Smoke__ExpectedTimeZone=Europe/Dublin \
Smoke__SharedCalendar=Household \
Smoke__MemberCalendars=James,Kirk,Lars,Rob \
Smoke__PushIncapableCalendars="Holidays in United Kingdom" \
dotnet test tests-smoke/FamilyHQ.Smoke.Features/FamilyHQ.Smoke.Features.csproj
```

### Preflight only

Useful as a five-second answer to "is preprod fit to test against?":

```bash
dotnet test tests-smoke/FamilyHQ.Smoke.Features/FamilyHQ.Smoke.Features.csproj \
  --filter "FullyQualifiedName~PreprodEnvironmentHealth"
```

### One scenario

Filter on the Reqnroll-generated method name — the scenario title in PascalCase with punctuation
removed, exactly as in the E2E suite. Note that a **hyphen becomes an underscore**, so
"A two-member event…" generates `ATwo_MemberEventIsWrittenOnceToTheSharedCalendar`:

```bash
dotnet test tests-smoke/FamilyHQ.Smoke.Features/FamilyHQ.Smoke.Features.csproj \
  --filter "FullyQualifiedName~ATwo_MemberEventIsWrittenOnceToTheSharedCalendar"
```

If a filter resolves to zero tests, read the generated name out of the `.feature.cs` beside the
feature file rather than guessing at the punctuation:

```bash
grep -oE "Task [A-Za-z0-9_]+\(\)" tests-smoke/FamilyHQ.Smoke.Features/KioskToGoogle.feature.cs
```

`--filter "Scenario=<title>"` resolves to zero tests; Reqnroll does not expose the human title as a
trait.

### Parallelism: one

`xunit.runner.json` pins `maxParallelThreads: 1` and `parallelizeTestCollections: false`. This is not
a performance oversight. Unlike E2E — where every scenario provisions its own isolated Simulator user —
smoke scenarios share **one** live environment, **one** Google account and **one** set of calendars.
Two of them running at once would write to the same calendars and count each other's events. Do not
raise it.

---

## Configuration

Loaded from `appsettings.json` plus environment overrides (the same pattern as E2E's
`TestConfiguration`). The checked-in `appsettings.json` carries **empty placeholders** and a comment
saying where the real values come from; **never commit a value into it.**

| Key | What it is |
|---|---|
| `Smoke__BaseUrl` | preprod's origin. Serves both the kiosk and `/api` — preprod routes both behind one hostname. |
| `Smoke__IssueTokenSecret` | Shared secret for the two smoke token endpoints. **A secret.** Comes from the Jenkins credential that holds preprod's smoke settings; it is the same value as `Auth__IssueTokenEndpoint__Secret` in preprod's environment. |
| `Smoke__UserId` | The smoke account's Google subject identifier. The token endpoints mint for this account and no other. |
| `Smoke__ExpectedLocation` | The place name preprod is expected to have saved (`dublin`). An **expectation**, asserted by preflight — the suite never saves a location. |
| `Smoke__ExpectedTimeZone` | The IANA zone preprod is expected to resolve as the family's (`Europe/Dublin`). Anchors every wall-clock and recurrence assertion, and is pinned onto the kiosk browser. |
| `Smoke__SharedCalendar` | The calendar expected to be flagged shared — the container multi-member events go to (`Household`). |
| `Smoke__MemberCalendars` | The member calendars, comma-separated (`James,Kirk,Lars,Rob`). |
| `Smoke__PushIncapableCalendars` | Calendars that legitimately have no Google push channel — a read-only subscription such as `Holidays in United Kingdom`. Excluded from the webhook check rather than silently tolerated. |
| `Smoke__GoogleCalendarApiBaseUrl` | Google Calendar API root. Defaults to the real one; configurable so the oracle's address is explicit rather than assumed. |
| `Smoke__PushWaitSeconds` | How long a scenario waits for a change made in Google to reach preprod through the live push path. Default 180. |
| `Smoke__GoogleWaitSeconds` | How long a scenario waits for a kiosk write to become visible in Google. Default 60. |
| `Smoke__SyncHorizonDays` | How far ahead preprod's own sync reaches, in days (default 365). Not an environment expectation — a **product** bound. Only the yearly-series scenarios reach past it; without it their second occurrence would be reported as a disagreement with Google rather than as the designed edge of the sync window. If the product's horizon moves, move this with it. |
| `Smoke__Headless` | Run the kiosk browser headless. Default true. |
| `Smoke__AllowUntrustedCertificate` | Skip TLS chain validation. Default **false**; see the prerequisites. |

**Why the expectations are configuration and not literals.** Preprod drifts: at the time this suite
landed, two of its calendars still read `Work` and `Personal` because FHQ-211's rename fix had not
reached it, and `Household` was not yet flagged shared. The expected names, zone and location live in
configuration so the suite can follow the environment without a code change — and so that a
mismatch is reported as an environment fault rather than hidden by a convenient literal.

---

## In the preprod pipeline

`Jenkinsfile.deploy-preprod` runs the suite after every preprod deploy (FHQ-142), in two stages that
follow `Wait for Services`:

| Stage | What it runs | Why it is separate |
|---|---|---|
| **Smoke: Preflight** | `--filter "FullyQualifiedName~PreprodEnvironmentHealth"` — the seven checks and nothing else. It also does the one-off setup: extracts the browser and ICU libraries from the Playwright image if the agent lacks them, builds the suite `-c Release`, and installs Chromium. | It is the fast answer to "is preprod fit to test against?", and it separates an environment fault from a FamilyHQ defect *before* anything else runs. |
| **Smoke: Scenarios** | `--filter "FullyQualifiedName!~PreprodEnvironmentHealth"` — the rest of the suite, `--no-build` against what the preflight stage built. | So that a red run names which half is wrong in the stage view, without anyone having to read a trx first. |

### They gate the release (FHQ-143)

Neither stage is wrapped any more, and the `post.unstable` path that FHQ-142 used to keep them
advisory is gone. A smoke failure therefore:

- fails the **build**, so `post.success` never fires;
- **stops the release** — nothing is promoted to `FamilyHQ-Deploy-Production`;
- leaves preprod itself deployed and serving, since the deploy stages ran before the smoke ones.

**Preflight gates too**, deliberately. A run that cannot establish the environment is healthy has not
established anything about the release either, so promoting on it would be promoting on an unread
test. User's ruling, 2026-09-27: *"This should absolutely gate. We do not go to production without a
green run."*

**The cost, accepted knowingly.** Promotion now depends on things that are not the product: the smoke
Google account's grant staying valid, preprod's calendars matching the configured names, its saved
location and time zone. If any of those drift, releases stop until they are fixed — which is the
point, but it means the environment is now on the release's critical path.

**Break-glass — how to ship anyway.** Run `FamilyHQ-Deploy-Production` **manually** with
`DIRECTION=specific` and `SEMVER_TAG=vX.Y.Z`. It does not go through this chain, so it is unaffected
by a red smoke run. Use it when the failure is understood and the release must go — not to skip past
a failure nobody has read. If the cause is a lapsed grant, the re-sign-in runbook below fixes the
environment properly in about a minute, which is usually faster than reasoning about a bypass.

Read a red smoke stage as: *"preprod deployed fine; something about the real third-party path did not
hold."* That is something to investigate, not a flake to re-run — see
[diagnosing a failure](#diagnosing-a-failure) and, before dismissing anything,
[`intermittent-issues.md`](intermittent-issues.md).

One consequence worth knowing: the smoke stages run before `post`, so a release chain now waits the
length of a smoke run before the production deploy is triggered. That wait grew with full coverage. The
scenarios run one at a time by necessity (see [parallelism](#parallelism-one)), the Google-originated ones
each wait for a live push, and several of them make two changes in sequence — so budget tens of minutes
rather than a few. The cost is deliberate: the alternative is either fewer third-party interactions proven
or a sync triggered by hand, and a triggered sync would pass on an environment whose push path is dead.

### Scenarios are skipped when preflight fails

The preflight stage records its outcome in `SMOKE_PREFLIGHT_OK`, and `Smoke: Scenarios` checks it before
doing anything. If preflight did not pass, the scenarios stage logs `SKIPPED:` with the reason and stops.
This is not tidiness: the suite gates every scenario on a healthy preflight
([principle 1](#principles-you-must-not-quietly-relax)), so all of them would refuse in turn and spend a
couple of minutes restating the fault preflight has already named once. The flag is set to `false`
*before* the work, so a stage that dies in library extraction, the build or the browser install also
skips the scenarios rather than running them against an unbuilt suite.

### Where the configuration comes from

| Where | What | Why there |
|---|---|---|
| `Jenkinsfile.deploy-preprod`, the `environment {}` block | `Smoke__ExpectedLocation`, `Smoke__ExpectedTimeZone`, `Smoke__SharedCalendar`, `Smoke__MemberCalendars`, `Smoke__PushIncapableCalendars` and `SMOKE_PROJECT`. `Smoke__BaseUrl` is set from the pipeline's existing `APP_BASE_URL`. | None of it is secret, and all of it is an *expectation* about preprod. Whoever reads a red run needs to see what the run believed, in the diff, rather than opening a Jenkins credential to find out. |
| The `familyhq-preprod-env` file credential | `Auth__IssueTokenEndpoint__Secret` (the suite reads it as `Smoke__IssueTokenSecret`) and `Smoke__UserId`. | They must not be committed, and preprod's own environment file already carries both — one place to update beats two. |

The `runSmokeSuite()` helper at the foot of the Jenkinsfile reads those two keys out of the credential
file with an inline `read_key` shell function, modelled on the `Validate Production Config` stage in
`Jenkinsfile.deploy-prod`. Three things about it are load-bearing, and breaking any of them puts a secret
in a console log that outlives the build:

- the shell script is a **single-quoted** Groovy string, so Groovy interpolates nothing into it and
  neither value ever appears in a command line;
- `set +x` is its **first statement**, because Jenkins otherwise runs `sh` as `sh -xe` and a traced
  assignment prints what it assigned;
- nothing echoes either value — only whether it was missing.

Never add a `cat` of the env file, an `echo` of a matched line, or an interpolated `sh` string there.

### Where the results land

| What | Where |
|---|---|
| Test results | Published with the `mstest` step from `smoke-preflight-results.trx` and `smoke-scenario-results.trx`, so failures are readable from the build's **Test Result** page instead of by scrolling the console. |
| Failure screenshots | Archived as build artifacts from `**/TestResults/smoke-artifacts/*.png` by both stages (`allowEmptyArchive`, because preflight never drives a browser and so never produces one). One per failed kiosk scenario, named `<scenario>-<shortid>.png`. |
| Console output | `--logger "console;verbosity=detailed"`, so each scenario's correlation id and any `kiosk console:` lines are in the log to search Seq with. |

FHQ-141's first real failure was diagnosed from one of those screenshots. Look at it before theorising.

### Two things about the Jenkins agent

- **Globalization must not be invariant.** Both stages run with
  `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false` and the ICU libraries are part of what the library shim
  extracts. Without them `Europe/Dublin` cannot be resolved, and every wall-clock and recurrence
  assertion in the suite is anchored to that zone.
- **TLS is validated.** The suite validates preprod's certificate; the deploy stages' own health checks
  use `curl -sk` and therefore prove nothing about trust. Preprod's certificate is issued via DNS-01, so
  a stock trust store should accept it — but if *every* preflight check fails with a TLS error, fix the
  agent's trust. Do **not** set `Smoke__AllowUntrustedCertificate` in the pipeline.

---

## Principles you must not quietly relax

These are the reason the suite is worth anything. Each is load-bearing.

1. **Check the environment, never repair it.** Preflight asserts preprod's state and fails when it is
   wrong. Nothing in the suite writes a setting, registers a webhook, or renames a calendar.
   `PreprodApiClient` deliberately offers no verb that could. A bad environment followed by a green run
   is false confidence, so **every core scenario is gated on preflight being fully healthy**.
2. **No compensation.** A failing check fails the scenario immediately. No retries, no manual
   `POST /api/sync/trigger`, no "wait a bit longer and look again". `BoundedWait` is the *assertion* for
   an asynchronous outcome — the deadline is the specification of how long the live path may take — and
   there is deliberately no overload that retries. Anything gathered after a failure is **read-only**: a
   screenshot, the correlation id, the console errors, what Google currently holds.
3. **A correlation id per scenario.** A GUID from a hook, logged at the start, sent as
   `X-Correlation-Id` on every call the suite makes to preprod, seeded into the kiosk's
   `familyhq_session_correlation_id`, and written into **every** event description as
   `smoke-correlation: <guid>`. Events are located and confirmed by it before any assertion.
4. **A short id in every event title** (`Two-member outing · 7f3a9c`). Kiosk-side assertions see only
   the title, and smoke events are kept, so a title match without the short id would happily find an
   earlier run's event.
5. **Every series is bounded.** Three occurrences. `SmokeWeeklyRecurrence` has no "never ends" option,
   so a scenario cannot create an endless series by omission. The events are left behind for
   post-mortem; an unbounded series would keep expanding on a live calendar for ever.
6. **Google is the oracle.** Recurrence is compared against `events.instances`, never against a
   calculation of ours. So is every other assertion about a write: asking FamilyHQ what it wrote only
   establishes that FamilyHQ agrees with itself.
7. **No secret reaches a log, console or artifact.** The JWT, the Google access token and the shared
   secret are never passed to a log line or an exception message. `SecretGuard` additionally scrubs them
   out of the one place untrusted text is quoted — the body of a failed HTTP response.

Two more that are not on the ticket's list but follow from it:

- **No project reference to any `tests-e2e` project.** The duplicated page object and base page are the
  price, and it is the right price: the two suites answer different questions of different environments
  and must be free to drift. Coupling them would make every preprod quirk an E2E maintenance event.
- **Nothing in a test log that identifies a person.** A Google *primary* calendar's summary is the
  account's email address, so the preflight message that lists preprod's calendar names filters out any
  name containing `@`. The primary calendar is never an expected test calendar, so no diagnostic value
  is lost.

---

## Project structure

```
tests-smoke/
├── FamilyHQ.Smoke.Common/     # Configuration, correlation, clock, bounded wait, driver, page objects
├── FamilyHQ.Smoke.Data/       # preprod's API client, the Google oracle, the DTOs for both
├── FamilyHQ.Smoke.Steps/      # Preflight, hooks, step definitions
└── FamilyHQ.Smoke.Features/   # Gherkin features, appsettings.json, reqnroll.json, xunit.runner.json
```

Notable files:

| File | Why it matters |
|---|---|
| `FamilyHQ.Smoke.Common/Configuration/SmokeConfiguration.cs` | Every expectation about preprod, in one place. |
| `FamilyHQ.Smoke.Common/Correlation/SmokeCorrelation.cs` | The scenario's identity: full id for descriptions and headers, short id for titles. |
| `FamilyHQ.Smoke.Common/Helpers/FamilyClock.cs` | The family's zone, pinned onto the browser and used for every date the suite computes. |
| `FamilyHQ.Smoke.Common/Helpers/BoundedWait.cs` | The only way to wait for an asynchronous outcome. No retry overload, on purpose. |
| `FamilyHQ.Smoke.Common/Helpers/SecretGuard.cs` | Scrubs registered credentials out of quoted response bodies. |
| `FamilyHQ.Smoke.Common/Hooks/SmokePlaywrightDriver.cs` | Kiosk browser: injects the JWT into localStorage, pins the zone, collects console errors. |
| `FamilyHQ.Smoke.Common/Pages/SmokeDashboardPage.cs` | The only page object. Duplicated from E2E deliberately. |
| `FamilyHQ.Smoke.Data/Api/SmokeTokenClient.cs` | The two FHQ-139 endpoints. Registers every credential with `SecretGuard`. |
| `FamilyHQ.Smoke.Data/Api/PreprodApiClient.cs` | preprod's API, read-only by construction. |
| `FamilyHQ.Smoke.Data/Google/GoogleCalendarOracle.cs` | Google, read and write. The oracle, and the phone stand-in. |
| `FamilyHQ.Smoke.Steps/Preflight/SmokePreflight.cs` | The seven checks. Runs once per run; repairs nothing. |
| `FamilyHQ.Smoke.Steps/Hooks/SmokeScenarioHooks.cs` | Correlation, the health gate, the kiosk, failure forensics. |
| `FamilyHQ.Smoke.Steps/SmokeLookup.cs` | "What does Google hold for this scenario, and what does preprod serve?" |
| `FamilyHQ.Smoke.Steps/SmokeSeries.cs` | Google's answer about a series, and the one comparison every recurrence scenario ends in. |
| `FamilyHQ.Smoke.Steps/SmokeIcal.cs` | The RRULE lines the suite writes *into* Google — stated in the standard's own syntax rather than borrowed from FamilyHQ's rule builder, so one shared misunderstanding cannot satisfy both sides. |
| `FamilyHQ.Smoke.Steps/SmokeScenarioDays.cs` | A day of its own per scenario — see [why no scenario shares a day](#why-no-scenario-shares-a-day). |

### Why no scenario shares a day

Every scenario puts its events on a day of its own, handed to it as `state.EventDay` by
`SmokeScenarioDays` — a plain counter, allocated in the scenario hook, starting tomorrow.

It looks like over-engineering and it is not. The suite's events are **kept** (principle 5), so a day
accumulates every event every run has ever put there. While the suite was small and ran once per preprod
deploy, one shared day held three or four tiles and nothing went wrong. Past that, the kiosk's day view lays
overlapping tiles over one another, Playwright finds the tile a scenario wants, another scenario's tile
intercepts the click, and thirty seconds later the scenario fails with a locator timeout that reads exactly
like a product fault. It surfaced while this coverage was being written, as two *core* scenarios failing after
the suite had been run several times in one afternoon — nothing to do with the code under test.

A counter rather than a hash of the scenario's id, because a hash collides: with a dozen scenarios over even a
couple of months of days, two landing on the same day is more likely than not. The cost of a counter is that a
scenario run on its own sits on a different day than it would in a full run, which is harmless — nothing about
a scenario depends on its date, and its events are always found by the correlation id and the short title id.

Two rules follow:

- **Never compute a date in a step.** Take `state.EventDay`, or a date derived from it (the first Wednesday on
  or after it, the day before the next daylight-saving change). `SmokeEventShape` deliberately offers no day
  at all.
- **Never assume a day is empty.** It holds this run's events and nothing else, which is enough; it is not a
  clean slate and the retained events from earlier runs are the point.

### `data-testid` attributes this suite relies on

FHQ-141 added these to the kiosk so the suite addresses controls by identity rather than by user-facing
copy. **They are part of this change**, so the kiosk scenarios cannot pass against a preprod that
predates this branch:

`event-title-input`, `event-location-input`, `event-description-input`, `event-delete-btn`,
`weather-strip`, `weather-strip-current`, `weather-strip-temp`, `weather-strip-condition`,
`event-capsule`, `day-event-block`.

Everything else it uses (`add-event-btn`, `event-save-btn`, `day-tab`, `day-picker-*`,
`event-modal-tab-*`, `all-day-toggle`, `recurrence-*`, `recurrence-scope-*`) already existed for E2E. The
full-coverage pass added no new ones: the all-day toggle, the recurrence interval stepper, the frequency
pills and the three scope pills were all already addressable.

---

## Preflight: what each check proves and what each failure means

Preflight runs **once per run**, before the first scenario, and its result gates everything else. Each
check reports independently as its own scenario in `Preflight.feature`, so a red run names the specific
thing that is wrong. A failing check never stops the others — whoever has to fix the environment wants
the whole list.

| Check | What it proves | What a failure means |
|---|---|---|
| **FamilyHQ session token** | `POST /api/auth/issue-token` returns a JWT, so the kiosk can start signed in. | A 404 is deliberately ambiguous between four causes: the endpoint is disabled (`Auth__IssueTokenEndpoint__Enabled`), the deployment reports a production tier, `Smoke__IssueTokenSecret` does not match, or `Smoke__UserId` names an account with no stored Google connection. Check the flag and the secret first. A 400 means `Smoke__UserId` is empty — and confirms the secret *was* accepted. |
| **Google access token** | `POST /api/auth/issue-google-access-token` refreshes the stored grant, so the suite has an oracle credential. | 409 `reauth_required` means the grant is revoked — see [re-signing in](#re-signing-in-when-the-google-grant-is-revoked). No amount of retrying will fix it. 404 means the endpoint is unavailable or the account never connected Google. |
| **Google calendar list** | That token actually works against the Calendar API. | preprod minted a token Google will not accept — almost always the granted scopes no longer cover the Calendar API. Re-consent. |
| **saved location** | `GET /api/settings/location` equals `Smoke__ExpectedLocation`. Without it the weather widget has nothing to render and E1 means nothing. | Either the environment was re-pointed or the configured expectation is stale. Decide which, then change that one. The suite will not save it — saving a location also geocodes it, and Nominatim is out of scope. |
| **family time zone** | `GET /api/settings/timezone`'s *effective* zone equals `Smoke__ExpectedTimeZone`. | Every wall-clock and recurrence assertion is anchored to this zone, and it must be the zone preprod will actually stamp on an outbound write (FHQ-170). Fix whichever of the two is wrong. |
| **test calendars** | The expected calendars exist on both sides, and **exactly one** is flagged shared — the configured one. | Missing from preprod but present in Google: preprod has not adopted the Google-side name yet (FHQ-211 adopts it on every sync — check the environment carries that fix). No shared calendar: multi-member events have nowhere to go; flag it on the kiosk's calendar settings page. More than one: there must be exactly one answer to "the shared calendar". |
| **webhook registrations** | Every push-capable calendar has an **unexpired** Google push channel, per `GET /api/diagnostics/webhook-registrations`. | Without a live channel nothing made on a phone reaches the kiosk, so the Google-to-kiosk scenarios would fail for an environment reason and look like a FamilyHQ bug. Re-register with `POST /api/sync/register-webhooks` on preprod and check `Sync:WebhookBaseUrl` still points at RelayRobin. A 404 from the diagnostics endpoint itself means the environment predates FHQ-141. |

`GET /api/diagnostics/webhook-registrations` was added by FHQ-141 for this check. Google publishes no
way to list push channels, so FamilyHQ's own registrations are the only available answer. It is
read-only, scoped to the caller's calendars, and carries neither the channel id nor the channel token —
the token is the credential that authorises posting a notification. Expired rows are **returned, not
filtered**, because "expired last Tuesday" and "never registered" are different faults with different
fixes.

---

## Scenarios: what each one proves

Verification style throughout: compare the **full set** through preprod's API against Google, and
spot-check in the UI. Do not reproduce E2E's UI coverage here.

### Open-Meteo — `Weather.feature`

| ID | Scenario | What it proves | What a failure means |
|---|---|---|---|
| **E1** | The dashboard shows live current conditions for the saved location | The whole weather chain: saved location → Open-Meteo → ingest → API → kiosk. The strip renders only when preprod holds a current reading, and it only holds one because it asked Open-Meteo for the coordinates behind the saved place name. Also that the kiosk booted clean — a Blazor WASM app that throws on start still paints a dashboard, so "it looks fine" is not evidence. | No strip: Open-Meteo is unreachable, the reading has aged out of its window, or weather is disabled. Check `GET /api/weather/current` (204 means no data) and `GET /api/settings/weather`. A console error: something in the kiosk threw, whatever the page looks like. |

### Kiosk → Google — `KioskToGoogle.feature`

| ID | Scenario | What it proves | What a failure means |
|---|---|---|---|
| **KG1** | A two-member event is written once, to the shared calendar | The calendar model's outbound half: a multi-member event goes **once**, to the shared container, carrying `[members: A, B]`. All push-capable calendars are searched, not just the shared one. | More than one event: it was also written to a member calendar, and the family sees it twice in the Google Calendar app. None: the kiosk write never reached Google. Wrong calendar: placement is broken. Missing or wrong tag: the Google side can no longer tell who the event belongs to. |
| **KG1b** | A single-member event is written to that member's calendar only | The other half: a single-member event belongs on that member's own calendar, and **not** in the shared container. | An event in the shared calendar as well is the duplicate the family sees twice. |
| **KG3** | Editing only the title leaves every other field in Google untouched | **The golden rule.** The event is created *in Google* first, the way a phone does — free-text description, location, `colorId`, an explicit reminder — because a change correct for events FamilyHQ created can be wrong for ones it merely synced, and the synced ones are the majority. The kiosk then changes the title and nothing else. | Description gone: FamilyHQ overwrote the user's words instead of writing alongside them. `colorId` gone: FamilyHQ sent a whole event resource that omitted a field it has no opinion about — the purest form of the bug. Reminders changed: FHQ-189/205 territory; the family gets alerted at a different time than they set. Start/end changed: the write re-anchored an event nobody asked it to move. |
| **KG4** | Deleting an event on the kiosk removes it from Google | The delete actually propagates. | An event still in Google after a successful local delete is the worst shape of this bug: it comes back on the next sync. |

KG3's description assertion checks that the original free text **survives**, not that the description
is byte-identical. FamilyHQ appends its managed `[members: …]` tag on every write; that is intended and
visible to the user as a tag. Replacing the user's words is the failure.

### Google → kiosk — `GoogleToKiosk.feature`

Both scenarios wait for the **live** push: Google → RelayRobin → preprod's `/api/sync/webhook` → the
sync queue → the API. Nothing triggers a sync by hand, because a scenario that did would pass on an
environment whose push path is completely dead.

| ID | Scenario | What it proves | What a failure means |
|---|---|---|---|
| **GK2** | A shared-calendar event naming two members appears for both of them | The inbound half of the calendar model: member names matched as whole words anywhere in a free-text description (no `[members:]` tag is used here on purpose — the tag is the easy path, and real phone-made events do not carry one). Plus one UI spot-check. | An extra member: the matcher is too greedy. A missing one: too strict. Nothing at all after the wait: the push path did not deliver — preflight already confirmed a live channel, so start with RelayRobin and `Sync:WebhookBaseUrl`. |
| **GK4** | An event deleted in Google disappears from the kiosk | Deletions travel inbound too. | An event the family removed on a phone but that stays on the kiosk is the visible half of a broken inbound sync. |

### All-day events — `AllDayEvents.feature`

Google describes an all-day event with a pair of calendar **dates**, and its end date is the day *after* the
last day the event covers. Nothing else in the suite exercises that convention, and it is the one shape that
carries no time zone at all.

| ID | Scenario | What it proves | What a failure means |
|---|---|---|---|
| **AD1** | A one-day all-day event created on the kiosk is dated the way Google dates one | The kiosk writes `start.date` = the day, `end.date` = the following day, and **no** `dateTime` and no `timeZone` on either boundary. | An end date equal to the start describes an event that is over before it begins and Google rejects it. An end date a day later gives the family a two-day event. A `timeZone` on an all-day event is FamilyHQ asserting something about the event the family never stated. |
| **AD2** | A one-day all-day event created in Google covers that day alone on the kiosk | The exclusive end date survives as an exclusive end: preprod serves `IsAllDay`, starts on the day Google named and ends at the *next* day's boundary — and the kiosk draws it on that day and **not** on the next. | A tile on the following day is the exclusive end read as inclusive: every all-day event runs a day longer than the phone says, and the write-back then tells Google the same thing. |

### Recurrence — `Recurrence.feature`

| ID | Scenario | What it proves | What a failure means |
|---|---|---|---|
| **RK1** | A bounded weekly series created on the kiosk matches Google's expansion | Google holds **one** master with exactly one `RRULE`, `FREQ=WEEKLY`, `COUNT=3` and no `UNTIL`; `start.timeZone` is the family's zone; and the occurrence set preprod serves is exactly Google's `events.instances`. | Two masters: the series was written twice. No `recurrence` array: the rule was lost and it went out as a single event. Wrong `start.timeZone`: every future occurrence is re-anchored, which is FHQ-170 — the damage shows up at the next DST transition, not today. A different occurrence set: preprod and Google disagree about what the rule means. |
| **RG1** | A bounded two-weekday series created in Google shows exactly its instances | A `BYDAY=TU,TH;COUNT=3` series made in Google expands on the kiosk to exactly Google's instances, at the same **wall-clock** time, marked recurring. | Same instants but a different displayed hour means the phone and the wall show the same event at different times. Missing recurrence glyph: an occurrence the family cannot tell is part of a series is one they will edit expecting to change only that day. |

### The shape of a series the kiosk writes — `RecurrenceShapes.feature`

| ID | Scenario | What it proves | What a failure means |
|---|---|---|---|
| **RK2** | A fortnightly series carries both its interval and its chosen weekday to Google | The rule that arrives carries `INTERVAL=2` **and** `BYDAY` naming the weekday the user chose, which is deliberately *not* the start date's weekday. | A dropped interval turns a fortnightly series weekly, and the family finds an event on a week they had free. A missing `BYDAY` lets Google derive the weekday from the start date — which agrees with the choice right up until the two differ. |
| **RK3** | A yearly all-day series falls on the same date each year | `FREQ=YEARLY` with a count, dates rather than times, and Google's two occurrences on the same month and day in consecutive years. | A second occurrence that has slipped a day is an occurrence derived by adding a fixed number of days across a leap year. A `dateTime` means the all-day nature was lost on the way out. |
| **RK4** | A two-member series is written once, to the shared calendar, and belongs to both members | Multi-member placement applied to a *series*: one master in the shared container with the `[members: …]` tag on it, and every occurrence preprod serves belonging to both. | A copy on each member's calendar is two series the family edits twice and sees twice. An occurrence belonging to one member only is one the other never sees. |
| **RK12** | Switching the repeat off collapses the series to a single event | The recurrence-off toggle actually clears the rule on Google, and leaves the event it started from where it was. | A rule left behind keeps expanding on the family's calendar and the series reappears on the next sync. A moved start means clearing the rule also relocated the survivor. |

### Editing a series from the kiosk, at each scope — `RecurrenceEditScope.feature`

The three scopes are three different writes. `ThisOnly` patches the instance into an exception, `ThisAndFollowing`
creates a forward series and *then* truncates the original, `AllInSeries` patches the master. Each scenario
creates its own bounded series in the Background and then applies one of them to the **middle** occurrence, so
"only this one", "everything from here" and "everything" are three visibly different outcomes.

| ID | Scenario | What it proves | What a failure means |
|---|---|---|---|
| **RK5** | Changing one occurrence leaves the master and the other occurrences alone | Google holds exactly one exception, carrying the new title, linked to the master by `recurringEventId` and recording the slot it replaces in `originalStartTime`; the master keeps its own title and its rule. | No `originalStartTime`: the series expands its own occurrence in that slot too and the family sees both. A master that took the new title renamed every other occurrence — the outcome the user explicitly did not choose. |
| **RK6** | Changing this and following ends the original series and starts a replacement | Two masters: the original bounded by `UNTIL` (and no `COUNT` — two end conditions on one rule is not a rule), and a replacement starting exactly at the split occurrence with the new title. The union of their instances is the *same* set of instants as before the split. | The original gone: the occurrences before the split went with it, and those are the family's history. An untruncated original: the two series overlap and every later occurrence shows twice. Moved instants: the calendar was re-timed by an edit that asked for a rename. |
| **RK7** | Changing all events over an occurrence that was already singled out | Ordering matters, and the messages say which half went wrong: the single-occurrence edit must produce an exception **first** (a failure there is reported as the precondition, not as the behaviour under test), and the whole-series rename must then leave that exception standing as *one* exception still pinned to the slot it replaces, leave the series' dates alone, and leave the kiosk showing exactly what Google holds for each occurrence. See [what Google actually does to an override](#what-google-actually-does-to-an-occurrence-override) — it does **not** preserve the title, so the scenario does not ask FamilyHQ to. | An exception deleted takes the family's one deliberately different occurrence with it. Two exceptions means the slot is now filled twice and they see both. Moved dates mean a rename re-anchored the series. A kiosk out of step with Google's titles means the phone and the wall disagree. |

### A phone-made series edited on the kiosk — `RecurrenceGoldenRule.feature`

| ID | Scenario | What it proves | What a failure means |
|---|---|---|---|
| **RK11** | Renaming a series made in another time zone leaves its zone, its rule and its other fields alone | **The golden rule, applied to a series.** The series is created in Google anchored to `America/New_York` — deliberately not the family's zone, and asserted to differ from it, so a substitution cannot hide behind a coincidentally equal offset. A title-only edit at "all events" must leave `start.timeZone`, `end.timeZone`, the `RRULE`, the free-text description, the location, the `colorId`, the boundaries and the reminders exactly as Google held them. | A zone replaced by the family's configured one re-anchors every future occurrence: the series still shows the right time until the two zones' daylight-saving schedules diverge, and then it moves — on the phone, months from now, with nothing to connect it to the edit. A rewritten rule moves which days the series falls on. |

### Deleting from a series on the kiosk, at each scope — `RecurrenceDeleteScope.feature`

| ID | Scenario | What it proves | What a failure means |
|---|---|---|---|
| **RK8** | Deleting one occurrence cancels that occurrence alone | Google stops expanding the cancelled slot and expands every other occurrence unchanged. | More than one occurrence gone is a delete that took days the family never offered up — and a delete has nothing to undo it. |
| **RK9** | Deleting this and following ends the series before that occurrence | The master's rule gains an `UNTIL` before the split, and exactly the occurrences before it survive. | A rule left as it was means nothing was removed on Google's side and the occurrences come back on the next sync. |
| **RK10** | Deleting all events removes the series from Google | Nothing carrying the scenario's marker is left in Google, and preprod serves nothing. | A master left behind keeps expanding, and the family sees the series they deleted reappear. |

### Series made and unmade in Google — `RecurrenceFromGoogle.feature`

| ID | Scenario | What it proves | What a failure means |
|---|---|---|---|
| **RG2** | A series Google bounds by an end date stops exactly where Google stops it | The **other** of Google's two bounds. `UNTIL` is inclusive and stated in UTC, and the rule is written so the end date lands exactly on the last wanted occurrence's start — the boundary an exclusive reading drops. | Stopping a week early drops an occurrence the family put in the calendar; running a week late invents one Google never expanded. Only the boundary shows the fault, which is why nothing else would. |
| **RG3** | A yearly all-day series created in Google lands on the same date each year | The inbound half of the yearly shape: two occurrences, same month and day, consecutive years, expanded as dates. | A slipped date is a fixed-days calculation across a leap year. A `dateTime` means the all-day nature was lost inbound. |
| **RG8** | Changing the members named on a Google series changes who every occurrence belongs to | A membership change made on a phone reaches **every** occurrence of the series (two members before, a different two after — the count stays at two, so this is a membership change and not also a move between the shared container and a member calendar). | An occurrence left behind is one a member still sees that is no longer theirs, and one the new member never sees. |
| **RG9** | A series deleted in Google disappears from the kiosk | Deletion of a whole series travels inbound. | Occurrences of a deleted series still on the kiosk are the visible half of a broken inbound sync. |

### A series edited in Google, the way the phone app edits one — `RecurrenceEditedInGoogle.feature`

The Google Calendar app has no "scope": it performs each choice as a different edit to the data. These
scenarios make those edits directly, with the oracle credential, and then ask whether the kiosk shows what
Google now holds. Expectations are derived from Google's expansion in every case, so the scenario is correct
whatever Google's rules turn out to be — what is asserted is that FamilyHQ agrees with it.

| ID | Scenario | What it proves | What a failure means |
|---|---|---|---|
| **RG4** | One occurrence moved and renamed in Google changes on the kiosk and nothing else does | Google holds one exception, at its new time, recording the slot it replaces; preprod serves the whole set instant for instant and **title by title**. | A kiosk that shows the old time for the edited occurrence, or the new title on its neighbours, has read an exception as a change to the series. |
| **RG5** | One occurrence deleted in Google disappears from the kiosk and the others stay | The cancelled slot is gone from Google's expansion *and* from what preprod serves, and the others are untouched. | A single occurrence the family removed on a phone that stays on the kiosk is the shape of a broken inbound sync most easily mistaken for the series being intact. |
| **RG6** | A series split in Google at one occurrence shows as both halves on the kiosk | The phone-app split: the original truncated with `UNTIL`, a second series inserted from the split point, and the kiosk showing the union of the two expansions with the right title on each half. | Showing only one half means the kiosk followed the truncation and missed the replacement, or vice versa — and the family loses or duplicates part of the series. |
| **RG7** | Renaming a Google series reaches the occurrence that had already been singled out | That the exception survives the master rename as one exception on its original slot, and that whatever titles Google now holds are the titles the kiosk shows. Ordering is enforced: singling one out has to have worked before the rename, and a failure there is reported as the precondition. | A kiosk still showing a per-occurrence title Google has overwritten is a stale occurrence the family sees one way on the phone and another way on the wall. An exception that lost its original slot is expanded twice. |

#### What Google actually does to an occurrence override

Both RK7 and RG7 were written on the assumption — the reasonable one, and the one the ticket carried — that
Google leaves an already-singled-out occurrence alone when the master is changed, the way its UI implies.
**It does not.** Patching a master's `summary` through the API overwrites the `summary` an exception was
carrying; the exception survives as an exception, on its original slot, but with the series' new title.

This was established the only way it can be: by making that exact patch against real Google with the oracle
credential and reading the result back, seconds later, before FamilyHQ could have touched anything (RG7's own
run). It is not a FamilyHQ behaviour and it is not something the Simulator would ever have shown.

The consequence for this suite is the point of the prime directive. The kiosk's "all events" edit overwrites
the override too — and that is FamilyHQ being **compatible**, not FamilyHQ being wrong. A scenario demanding
the override survive would have been demanding that FamilyHQ diverge from the system of record. So both
scenarios assert the *structure* Google leaves behind (one exception, still on its slot) and defer the titles
to the title-by-title comparison against Google's own expansion, which is correct whatever Google decides.

If a future run goes red here because Google has changed its mind, that is this suite doing its job: re-read
this section, re-establish what Google does now, and move the assertion — do not relax it.

### A series that spans the next daylight-saving change — `DaylightSaving.feature`

| ID | Scenario | What it proves | What a failure means |
|---|---|---|---|
| **RD1** | A series created on the kiosk keeps its wall-clock time across the change | A **daily** series of three — the day before the next transition, the day of it, the day after — written by the kiosk, expands in Google to three occurrences at one unchanged wall-clock time and at **two different UTC offsets**, and preprod serves exactly those instants. | An occurrence an hour out is a series stepped forward in fixed units instead of in its own zone. A skipped or repeated date is a daily rule that lost a day at the transition. |
| **RD2** | A series created in Google keeps its wall-clock time across the change | The same, inbound. | The same, seen from the other direction: the phone and the wall would show the same event at different times. |

**How the transition is found, and why it is not a date in the source.** `FamilyClock.NextOffsetChangeDate()`
asks the family's own zone when it next changes its UTC offset, sampling midday on each candidate date from
tomorrow onwards and stopping at the first day whose offset differs. The series is then placed at
`transition − 1 day` with a **daily** rule and three occurrences.

Three things follow, and all three are deliberate:

- **A date written into the source would rot silently.** Once it passed, the scenario would keep passing while
  testing an ordinary three-day series. Coverage that has quietly stopped existing is worse than none, because
  it still looks like coverage.
- **Daily, not weekly.** Three consecutive days is the shortest bounded series that can straddle a transition.
  A weekly series would have to start a week before it, which is not always in the future, and would place the
  whole scenario further out for no extra coverage.
- **The straddle is asserted, not assumed.** The scenario requires the three occurrences to sit at **more than
  one** UTC offset, and requires their dates to be exactly the day before, the day of and the day after the
  discovered date. If the placement ever stops spanning a change — the zone's rules change, the arithmetic
  drifts — the scenario fails and says so instead of passing vacuously. A zone with no transition at all in the
  coming year is reported as such rather than searched for for ever.

---

## Re-signing in when the Google grant is revoked

Preflight's **Google access token** check failing with 409 `reauth_required` means Google no longer
honours the stored refresh token. Causes: the account owner revoked FamilyHQ's access, the OAuth client
changed, the app is still in testing mode and its refresh tokens expired, or the password changed.

No retry, redeploy or configuration change fixes it. Someone has to sign in:

1. Open `https://preprod.familyhq.alphaepsilon.co.uk:8400` in a browser on the LAN.
2. Sign out if a stale session is showing, then **Login to Google**.
3. Sign in as the **smoke** Google account — not a family account. Its credentials are in the
   preprod environment's credential store (`Google__Username` / `Google__Password`); they are not in
   this repository and must never be written into one.
4. Grant the calendar scopes when asked. If the consent screen does not appear, revoke FamilyHQ's
   access in the account's Google security settings first, so consent is asked for again.
5. Confirm the kiosk shows the calendar rather than a re-auth banner.
6. Re-run preflight only:
   ```bash
   dotnet test tests-smoke/FamilyHQ.Smoke.Features/FamilyHQ.Smoke.Features.csproj \
     --filter "FullyQualifiedName~PreprodEnvironmentHealth"
   ```

The preflight failure message names the URL, so the person paged does not have to come and find this
document to know what to do.

Interactive sign-in is deliberately **not** automated. Google's consent UI is bot-detection prone from
CI, which is the whole reason the FHQ-139 token endpoints exist; automating it would be the most
fragile thing in the suite.

---

## Diagnosing a failure

1. **Read the failure message.** Every one of them is written to be actionable on its own. Preflight
   messages say what to change; scenario messages say which link of a chain did not carry the change.
2. **Take the correlation id** from the scenario's first output line
   (`correlation=… short=…`) and search Seq for it. Every call the suite made to preprod carried it as
   `X-Correlation-Id`, and the kiosk's own requests carried it as `X-Session-Correlation-Id`, so one
   value spans both. See [`seq-log-investigation`](../skills/seq-log-investigation/SKILL.md).
3. **Look at the retained events.** The events are still there, on the smoke account's calendars, with
   the short id in the title and the full correlation id in the description. Open the calendar in the
   Google Calendar UI and look at what was actually written. This is the highest-value step and it is
   the reason nothing is cleaned up.
4. **Look at the screenshot** in `TestResults/smoke-artifacts/<scenario>-<shortid>.png`, and at the
   `kiosk console:` lines in the test output. After a pipeline run it is a build artifact of the
   `Smoke: Scenarios` stage — see [where the results land](#where-the-results-land).
5. **Do not re-run and hope.** An intermittent smoke failure is a report about a third party or about
   FamilyHQ's handling of one; that is information, not noise. Record it in
   [`intermittent-issues.md`](intermittent-issues.md) before dismissing anything.

### "Every scenario failed with *Refusing to run*"

That is the health gate, and the environment is the problem, not the suite. The message carries the
whole preflight failure list. Fix the environment; nothing in the suite will.

### "A typed value shows on screen but the app behaves as though it were never entered"

This is the FHQ-141 kiosk-create defect, and it will come back if anyone adds a field.

Playwright's `FillAsync` sets a field's value and raises `input`, but **not** the `change` event that
Blazor's `@bind` and `@onchange` actually listen for — that follows only when the field is blurred.
A flow usually gets away with it, because the next thing it does is click another control and the
click blurs the field just in time. The first preprod run is what it looks like when that sequencing
luck runs out: the start time picker showed `10:00` in its text box over a model still holding
`09:00`, the modal blocked Save on unrelated validation, and the scenario died five seconds later on
an assertion that had nothing to do with the cause.

Two rules follow, both enforced in `SmokeDashboardPage`:

- Type through `CommitFieldAsync`, which fills **and presses Tab**. Never call `FillAsync` directly on
  a bound field.
- Assert against whatever the component renders **from its model**, not against the input you just
  typed into. For the time picker that is the `+`/`-` stepper readouts; for a date input, the value
  read back after the commit.

Set the **start** time before the end, too: the modal's start-time setter preserves the event's
duration by shifting the end by the same delta, so a start set afterwards silently moves an end that
was already right.

### "A recurrence scenario reports the right number of occurrences but the wrong titles"

Almost certainly a comparison that has escaped the bounded wait. A change made in Google very often leaves
the *number* of occurrences alone — a renamed series, a moved occurrence — so a wait that settles on the count
and then compares titles or instants returns the moment it is asked and compares against a kiosk the push has
not reached. It fails against a perfectly healthy environment, and the obvious-looking fix (wait, then look
again) is exactly the compensation [principle 2](#principles-you-must-not-quietly-relax) forbids.

Every part of the comparison belongs **inside** the wait, which is what
`SmokeSeries.AssertPreprodAgreesWithGoogle…Async` does. It then makes one further read-only look after the
deadline purely to turn "still false" into a named difference, and raises the original timeout if that look
somehow agrees.

### "Preflight passes but every kiosk scenario times out on a locator"

Check whether the `data-testid` attributes listed [above](#data-testid-attributes-this-suite-relies-on)
are present in the deployed build. They arrived with FHQ-141, so a preprod that predates this branch
cannot satisfy them.

---

## Adding a scenario

1. **Ask first whether it belongs here.** If the behaviour can be proved against the Simulator, it
   belongs in E2E. This suite is for interactions with third parties that the Simulator does not model.
2. Write the scenario in the relevant `.feature` file. Keep it to five steps or fewer, and describe the
   outcome rather than the clicks.
3. Tag it `@kiosk` if it drives the browser. Feature-level tags count — the hooks read the scenario's
   tags **and** its feature's, because both tags in this suite are declared once at feature level.
4. Reuse `SmokeEventShape` for the event's times, **`state.EventDay`** for its date, and
   `SmokeCorrelation.Title` / `.Description` for its title and description. Never write a title or a
   description without them, and never compute a date of your own — see
   [why no scenario shares a day](#why-no-scenario-shares-a-day).
5. If it creates a series, it is bounded. `SmokeWeeklyRecurrence` gives you no other option.
6. Assert against Google or against preprod's API — never against FamilyHQ's opinion of its own write.
   `SmokeLookup` is the way in.
7. If you need a new expectation about the environment, add it to `SmokeConfiguration` and to
   preflight, and add the key to the [configuration table](#configuration). Do not put a literal in a
   step definition.

---

## Maintenance checklist

### When to update this suite

- **A new kind of third-party interaction.** A new Google API call, a new outbound field, a new
  provider. That is what this suite is for.
- **A change to the Google write path.** Ask whether KG3's and RK11's field lists still cover the fields that
  could be lost. RK11 is the series-shaped version of KG3 and the one that guards the anchor zone.
- **A change in what Google itself does.** The suite encodes observed third-party behaviour in a few places —
  see [what Google actually does to an occurrence override](#what-google-actually-does-to-an-occurrence-override).
  A red run there is a report about Google, not about FamilyHQ.
- **A change to the calendar model** (placement, membership, the `[members:]` tag) — KG1, KG1b and GK2
  encode the current model.
- **A change to a `data-testid`** the suite uses. Keep the list above accurate.
- **The preprod calendar set changes.** Update `Jenkinsfile.deploy-preprod`'s `environment {}` block (and
  your local run command), not a step definition.

### When *not* to

- To make a red run green. A failing smoke scenario is either a real defect or a real environment
  fault; relaxing the assertion removes the only thing that would have caught it.
- To add UI coverage. That is E2E's job.
- To add a retry. See principle 2.

### Deliberately not covered

- **Moving an event between calendars** (a member change that crosses the single-member / multi-member
  boundary). FamilyHQ implements it as create-on-target plus delete-from-source, which changes the Google event
  id and drops every field FamilyHQ does not model. Scenarios for it would be permanently red, and with the
  suite gating releases a permanently red scenario blocks every release. They belong with the fix, as its proof.
- **`MoveEventAsync`** on the Google client: it has no production call sites at all, so there is nothing to
  smoke-test.
- **Stopping a push channel.** It only fires on re-registration or expiry, neither of which a scenario can
  force without waiting days. Covered by the renewal check instead.
- **Interactive Google sign-in** and **geocoding** — see [out of scope](#re-signing-in-when-the-google-grant-is-revoked)
  above and the manual sign-in procedure.

### Related documents

- [`e2e-testing-maintenance.md`](e2e-testing-maintenance.md) — the Simulator-backed suite this one is
  modelled on and deliberately not coupled to.
- [`intermittent-issues.md`](intermittent-issues.md) — read before dismissing any failure as flake.
- [`architecture.md`](architecture.md) — where the sync, webhook and weather paths live.
- `AGENTS.md` — the prime directive this suite exists to defend.
