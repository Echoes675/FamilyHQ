---
name: obsidian-tickets
description: Project-specific skill that defines how Claude reads and writes tickets in the FamilyHQ Obsidian vault. Load whenever working in this repo. Defines trigger → action rules for the ticket lifecycle, override paths for spec/plan output, and MCP usage conventions.
---

# obsidian-tickets — FamilyHQ ticket workflow

The vault at `D:\Obsidian Vault\FamilyHQ` is the single source of truth for FamilyHQ tickets, specs, and plans. This skill tells Claude when and how to interact with it.

## Vault root — the open vault is the parent folder

**Obsidian has `D:\Obsidian Vault` open, not `D:\Obsidian Vault\FamilyHQ`.** The parent holds two
project folders (`FamilyHQ/`, `RelayRobin/`) and one live config at `D:\Obsidian Vault\.obsidian\`.
Each project folder still contains a leftover `.obsidian/` from when it was its own vault; **Obsidian
ignores nested ones**, so anything read or written there has no effect on what renders.

Two consequences that are easy to get wrong:

- **MCP paths are vault-root-relative, so they start with `FamilyHQ/`** — `obsidian_get_file_contents("FamilyHQ/Tickets/FHQ-N/FHQ-N.md")`, not `"Tickets/FHQ-N/FHQ-N.md"`. Filesystem paths in this skill are absolute and unaffected.
- **Dataview folder sources are one level deeper than they read.** Dashboard and epic roll-up queries are written to resolve under either root — `FROM "FamilyHQ/Tickets" OR "Tickets"`. Both names are deliberate: the bare one matches nothing in the combined vault, the prefixed one matches nothing if `FamilyHQ/` is ever opened standalone. Don't collapse them to one. `WHERE file.folder = this.file.folder` is relative and needs no prefix.

Diagnosing "the dashboards render as raw text with the fences visible" means checking
`D:\Obsidian Vault\.obsidian\`, never the nested copies (FHQ-157 — the nested folder was checked,
found healthy, and the real cause went unfound for four days).

## Vault layout

```
D:\Obsidian Vault\FamilyHQ\
├── README.md
├── _Templates/        (9 templates: Idea, Feature, Bug, Investigation, Chore, Epic, Subtask, Spec, Plan)
├── _Dashboards/       (6 dashboards: Inbox, Backlog, Active, Done, All Tickets, Epics)
├── Tickets/
│   └── FHQ-N/                  (active tickets: Inbox / Planning / Ready / In Progress / In Review /
│                                Staging / Ready for release)
│       ├── FHQ-N.md
│       ├── FHQ-N-spec.md       (created when brainstorming for this ticket)
│       ├── FHQ-N-plan.md       (created when planning for this ticket)
│       └── FHQ-N.X.md          (subtasks, flat files)
├── Done/
│   └── FHQ-N/                  (Done tickets — folder moved here when the ticket reaches Done, not
│                                when its PR merges)
└── Archive/
    └── FHQ-N/                  (Cancelled tickets moved here on user confirmation)
