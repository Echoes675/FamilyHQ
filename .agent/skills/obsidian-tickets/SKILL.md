---
name: obsidian-tickets
description: Project-specific skill that defines how Claude reads and writes tickets in the FamilyHQ Obsidian vault. Load whenever working in this repo. Defines trigger → action rules for the ticket lifecycle, override paths for spec/plan output, and MCP usage conventions.
---

# obsidian-tickets — FamilyHQ ticket workflow

The vault at `D:\Obsidian Vault\FamilyHQ` is the single source of truth for FamilyHQ tickets, specs, and plans. This skill tells Claude when and how to interact with it.

## Vault layout

```
D:\Obsidian Vault\FamilyHQ\
├── README.md
├── _Templates/        (8 templates: Idea, Feature, Bug, Investigation, Chore, Subtask, Spec, Plan)
├── _Dashboards/       (5 dashboards: Inbox, Backlog, Active, Done, All Tickets)
├── Tickets/
│   └── FHQ-N/
│       ├── FHQ-N.md
│       ├── FHQ-N-spec.md       (when In Progress)
│       ├── FHQ-N-plan.md       (when In Progress)
│       └── FHQ-N.X.md          (subtasks, flat files)
└── Archive/
```

## ID scheme

- Top-level: `FHQ-N` (monotonic, never reused). Counter: scan `Tickets/FHQ-*/` directories, take max N → next is N+1.
- Subtasks: `FHQ-P.S`. Counter: scan `Tickets/FHQ-P/FHQ-P.*.md` for max S → next is S+1.

## Lifecycle states

**Top-level:** `Inbox → Ready → In Progress → In Review → Done` (terminal) | `Cancelled` (terminal)

**Subtasks:** `Ready → In Progress → In Review → Done` (terminal) | `Cancelled` (terminal) | `Promoted` (terminal — became a top-level ticket). Subtasks have no branch, no PR. "Done" means local sign-off (review agent finds no Blocker/Major, tests green).

## Trigger → action rules

| # | Trigger | Action |
|---|---|---|
| 1 | User says "add an idea / log a bug / throw in the vault" | Compute next `FHQ-N`. Create `Tickets/FHQ-N/FHQ-N.md` from the appropriate template (Idea/Bug/Feature/etc.), `status: Inbox`. Confirm: "Logged as FHQ-N." |
| 2 | User says "let's flesh out FHQ-N" | Open the ticket. Walk through the structured body sections. Promote `type` if needed (e.g., Idea → Feature). |
| 3 | User says "FHQ-N is ready" or acceptance criteria look complete | Set `status: Ready`, bump `updated`. |
| 4a | About to write a spec via `superpowers:brainstorming` for FHQ-N | Save spec output to `D:\Obsidian Vault\FamilyHQ\Tickets\FHQ-N\FHQ-N-spec.md` (overrides skill default `docs/superpowers/specs/...`). Status remains `Ready`. |
| 4b | About to invoke `superpowers:writing-plans` for FHQ-N | Set `status: In Progress`, bump `updated`. Save plan output to `D:\Obsidian Vault\FamilyHQ\Tickets\FHQ-N\FHQ-N-plan.md` (overrides skill default `docs/superpowers/plans/...`). |
| 5 | After plan completes with M implementation steps | Auto-create M subtasks `FHQ-N.1` through `FHQ-N.M`, each from the Subtask template with `parent: FHQ-N`, `plan_step: <i>`, `status: Ready`. Each subtask body must include `Parent: [[FHQ-N]]` directly under the H1 heading. |
| 6 | I create a feature branch (`git checkout -b feat/FHQ-N-...`) | Set `branch: feat/FHQ-N-...` on the parent ticket, bump `updated`. |
| 7 | I run `gh pr create` and a PR opens successfully | Refuse if any subtask is not in terminal state (`Done` / `Cancelled` / `Promoted`); list which. Otherwise set `pr: <url>`, `status: In Review`, bump `updated`. |
| 8 | Session start, for each ticket with `status: In Review` | Run `gh pr view <num> --json state,mergedAt`. If merged: set `status: Done`, `merged: <date>`, bump `updated`. |
| 9 | User says "I merged FHQ-N" or "close FHQ-N" | Same as #8 on demand. |
| 10 | User says "cancel FHQ-N" | Set `status: Cancelled`. Ask whether to move folder to `Archive/`. |
| 11 | Session start (every session) | One-line summary: "Backlog: X In Progress, Y In Review, Z Ready, W Inbox." Skip silently if vault unreachable. |
| 12 | A review agent (`superpowers:code-reviewer`, `superpowers:requesting-code-review`, ultrareview) returns findings while a subtask is `In Review` | Append findings to subtask's "Review notes" section, severity-tagged (Blocker/Major/Minor/Nit). If any Blocker/Major: keep status `In Review`. Else: prompt to move to `Done`. |
| 13 | About to move ticket to `In Progress` | Check `blocked_by`. Refuse if any blocker is not in terminal state (`Done`/`Cancelled`/`Promoted`); list which. |
| 14 | A blocker becomes `Done` | If exactly one ticket was blocked only by it, surface "FHQ-X is now unblocked." |
| 15 | Subtask should become a top-level ticket | Apply Promotion rule: create new top-level `FHQ-M` (next main counter) from Feature template with `replaces: FHQ-N.S`. Set the subtask `status: Promoted`, `promoted_to: FHQ-M`. Append body line "Promoted to [[FHQ-M]] on YYYY-MM-DD". |

## MCP usage

Use mcp-obsidian tools (already wired up at user scope):

- **Read:** `obsidian_get_file_contents`, `obsidian_list_files_in_vault`, `obsidian_list_files_in_dir`, `obsidian_simple_search`
- **Write:** `obsidian_patch_content` (target a YAML key for frontmatter updates), `obsidian_append_content` (add to body sections)
- **Pattern:** read-modify-write for frontmatter changes — atomic enough for solo use; no locking.

If MCP is unavailable (Obsidian not running), surface it to the user and skip vault writes — never silently drop.

## Failure modes

- **Branch name doesn't match `<feat|fix|chore|spike>/FHQ-N-...`** — don't auto-link; ask which ticket to attach to.
- **Two PRs cite the same FHQ-N** — flag, don't guess.
- **GDrive conflict file `FHQ-N (1).md`** — ignored by ID scanner (strict regex). User reconciles manually.

## Override notes for related skills

- `superpowers:brainstorming` default save path → overridden to `Tickets/FHQ-N/FHQ-N-spec.md`
- `superpowers:writing-plans` default save path → overridden to `Tickets/FHQ-N/FHQ-N-plan.md`
- For non-ticket exploration (rare), defaults still apply (`docs/superpowers/...`).
