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
7. [Core scenarios: what each one proves](#core-scenarios-what-each-one-proves)
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

### They are non-gating, for now

Both stages are wrapped in `catchError(buildResult: 'UNSTABLE', stageResult: 'FAILURE')`. A smoke
failure therefore:

- turns the **build** yellow (UNSTABLE) and the **stage** red, so it cannot be mistaken for a pass;
- leaves the **deploy** successful — preprod is up and serving whatever was deployed;
- does **not** block promotion. The master release chain to `FamilyHQ-Deploy-Production` runs from
  `post.unstable` as well as `post.success`, precisely so that a non-gating stage cannot become a gate by
  accident. Making smoke an actual promotion gate is **FHQ-143**; it should happen there, on purpose.

Read an UNSTABLE preprod build as: *"preprod deployed fine; something about the real third-party path did
not hold."* That is something to investigate, not a flake to re-run — see
[diagnosing a failure](#diagnosing-a-failure) and, before dismissing anything,
[`intermittent-issues.md`](intermittent-issues.md).

One consequence worth knowing: the smoke stages run before `post`, so a release chain now waits the
length of a smoke run (a few minutes) before the production deploy is triggered.

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

### `data-testid` attributes this suite relies on

FHQ-141 added these to the kiosk so the suite addresses controls by identity rather than by user-facing
copy. **They are part of this change**, so the kiosk scenarios cannot pass against a preprod that
predates this branch:

`event-title-input`, `event-location-input`, `event-description-input`, `event-delete-btn`,
`weather-strip`, `weather-strip-current`, `weather-strip-temp`, `weather-strip-condition`,
`event-capsule`, `day-event-block`.

Everything else it uses (`add-event-btn`, `event-save-btn`, `day-tab`, `day-picker-*`,
`event-modal-tab-*`, `recurrence-*`, `recurrence-scope-*`) already existed for E2E.

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

## Core scenarios: what each one proves

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

### Recurrence — `Recurrence.feature`

| ID | Scenario | What it proves | What a failure means |
|---|---|---|---|
| **RK1** | A bounded weekly series created on the kiosk matches Google's expansion | Google holds **one** master with exactly one `RRULE`, `FREQ=WEEKLY`, `COUNT=3` and no `UNTIL`; `start.timeZone` is the family's zone; and the occurrence set preprod serves is exactly Google's `events.instances`. | Two masters: the series was written twice. No `recurrence` array: the rule was lost and it went out as a single event. Wrong `start.timeZone`: every future occurrence is re-anchored, which is FHQ-170 — the damage shows up at the next DST transition, not today. A different occurrence set: preprod and Google disagree about what the rule means. |
| **RG1** | A bounded two-weekday series created in Google shows exactly its instances | A `BYDAY=TU,TH;COUNT=3` series made in Google expands on the kiosk to exactly Google's instances, at the same **wall-clock** time, marked recurring. | Same instants but a different displayed hour means the phone and the wall show the same event at different times. Missing recurrence glyph: an occurrence the family cannot tell is part of a series is one they will edit expecting to change only that day. |

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

### "Preflight passes but every kiosk scenario times out on a locator"

Check whether the `data-testid` attributes listed [above](#data-testid-attributes-this-suite-relies-on)
are present in the deployed build. They arrived with FHQ-141, so a preprod that predates this branch
cannot satisfy them.

---

## Adding a scenario

1. **Ask first whether it belongs here.** If the behaviour can be proved against the Simulator, it
   belongs in E2E. This suite is for interactions with third parties that the Simulator does not model.
   FHQ-195 tracks the remaining coverage.
2. Write the scenario in the relevant `.feature` file. Keep it to five steps or fewer, and describe the
   outcome rather than the clicks.
3. Tag it `@kiosk` if it drives the browser. Feature-level tags count — the hooks read the scenario's
   tags **and** its feature's, because both tags in this suite are declared once at feature level.
4. Reuse `SmokeEventShape` for the event's date and times, and `SmokeCorrelation.Title` /
   `.Description` for its title and description. Never write a title or description without them.
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
- **A change to the Google write path.** Ask whether KG3's field list still covers the fields that
  could be lost.
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

### Related documents

- [`e2e-testing-maintenance.md`](e2e-testing-maintenance.md) — the Simulator-backed suite this one is
  modelled on and deliberately not coupled to.
- [`intermittent-issues.md`](intermittent-issues.md) — read before dismissing any failure as flake.
- [`architecture.md`](architecture.md) — where the sync, webhook and weather paths live.
- `AGENTS.md` — the prime directive this suite exists to defend.
