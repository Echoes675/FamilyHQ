---
name: seq-log-investigation
description: Read centralised structured logs from the lower-environment Seq (dev/staging/preprod) via read-only seqcli, for debugging and incident investigation. Load when investigating an error, exception, failing test, Deploy failure, flaky behaviour, or when asked to "check the logs" / correlate a request. Includes one-time local setup for a new machine.
---

# Seq log investigation (lower environments)

FamilyHQ ships structured logs from **dev / staging / preprod** to a shared [Seq](https://datalust.co/seq) instance on the OMV host. During debugging you can query those logs **read-only** with `seqcli` instead of asking the operator to copy/paste fragments. This is the log evidence-gathering channel for `superpowers:systematic-debugging` (Phase 1).

> **Scope: lower environments only**, via the `familyhq-omv` seqcli profile. Production has a *separate* Seq instance — out of scope here (tracked by FHQ-51).

## When to use
- Investigating an error / exception / failing test / Deploy-Dev or Deploy-Staging failure.
- "Why did X happen in `<env>`", "check/inspect the logs", reconstructing a request.
- Correlating events across a request via `CorrelationId`.

## How to query (read-only)

`seqcli` is a dotnet global tool. Commands target the `familyhq-omv` profile — the API key lives in seqcli's config, never in the command:

```bash
# recent events in one environment:
seqcli search --profile familyhq-omv -f "Environment = 'Staging'" -c 20

# follow one request across the system by correlation id:
seqcli search --profile familyhq-omv -f "CorrelationId = '<guid>'" -c 100

# errors only, in dev:
seqcli search --profile familyhq-omv -f "Environment = 'Development' and @Level = 'Error'" -c 50

# live tail an environment:
seqcli tail --profile familyhq-omv -f "Environment = 'Staging'"
```

For time ranges (`--start` / `--end`) and more options: `seqcli search --help`.

Every event carries: `Environment`, `Application` (`FamilyHQ.WebApi` / `FamilyHQ.Simulator`), `MachineName`, `CorrelationId` / `SessionCorrelationId` (request-scoped), request fields (`Method`, `Path`, `StatusCode`, `ElapsedMs`), and `SourceContext`.

### Environment labels (important)
- dev → `Environment = 'Development'`
- staging → `Environment = 'Staging'`
- **preprod → `Environment = 'Production'`** — preprod runs as `ASPNETCORE_ENVIRONMENT=Production`. Real production logs go to a *separate* Seq, so on this instance **"Production" means preprod**.

### Retention
~14 days (FHQ-33). Older events are pruned — don't expect logs beyond that window.

## Guardrails — READ ONLY
- Use only `seqcli search`, `seqcli tail`, `seqcli query` — these are allowlisted in `.claude/settings.json`.
- **Never** run `seqcli apikey`, `seqcli user`, `seqcli config`, `seqcli ingest`, or any write/admin/delete subcommand. The profile's key is read-only and these are out of bounds.
- Never print or echo the API key. It lives only in the seqcli profile, never in commands, output, or the repo.

## One-time local setup (new machine / missing profile)

If `seqcli` or the `familyhq-omv` profile is missing — e.g. on a fresh workstation, or `seqcli profile list` doesn't show it — re-establish access:

1. **Install seqcli** (dotnet global tool):
   ```bash
   dotnet tool install --global seqcli
   ```
   Ensure the global-tools directory is on `PATH` — `%USERPROFILE%\.dotnet\tools` (Windows) / `~/.dotnet/tools` (Unix). If `seqcli` isn't found after install, re-open the shell or add that directory to `PATH`.
2. **Mint a read-only API key** in the OMV Seq UI (`http://192.168.86.23:8500` → **Settings → API Keys → Add API Key**): grant **Read** permission only — no Ingest / Write / Setup / Admin. Copy the token.
3. **Create the seqcli profile** (run in a normal terminal so the key is never captured in a shared transcript):
   ```bash
   seqcli profile create -n familyhq-omv -s http://192.168.86.23:8500 -a <READONLY_KEY>
   ```
4. **Verify:** `seqcli profile list` shows `familyhq-omv (http://192.168.86.23:8500)`, and `seqcli search --profile familyhq-omv -c 1` returns an event.
5. **Allowlist the read commands** (avoids a permission prompt on every query). `.claude/` is **gitignored** in this repo, so `.claude/settings.json` is per-workstation — re-add it on a new machine (or via `/permissions`):
   ```json
   { "permissions": { "allow": ["Bash(seqcli search:*)", "Bash(seqcli tail:*)", "Bash(seqcli query:*)"] } }
   ```
   Read sub-commands only — leave `apikey`/`user`/`config`/ingest/write prompted.

### Rotating the key
Revoke the old key in the Seq UI → mint a new **read-only** key → re-run `seqcli profile create -n familyhq-omv -s http://192.168.86.23:8500 -a <NEW_KEY>` (overwrites the profile). The profile is the only place the key lives on the workstation.

## Production
Production logs live on a separate Seq instance and are **not** covered by this skill or the `familyhq-omv` profile. Production query access is tracked by FHQ-51 (blocked on the production Seq, FHQ-34).