```

A top-level ticket lives in exactly one of `Tickets/`, `Done/`, or `Archive/` at any time. Subtasks always live inside their parent's folder regardless of subtask state — when a parent moves, subtasks (and the spec/plan files) come with it as part of the folder. **Epics are the exception:** they live under a top-level `Epics/` folder (active in `Epics/FHQ-E0N/`, closed in `Epics/Done/FHQ-E0N/` or `Epics/Cancelled/FHQ-E0N/`) and never in `Tickets/`.

## ID scheme

- Top-level: `FHQ-N` (monotonic, never reused). Counter: scan `Tickets/FHQ-*/`, `Done/FHQ-*/`, and `Archive/FHQ-*/` directories — take max N across all three → next is N+1. (A ticket may live in any one of these locations depending on its terminal/active state.)
- Subtasks: `FHQ-P.S`. Counter: locate the parent's folder (in `Tickets/`, `Done/`, or `Archive/`) and scan that folder for `FHQ-P.*.md` for max S → next is S+1.
- Epics: `FHQ-E0N` (zero-padded, e.g. `FHQ-E01`). **Separate** counter: scan `Epics/` recursively (incl. `Done/`, `Cancelled/`) for `FHQ-E\d+` → max + 1. The letter keeps epic ids out of the numeric `FHQ-N` scan, so the two sequences never collide.

**Re-scan in the same turn as the write — a counter goes stale within a session.** Sessions in this
repo run across days, and the user creates tickets between turns. A max computed earlier in the
conversation is not safe to reuse, however recently it feels like you ran it. This applies to all
three counters above, the epic sequence included.

The cost of getting it wrong is a destroyed ticket, not a duplicate id: the vault is **not a git
repo** and has no Obsidian trash, so a clobbered file is recoverable only from Obsidian's File
Recovery snapshots (Settings → File recovery) or Google Drive version history — the vault is
Drive-synced, and Drive keeps 30 days for non-Google files.

**The tell:** a create that reports the file was *updated* rather than *created* has landed on an
existing ticket. Stop and inspect what was there before. Confirming that the file you just wrote is
intact, or that no sibling files sit beside it, cannot reveal an overwrite — the previous content is
already gone by then. (FHQ-188, 2026-09-08: an id scanned five days earlier was reused, and this
signal was seen and misread.)

**Title field:** every ticket carries a `title:` frontmatter field — the human title (its H1 text without the `FHQ-N — ` prefix). Set it on create and keep it in sync with the H1; dashboards display it (DQL cannot read the H1, and DataviewJS is disabled). Epic members in particular need `title:` for the epic's member roll-up.

## Lifecycle states

**Top-level:** `Inbox → Planning → Ready → In Progress → In Review → Staging → Ready for release → Done` (terminal) | `Cancelled` (terminal)

| State | Means | Evidence |
|---|---|---|
| Inbox | Logged, awaiting planning and design | — |
| Planning | Design and plans being written | — |
| Ready | Design agreed; plan written where the path needs one | spec/plan files |
| In Progress | Implementation and verification | `branch` |
| In Review | Code complete with its evidence; PR open for the user | `pr` |
| Staging | Merged to `dev`, awaiting a green staging run on the merged code | `merged` |
| Ready for release | `FamilyHQ-Deploy-Staging` green on `dev` | `staged` |
| Done | **Live in production**, or merged with a recorded reason no release applies | `released` |

`Done` means the change reached production. FamilyHQ's release chain runs `master` build → staging →
**preprod** → production, each link triggering the next; preprod is a rung of that chain, not a state
of its own. Frontmatter carries `staged:` (date + the run that proved it) and `released:` (version +
date); `merged:` keeps meaning merged-to-`dev`.

**No backward transitions, anywhere.** A failed staging run leaves the ticket in `Staging` while
fixes go round the dev pipeline and merge again. PR comments leave it in `In Review` while fixes are
pushed. Status records how far the work has reached, not whether one attempt survived.

**`Done` without a release is legitimate** for work no pipeline deploys — a CI-only change, an
operator-run script — and requires a recorded reason on the ticket saying why no release applies.

**Tickets marked `Done` before 2026-09-20 mean "merged", not "released"** — they predate this model
and are deliberately not relabelled.

**Subtasks:** `Ready → In Progress → In Review → Done` (terminal) | `Cancelled` (terminal) | `Promoted` (terminal — became a top-level ticket). Subtasks have no branch, no PR. "Done" means local sign-off (review agent finds no Blocker/Major, tests green) — deliberately **not** the same claim a top-level `Done` makes (live in production).

**Epics:** `Open → Done` (terminal) | `Cancelled` (terminal). No branch/PR, no `In Review`. Members drive their own lifecycle; when *all* members reach a terminal state, prompt to close the epic (moving its folder to `Epics/Done/` or `Epics/Cancelled/`).

## Branch & PR model

FamilyHQ uses `master` (production) / `dev` (integration) / short-lived branches. **One ticket branch
can target `master`: a hotfix cut from `master`, when what is already on `dev` is not safe to ship.**
Everything else targets `dev`, and a `master`-cut hotfix owes a back-merge into `dev` afterwards. The
two paths, the merge-base check that tells them apart, and the back-merge check are in the
`git-workflow` skill.

## Trigger → action rules

| # | Trigger | Action |
|---|---|---|
| 1 | User says "add an idea / log a bug / throw in the vault" | Compute next `FHQ-N`. Create `Tickets/FHQ-N/FHQ-N.md` from the appropriate template (Idea/Bug/Feature/etc.), `status: Inbox`, with `title:` set to the ticket's title. Confirm: "Logged as FHQ-N." |
| 2 | User says "let's flesh out FHQ-N" | Open the ticket. Walk through the structured body sections. Promote `type` if needed (e.g., Idea → Feature). |
| 3 | User says "FHQ-N is ready" OR every acceptance-criteria checkbox in the ticket body is ticked | Set `status: Ready`, bump `updated`. |
| 4a | About to write a spec via `superpowers:brainstorming` for FHQ-N | Set `status: Planning`, bump `updated` (so design work in flight is visible rather than leaving the ticket looking untouched). Save spec output to `D:\Obsidian Vault\FamilyHQ\Tickets\FHQ-N\FHQ-N-spec.md` (overrides skill default `docs/superpowers/specs/...`). `Ready` then means the plans exist — set it when design is agreed. |
| 4b | About to invoke `superpowers:writing-plans` for FHQ-N | Set `status: In Progress`, bump `updated`. Save plan output to `D:\Obsidian Vault\FamilyHQ\Tickets\FHQ-N\FHQ-N-plan.md` (overrides skill default `docs/superpowers/plans/...`). |
| 5 | After plan completes for FHQ-N (the plan file `FHQ-N-plan.md` was just written and FHQ-N is `In Progress`). M = count of numbered top-level tasks in the plan body (lines matching `### Task <N>:`). | Auto-create M subtasks `FHQ-N.1` through `FHQ-N.M`, each from the Subtask template with `parent: FHQ-N`, `plan_step: <i>`, `status: Ready`. Each subtask body must include `Parent: [[FHQ-N]]` directly under the H1 heading. **Idempotent**: if `FHQ-N.1` already exists, skip the rule entirely (no partial creation). |
| 6 | A branch matching `<feat\|fix\|chore\|spike\|hotfix>/FHQ-N-<slug>` is created (by user or agent) | Set `branch: <full branch name>` on the parent ticket, bump `updated`. |
| 7 | `gh pr create` succeeds and a PR is opened (by user or agent) | Refuse if any subtask is not in terminal state (`Done` / `Cancelled` / `Promoted`); list which. Otherwise set `pr: <PR URL>`, `status: In Review`, bump `updated`. |
| 8 | Session start, for each ticket with `status: In Review` (**runs before Rule #11** so the summary reflects today's merges) | Run `gh pr view <pr-stored-value> --json state,mergedAt` (the URL stored in `pr:` works for `gh pr view`). If state is `MERGED`: set `status: Staging`, `merged: <mergedAt date>`, bump `updated`. **Do not move the folder** — the move happens only at `Done`. If state is `CLOSED` and not merged: prompt the user — "FHQ-N's PR was closed without merging; leave at In Review, or Cancel?" — and act on the response. A closed PR never sends the ticket back to `Ready`: `status` tracks how far the work has reached, not whether this particular PR survived. |
| 9 | User says "I merged FHQ-N" | Same as Rule #8's merged path — set `status: Staging`, `merged: <date>`, bump `updated`. Do not move the folder. |
| 9a | Session start, for each ticket with `status: Staging` (runs after Rule #8, so tickets it just moved there are included) | Check whether a `FamilyHQ-Deploy-Staging` run started by the `dev` build of that merge has succeeded since the ticket's `merged` date (`jk run ls FamilyHQ-Deploy-Staging --limit 5`). **Prompt the user with the run number rather than transitioning silently** — one staging run often covers several merged tickets, so it is the user's call. On confirmation: set `status: Ready for release`, `staged: <date the run passed>`, bump `updated`, and note the run number in the ticket body. **Exception — a `master`-cut hotfix:** no `dev` run will ever cover it, so look instead for the staging run the `master` build started with a `SEMVER_TAG`. That run also promotes onward, so such a ticket usually clears 9a and 9b in one session — and it still owes its back-merge PR before it is genuinely finished. |
| 9b | User says a release has gone out (e.g. "I released FHQ-N" / "v1.1.20 is out"), naming which `Ready for release` tickets it carried — **or** records that no release applies to one (a CI-only change, an operator-run script) | For each named ticket: set `status: Done`, `released: <version + date>`, bump `updated`, and **move the ticket folder to `Done/FHQ-N/`** (with all its subtask/spec/plan files, as one unit). Note the release version in the ticket body — or, on the no-release path, the recorded reason no release applies. A release is `Done` only once `FamilyHQ-Deploy-Production` has succeeded; a chain that stopped at staging or preprod leaves the ticket where it is. |
| 10 | User says "cancel FHQ-N" | Set `status: Cancelled`, bump `updated`. Ask whether to move folder to `Archive/FHQ-N/`. On yes, move the ticket folder (with all subtask/spec/plan files) as one unit. |
| 8b | A PR merge is detected by Rule #8 or #9 | Check the `dev` branch build: `jk run ls FamilyHQ/dev --limit 1`. Two independently-green PRs can merge cleanly and still leave `dev` red (FHQ-168 — a semantic merge conflict; it happened on 2026-08-20 and the failing `dev` build sat unread). If red, follow the fix-forward procedure in `ci-gate/SKILL.md`. **Especially important when more than one PR was open at once.** |
| 11 | Session start (every session) — runs **after Rules #8 and #9a** | One-line summary: "Backlog: X In Progress, Y In Review, Z Ready, W Inbox, P Planning, S Staging, F Ready for release." Skip silently if vault unreachable. |
| 12 | A review-agent skill (`superpowers:code-reviewer`, `superpowers:requesting-code-review`, ultrareview, or any subagent that returns severity-tagged findings) reports while a subtask is `In Review`. The Subtask template provides a `## Review notes` section by default, so this section always exists. | Append the agent's findings under `## Review notes`, prefixing each with severity (Blocker/Major/Minor/Nit). If any Blocker/Major remain: keep status `In Review`. Otherwise: prompt the user to move the subtask to `Done`. |
| 13 | About to move ticket to `In Progress` | Check `blocked_by`. Refuse if any blocker is not in terminal state (`Done`/`Cancelled`/`Promoted`); list which. |
| 14 | A blocker becomes `Done` | For each ticket whose `blocked_by` array contained this blocker, recompute remaining open blockers. Surface "FHQ-X is now unblocked." individually for each that has no remaining open blockers (multiple unblockings on a single transition are surfaced as separate lines). When a blocker becomes `Cancelled` or `Promoted` instead, do NOT auto-unblock — those terminal states abandoned/redirected the dependency rather than resolving it. Prompt the user: "FHQ-N's blocker FHQ-X was <Cancelled|Promoted to FHQ-M>; should FHQ-N be unblocked, point at FHQ-M, or stay blocked?" |
| 15 | Subtask should become a top-level ticket | Apply Promotion rule: create new top-level `FHQ-M` (next main counter) from Feature template with `replaces: FHQ-N.S`. Set the subtask `status: Promoted`, `promoted_to: FHQ-M`. Append body line "Promoted to [[FHQ-M]] on YYYY-MM-DD". |
| 16 | User says "create an epic &lt;title&gt;" / "group FHQ-X, FHQ-Y… into an epic" | Compute next `FHQ-E0N` (scan `Epics/`). Create `Epics/FHQ-E0N/FHQ-E0N.md` from the **Epic** template (`status: Open`, `title: <title>`). For each named member set `epic: FHQ-E0N` (and a `title:` from its H1 if absent). Confirm "Created epic FHQ-E0N (N members)." |
| 17 | User says "add/assign FHQ-X to epic FHQ-E0N" | Set `epic: FHQ-E0N` on FHQ-X (and `title:` from its H1 if absent), bump `updated`. Refuse if the target is not `type: Epic`. |
| 18 | All member tickets of epic FHQ-E0N reach a terminal state (`Done`/`Cancelled`/`Promoted`) | Prompt: "All members of FHQ-E0N are terminal — close the epic?" On Done → `status: Done`, move folder to `Epics/Done/FHQ-E0N/`. On Cancel → `status: Cancelled`, move to `Epics/Cancelled/FHQ-E0N/` (create the subfolder on demand). |

## MCP usage

Use mcp-obsidian tools (already wired up at user scope):

- **Read:** `obsidian_get_file_contents`, `obsidian_list_files_in_vault`, `obsidian_list_files_in_dir`, `obsidian_simple_search`
- **Write:** `obsidian_patch_content` (target a YAML key for frontmatter updates), `obsidian_append_content` (add to body sections)
- **Pattern:** read-modify-write for frontmatter changes — atomic enough for solo use; no locking.
- **Paths:** vault-root-relative, so prefixed with `FamilyHQ/` (see *Vault root* above).

If MCP is unavailable (Obsidian not running), surface it to the user and skip vault writes — never silently drop.

## Failure modes

- **Branch name doesn't match `<feat|fix|chore|spike>/FHQ-N-...`** — don't auto-link; ask which ticket to attach to.
- **Two PRs cite the same FHQ-N** — flag, don't guess.
- **GDrive conflict file `FHQ-N (1).md`** — ignored by ID scanner (strict regex). User reconciles manually.

## Override notes for related skills

- `superpowers:brainstorming` default save path → overridden to `Tickets/FHQ-N/FHQ-N-spec.md`
- `superpowers:writing-plans` default save path → overridden to `Tickets/FHQ-N/FHQ-N-plan.md`
- For non-ticket exploration (rare), defaults still apply (`docs/superpowers/...`).
