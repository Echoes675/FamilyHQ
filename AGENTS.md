# FamilyHQ Agent Instructions

Dashboard app that displays a family calendar events

## Prime directive — preserve the Google Calendar experience

**Read this before any design decision. It outranks convenience, tidiness, and internal consistency.**

> **Golden rule:** any change to the makeup of the fundamental entities of the system, or to how
> their values are calculated, **must be fully compatible with how Google Calendar works.**

The context that makes it non-negotiable:

> FamilyHQ must preserve the Google Calendar experience. Most events are created, updated and
> deleted on a user's **mobile device in the Google Calendar app** — not in FamilyHQ. Therefore
> whatever changes we make must be **fully compatible**. The consideration of **existing events** is
> also a real concern: the production application is in use and has many calendars and events.

### What the golden rule covers

**The makeup of the entities** — the fields an event, calendar or recurring series carries, and what
each one means. If Google models something as a field with defined semantics (a series' time zone,
an all-day event's exclusive end date, a recurrence rule, an event's organiser), FamilyHQ's model
must carry it faithfully, not a convenient approximation of it. Dropping a field, or storing
something subtly different under the same name, is a compatibility change even when nothing
visibly breaks.

**How values are calculated** — deriving an occurrence, a boundary, an end date, a count, or a
displayed time must produce what Google produces, for the same input. Where behaviour is observable
— DST handling, recurrence expansion, all-day boundaries, full-replace update semantics — the
standard is **what Google actually does**, not a defensible reading of the spec. Two implementations
that agree in the common case and diverge at a transition are not compatible; they are a latent bug
with a date on it.

### Why this is easy to get wrong

Google is the system of record. **FamilyHQ is one client among several** — a full read-write one:
the touchscreen kiosk creates, edits and deletes events just as the phone app does. It is simply not
the *majority* path, and it does not own the data.

It is natural to reason as though it does, because FamilyHQ's database holds the events and its
settings are close to hand. They are not authoritative. And because FamilyHQ **writes**, that
assumption does not stay theoretical: an edit made on the kiosk can damage an event created on a
phone, and the family sees the damage in the Google Calendar app rather than here.

So compatibility has to hold in both directions:

- what FamilyHQ **writes** must be what the Google app expects to read, and
- an edit must change **what the user asked to change, and nothing else**.

The second is the one that gets missed — and it does **not** mean "avoid touching events created
elsewhere". Editing and deleting phone-created events is a supported, working feature; the kiosk is
a peer client, not a second-class one, and it may validly modify or remove anything on the family's
calendars. The rule is about *incidental* change, not intended change.

The question to ask of a write path is therefore not "may we touch this event?" but:

> **Does this request alter anything the user did not ask us to alter?**

A request that changes one field and silently rewrites another alongside it has broken the golden
rule, however correct the requested change was.

### What this requires in practice

- **Round-trip, don't substitute.** Before changing any Google write path, ask what the value was
  *before* FamilyHQ touched it and whether the change preserves it. Prefer sending back the value
  Google supplied over a locally-derived equivalent that merely looks the same.
