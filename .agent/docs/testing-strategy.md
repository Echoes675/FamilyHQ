# Testing strategy — which layer proves what

Read this before adding a test, and before deciding a layer is "the wrong place" for one. It exists
because FamilyHQ has three test layers whose *purposes* are easy to confuse, and choosing the wrong
one costs either a release cycle or a false sense of safety.

## The layers

| Layer | Runs against | Runs on | Proves |
|---|---|---|---|
| **Unit** (`tests/`) | nothing external — mocks only | every branch build | our logic in isolation, deterministically |
| **E2E** (`tests-e2e/`) | the **Simulator**, standing in for Google | every `FamilyHQ-Deploy-Dev` run | the whole app works end to end, cheaply and repeatably |
| **Preprod smoke** (`tests-smoke/`) | the **real** Google account, RelayRobin and Open-Meteo | every preprod deploy | that the behaviour Google actually exhibits matches what we built |

## The rule

> **Every preprod smoke scenario should also exist as an E2E scenario against the Simulator, unless
> the Simulator cannot model the behaviour faithfully.**
>
> The E2E twin catches the defect early and cheaply. The smoke run confirms it against Google.
> Neither replaces the other.

User's ruling, 2026-09-27: *"E2E should contain all the scenarios that are going into the preprod
smoke tests too… we want to find issues early, so finding in the dev pipeline is faster and cheaper
than waiting for a release to fail in preprod."*

The arithmetic is the whole argument. A defect caught in Deploy-Dev costs ten minutes and a re-push.
The same defect caught at preprod costs a release cycle — and once smoke gates promotion
([[FHQ-143]]), it blocks the release outright. [[FHQ-214]] is the worked example: the smoke suite
found it, which is the system working, but it was a plain delete race with nothing Google-specific
about it. An E2E twin would have caught it days earlier, for nothing.

## What belongs in smoke at all

Smoke covers **behaviour whose correctness depends on a third party**: the Google Calendar API in
both directions, Google OAuth and token refresh, push notifications through RelayRobin, Open-Meteo.

Anything kiosk-internal — agenda column ordering, theming, modal layout, view navigation — is **not**
a smoke concern, because no third party decides whether it is right. That belongs to E2E alone.

## When a twin is not possible, and why that matters

**The Simulator is a test double. A green E2E run is not proof of compatibility** — `AGENTS.md` says
so, and this is where that rule bites hardest. Before writing a twin, check whether the Simulator
models the behaviour at all:

- **Time zones and DST** — the Simulator ignores an event's `timeZone` and stamps its own host
  offset on timed writes (FHQ-176). Any zone-anchoring or DST twin is meaningless until that lands.
- **Full-replace write semantics** — the Simulator does not model Google's `PUT` clearing unmapped
  fields, so "editing preserves the fields we do not model" cannot be proven there: a field the
  Simulator never stored cannot be observed to survive.
- **Recurrence expansion** — `NodaTimeRecurrenceTimeZone` is duplicated verbatim in Services and the
  Simulator (FHQ-165), so for expansion the Simulator is **not an independent oracle**. A twin is a
  useful regression net; it is not evidence that we agree with Google.

**A twin that asserts something the Simulator models incorrectly is worse than no twin**, because it
passes while production is wrong — and it will be believed. If you cannot write an honest twin, say
so on the ticket and name the gap, rather than writing a green one.

## Practical consequences

- Adding a smoke scenario? Ask in the same breath whether an E2E twin is possible, and record the
  answer. FHQ-219 tracks the back-fill for scenarios that predate this rule.
- Adding an E2E scenario for third-party behaviour? Consider whether it also needs the smoke
  confirmation — the Simulator agreeing with us proves less than Google agreeing with us.
- Fixing a bug found by smoke? The fix should usually arrive with an E2E twin, so the *next*
  regression of that kind is caught in the dev pipeline instead.
