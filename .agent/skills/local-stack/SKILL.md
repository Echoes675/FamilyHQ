---
name: local-stack
description: Stand up and drive the full FamilyHQ stack locally. Load when a task needs to boot the app (frontend+backend+DB+Simulator), run the E2E suite locally, seed Simulator data, or drive the running UI/API to verify a change before pushing.
---

# local-stack — run and drive FamilyHQ locally

Use `scripts/dev-stack.ps1` to stand the stack up and exercise it.

## Using the tooling (don't hand-roll the environment)

- **`scripts/dev-stack.ps1` is the only supported way to manage the local environment.** Do not manually `docker run`/start containers, `dotnet run` the services, or kill/poll ports yourself — the script reconciles state, health-gates startup, and wires the services together. Manage the env *only* through its `up` / `down` / `reset` / `status` / `e2e` verbs.
- **Invoke it with the PowerShell tool** (it's a `.ps1`). **Do not pipe its output through `tail` / `grep` / `head` from a bash shell** — the output buffers and the run can appear to hang for a very long time. Capture the full output (e.g. `... | Out-String`) and filter it in memory instead. For a long E2E run or a repeat loop, run it in the background rather than truncating its output.
- **Filtering E2E:** prefer `-Filter "FullyQualifiedName~<GeneratedMethodOrClass>"` (the reliable form). The `-Filter "Scenario=<title>"` form may not resolve — Reqnroll does not expose the human scenario title as a test trait, so a title-based filter can match zero tests.
- **E2E runs as timed phases (FHQ-148):** the `e2e` verb logs `PHASE start/ok/TIMEOUT` for `boot → playwright-install → dotnet-test`. If a run stalls, read which `PHASE` last started — that names the stalled stage instead of leaving you with silence. Each phase has a hard timeout (`-InstallTimeoutMinutes`, default 5; `-TestTimeoutMinutes`, default 30) that aborts a hang, sweeps orphaned Playwright browser processes, and returns a non-zero exit code. The **browser install is skipped when Chromium is already cached**; pass `-ForceBrowserInstall` to force a refresh (e.g. after a Playwright version bump, if a run fails to launch a browser).

## Trigger -> action

| Trigger | Action |
|---|---|
| "stand up the stack", "run the app locally", "boot the system" | `pwsh scripts/dev-stack.ps1 up` |
| "run the E2E suite locally", "run E2E for <area>" | `pwsh scripts/dev-stack.ps1 e2e [-Filter "<expr>"]` |
| "tear it down", "clean up the local stack" | `pwsh scripts/dev-stack.ps1 down` |
| "is the stack up?" | `pwsh scripts/dev-stack.ps1 status` |
| "drive the UI" / "seed Simulator data" / "call the API" | See `.agent/docs/driving-the-system.md` |

## Key facts
- Ports: WebUi 7154, WebApi 7196 (`/api/health`), Simulator 7199 (`/health`).
- Selective E2E: `-Filter "FullyQualifiedName~<Feature>"` or `-Filter "Category=dashboard"` (see the filtering note above — prefer `FullyQualifiedName~`).
- Ephemeral DB by default; `-KeepData` persists. `-Reuse` attaches to a healthy stack.

## Detail
- Full command reference + troubleshooting: `.agent/docs/local-stack.md`
- UI + HTTP/backdoor driving: `.agent/docs/driving-the-system.md`