- **FamilyHQ settings are a fallback, not an override.** Use them for data Google did not supply
  (a brand-new event's time zone, say). Never use them to replace data it did.
- **A change correct for events FamilyHQ created may be wrong for events it merely synced.** Check
  both origins — the synced ones are the majority.
- **Verify against Google's behaviour, not the Simulator's.** The Simulator is a test double and
  does not model everything Google does; a green E2E run is not proof of compatibility. See
  `.agent/docs/intermittent-issues.md`.

### Existing production data is a first-class concern

Production holds many real calendars and events. For any schema, sync, or write change, state
explicitly **what happens to data that already exists** — "new rows get the new field" is not a
migration plan when the data that matters is already there.

Check whether normal operation actually backfills. It often does not: `CalendarSyncService` fetches
a series master **only when the RRULE is not already cached**, so existing series never re-fetch and
would never populate a newly added column.

## Core Context

- **Framework**: .NET 10 (Blazor WASM Frontend, ASP.NET Core Backend)
- **Database**: PostgreSQL / EF Core
- **Primary Tooling**: dotnet CLI (build, test, run)
- **E2E Acceptance testing**: Read `.agent\docs\e2e-testing-maintenance.md`

## Rules of Engagement (Safety)

- Operations Allowed Without Prompting
-- Read files, list directory contents
-- Type check, lint, format single files
-- Run single unit test
-- Search codebase, read documentation
-- Create git branches and commits
- Operations That Require Approval
-- Installing new packages or dependencies
-- Modifying configuration files (package.json, tsconfig.json, etc.)
-- Running full project build
-- Running full test suite or E2E tests
-- Git push operations
-- Deleting files or directories
-- Modifying database schemas
-- Changing environment variables
-- Making commits directly on the 'dev' or 'main' branches

## Progressive Disclosure Links

Refer to these files in the .agent/ directory for specific implementation details:

- Project Architecture &amp; Structure: `.agent/docs/architecture.md`
- UI Design System (themes, CSS variables, layer model, touch rules): `.agent/docs/ui-design-system.md`
- Intermittent / flaky issues tracker (read before dismissing a CI failure as flake): `.agent/docs/intermittent-issues.md`

## Skills

Read the relevant skill file before starting any task of that type:

- **Git commits**: Read `.agent/skills/git-commit-formatter/SKILL.md`
- **Git workflow (branching, PRs)**: Read `.agent/skills/git-workflow/SKILL.md`
- **Writing/modifying backend C# code**: Read `.agent/skills/dotnet-backend-patterns/SKILL.md`
- **Working with DateTimeOffset and PostgreSQL**: Read `.agent/skills/datetimeoffset-postgresql/SKILL.md`
- **Writing or modifying tests**: Read `.agent/skills/testing-standards/SKILL.md` and `.agent/skills/test-driven-development/SKILL.md`
- **BDD/acceptance tests**: Read `.agent/skills/bdd-testing/SKILL.md`
- **Playwright/browser automation**: Read `.agent/skills/playwright-cli/SKILL.md`
- **Security-sensitive code**: Read `.agent/skills/security/SKILL.md`
- **Any code changes**: Read `.agent/skills/coding-standards/SKILL.md`
- **Error handling or validation**: Read `.agent/skills/fail-fast-standard/SKILL.md`
- **Adding or modifying any log statement, catch block, or logging config**: Read `.agent/skills/logging/SKILL.md`
- **Frontend UI development**: Read `.agent/skills/frontend-design/SKILL.md`
- **Any CSS, component, layout, or page changes**: Read `.agent/skills/ui-theming/SKILL.md` (project-specific theme rules — takes precedence over frontend-design for colour and animation decisions)
- **Pushing a branch and verifying CI (before raising a PR)**: Read `.agent/skills/ci-gate/SKILL.md`
- **Investigating an error / failing test / incident — reading the lower-env logs in Seq**: Read `.agent/skills/seq-log-investigation/SKILL.md`
- **Standing up / driving the full stack locally (boot, run E2E, seed Simulator, verify a change before pushing)**: Read `.agent/skills/local-stack/SKILL.md`
- **Obsidian ticket workflow (any session, any task)**: Read `.agent/skills/obsidian-tickets/SKILL.md`

## Ticket workflow (Obsidian vault)

All FamilyHQ work is tracked in the Obsidian vault at `D:\Obsidian Vault\FamilyHQ`. Two non-negotiables:

- Specs and plans for any `FHQ-N` ticket land in the vault at `D:\Obsidian Vault\FamilyHQ\Tickets\FHQ-N\` — **not** `docs/superpowers/`.
- A session-start scan runs at the start of every session in this repo: list `In Progress` and `In Review` tickets, check `gh pr view` for any merged PRs that should auto-transition to `Done`, and produce a one-line summary.

See `.agent/skills/obsidian-tickets/SKILL.md` for the full trigger → action playbook.

### Skill Registration Rule

When creating a new skill:

1. Create the skill directory and `SKILL.md` file in `.agent/skills/`.
2. Update this "## Skills" section to include the new skill.
3. Ensure the skill follows the standard format with clear triggers and instructions.
4. Skills are automatically discovered at runtime using `list_files(".agent/skills", recursive=true)`.

