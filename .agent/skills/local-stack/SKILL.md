---
name: local-stack
description: Stand up and drive the full FamilyHQ stack locally. Load when a task needs to boot the app (frontend+backend+DB+Simulator), run the E2E suite locally, seed Simulator data, or drive the running UI/API to verify a change before pushing.
---

# local-stack — run and drive FamilyHQ locally

Use `scripts/dev-stack.ps1` to stand the stack up and exercise it.

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
- Selective E2E: `-Filter "Category=dashboard"`, `-Filter "FullyQualifiedName~<Feature>"`, `-Filter "Scenario=<name>"`.
- Ephemeral DB by default; `-KeepData` persists. `-Reuse` attaches to a healthy stack.

## Detail
- Full command reference + troubleshooting: `.agent/docs/local-stack.md`
- UI + HTTP/backdoor driving: `.agent/docs/driving-the-system.md`
