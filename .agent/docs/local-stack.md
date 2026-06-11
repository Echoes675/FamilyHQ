# Local Stack (dev-stack)

`scripts/dev-stack.ps1` stands up the full FamilyHQ stack locally and drives the E2E suite.

## Commands

| Command | What it does |
|---|---|
| `pwsh scripts/dev-stack.ps1 up` | Postgres container + Simulator + WebApi + WebUi, health-gated. |
| `pwsh scripts/dev-stack.ps1 down` | Stop services + remove container (and volume unless `-KeepData`). |
| `pwsh scripts/dev-stack.ps1 status` | Show health of each service. |
| `pwsh scripts/dev-stack.ps1 reset` | `down` then `up` with a fresh DB. |
| `pwsh scripts/dev-stack.ps1 e2e [-Filter <expr>]` | Ensure up, then run E2E (filtered). |

Flags: `-KeepData` (persist DB volume), `-Reuse` (attach if already healthy), `-Force`
(override the unidentified-process guard), `-Headed` (visible E2E browser).

## Ports

- WebUi: https://localhost:7154
- WebApi: https://localhost:7196 (health `/api/health`)
- Simulator: https://localhost:7199 (health `/health`)
- Postgres: localhost:5433 (databases `familyhq`, `familyhq_sim`) — host port 5433 by
  default to avoid clashing with a local Postgres on 5432; override with `POSTGRES_HOST_PORT`

## Selective E2E runs

`-Filter` is passed through to `dotnet test --filter` (the `@ignore` tag is always excluded):

- One scenario: `-Filter "Scenario=<name>"`
- A whole feature: `-Filter "FullyQualifiedName~RecurringEventsEdit"`
- A tag/area: `-Filter "Category=dashboard"`
- Combine: `-Filter "Category=webui&Category!=day-rollover"`

Results are written as `TestResults/e2e.trx` and the process exit code matches `dotnet test`.

## Reconciliation

State is discovered from the listening ports and the `familyhq-dev-db` container name —
not a cached file. `up` stops stale FamilyHQ services on our ports before starting, so a
stack left running by a previous session is reconciled automatically. A port held by a
process that is not provably ours is refused (the PID is named); pass `-Force` to override.

## Credentials

Defaults are `postgres`/`postgres`. To override, copy `scripts/DevStack/.env.example` to
`scripts/DevStack/.env` (git-ignored) and set `POSTGRES_USER` / `POSTGRES_PASSWORD`.

## State & logs

Runtime state and per-service logs live under `scripts/.dev-stack/` (git-ignored):
`logs/<service>.out.log` and `logs/<service>.err.log`, plus `state.json`.

## Troubleshooting

- **"No trusted dev HTTPS certificate"** → run `dotnet dev-certs https --trust`.
- **"Port held by an unidentified process"** → identify the named PID; stop it or re-run with `-Force`.
- **A service didn't become healthy** → check `scripts/.dev-stack/logs/<service>.out.log`.
- **Docker not running** → start Docker Desktop; `up` needs it for the Postgres container.
