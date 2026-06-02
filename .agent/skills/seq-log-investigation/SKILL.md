---
name: seq-log-investigation
description: Read centralised structured logs from the FamilyHQ Seq instances (dev/staging/preprod via the familyhq-omv profile; production via familyhq-prod) with read-only seqcli, for debugging and incident investigation. Load when investigating an error, exception, failing test, Deploy failure, flaky behaviour, or when asked to "check the logs" / correlate a request. Includes one-time local setup for a new machine.
---

# Seq log investigation

FamilyHQ ships structured logs to central [Seq](https://datalust.co/seq) instances. During debugging you can query them **read-only** with `seqcli` instead of asking the operator to copy/paste fragments. This is the log evidence-gathering channel for `superpowers:systematic-debugging` (Phase 1).

> **Two instances, two seqcli profiles — pick the one for the environment you're investigating:**
> - **`familyhq-omv`** — lower environments (dev / staging / preprod), shared Seq on the OMV LAN host (`http://192.168.86.23:8500`).
> - **`familyhq-prod`** — **production**, separate Seq at `https://seq.alphaepsilon.co.uk`.
>
> The two instances never cross the home↔prod boundary. Both profiles use a **read-only** key.

## When to use
- Investigating an error / exception / failing test / Deploy failure.
- "Why did X happen in `<env>`", "check/inspect the logs", reconstructing a request.
- Correlating events across a request via `CorrelationId`.

## How to query (read-only)

`seqcli` is a dotnet global tool. Target a profile (`--profile familyhq-omv` for lower envs, `--profile familyhq-prod` for production) — the API key lives in seqcli's config, never in the command:

```bash
# lower envs — recent events in one environment:
seqcli search --profile familyhq-omv -f "Environment = 'Staging'" -c 20

# follow one request across the system by correlation id:
seqcli search --profile familyhq-omv -f "CorrelationId = '<guid>'" -c 100

# errors only, in dev:
seqcli search --profile familyhq-omv -f "Environment = 'Development' and @Level = 'Error'" -c 50

# PRODUCTION — same commands, the familyhq-prod profile:
seqcli search --profile familyhq-prod -f "@Level = 'Error'" -c 50

# live tail:
seqcli tail --profile familyhq-omv -f "Environment = 'Staging'"
```

For time ranges (`--start` / `--end`) and more options: `seqcli search --help`.

Every event carries: `Environment`, `Application` (`FamilyHQ.WebApi` / `FamilyHQ.Simulator`), `MachineName`, `CorrelationId` / `SessionCorrelationId` (request-scoped), request fields (`Method`, `Path`, `StatusCode`, `ElapsedMs`), and `SourceContext`.

### Environment labels (important — and the "Production" ambiguity)
- On **`familyhq-omv`**: dev → `Development`, staging → `Staging`, **preprod → `Production`** (preprod runs as `ASPNETCORE_ENVIRONMENT=Production`).
- On **`familyhq-prod`**: real production → `Production`.
- ⚠️ Both preprod and real prod label themselves `Environment = 'Production'`. They're disambiguated by **which profile/instance you query**: `familyhq-omv` "Production" = **preprod**; `familyhq-prod` "Production" = **real production**.

### Retention
~14 days on `familyhq-omv` (lower envs); **30 days on `familyhq-prod`** (production). Older events are pruned — don't expect logs beyond the window.

## Guardrails — READ ONLY
- Use only `seqcli search`, `seqcli tail`, `seqcli query` — allowlisted in `.claude/settings.json`.
- **Never** run `seqcli apikey`, `seqcli user`, `seqcli config`, `seqcli ingest`, or any write/admin/delete subcommand. Both profiles' keys are read-only; these are out of bounds.
- Never print or echo an API key. Keys live only in the seqcli profiles, never in commands, output, or the repo.
- Production is real user-facing data — query with care.

## One-time local setup (new machine / missing profile)

If `seqcli` or a profile is missing (`seqcli profile list` doesn't show `familyhq-omv` / `familyhq-prod`), re-establish access:

1. **Install seqcli** (dotnet global tool):
   ```bash
   dotnet tool install --global seqcli
   ```
   Ensure the global-tools dir is on `PATH` — `%USERPROFILE%\.dotnet\tools` (Windows) / `~/.dotnet/tools` (Unix). Re-open the shell if `seqcli` isn't found.
2. **Mint a read-only API key** in each Seq UI you need (Settings → API Keys → Add → **Read** only, no Ingest/Write/Setup/Admin):
   - lower envs: `http://192.168.86.23:8500`
   - production: `https://seq.alphaepsilon.co.uk`
3. **Create the profile(s)** — run in a normal terminal so the key isn't captured in a shared transcript:
   ```bash
   seqcli profile create -n familyhq-omv  -s http://192.168.86.23:8500      -a <OMV_READONLY_KEY>
   seqcli profile create -n familyhq-prod -s https://seq.alphaepsilon.co.uk -a <PROD_READONLY_KEY>
   ```
4. **Verify:** `seqcli profile list` shows both, and `seqcli search --profile familyhq-omv -c 1` (and `--profile familyhq-prod`) returns an event.
5. **Allowlist the read commands** (avoids a permission prompt per query). `.claude/` is **gitignored** here, so `.claude/settings.json` is per-workstation — re-add on a new machine (or via `/permissions`):
   ```json
   { "permissions": { "allow": ["Bash(seqcli search:*)", "Bash(seqcli tail:*)", "Bash(seqcli query:*)"] } }
   ```
   Read sub-commands only.

### Rotating a key
Revoke the old key in that Seq UI → mint a new **read-only** key → re-run `seqcli profile create -n <profile> -s <url> -a <NEW_KEY>` (overwrites). The profile is the only place the key lives on the workstation.
