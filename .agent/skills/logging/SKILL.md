---
name: logging
description: FamilyHQ logging standard — levels, structured templates, redaction, log-on-failure, and per-environment log levels. Load whenever adding or modifying any log statement, catch block, or logging configuration.
---

# FamilyHQ Logging Standard

## Levels
- **Critical / Error** — a genuine failure that needs attention. An exception that breaks a user-visible operation or a background job.
- **Warning** — unexpected but handled/recoverable; degraded behaviour.
- **Information** — significant state transitions and outcomes (job started/skipped/completed, sync result, account state change).
- **Debug** — diagnostic detail useful when chasing a problem; off in production by default.
- **Expected-and-handled conditions must NOT be logged at Error/Warning.** (See FHQ-56: a re-auth-needed account being skipped is `Information`, not `Error`.)

## Structured templates
- Use named placeholders only: `_logger.LogInformation("Synced {Count} events for {UserId}", count, userId);`
- NEVER string-interpolate or concatenate the message: no `LogInformation($"...{x}...")`.
- Never pass a whole entity/DTO as a single structured property if it could serialise sensitive fields. Log specific, safe fields.

## Redaction (non-negotiable)
Never log, in the message or any structured property:
- Access/refresh tokens, authorization codes, client secrets, API keys, JWT signing keys.
- Full `Authorization` headers or raw OAuth token-endpoint response bodies — parse and log the OAuth `error`/`error_description` codes instead (see `GoogleAuthService.ParseOAuthError`).
- Connection strings.
- PII: email addresses, account display names.
Log stable identifiers instead: `{UserId}`, Google `sub`, job ids.
Cross-references: the `security` and `fail-fast-standard` skills.

## Log on failure
- No silent `catch { }`. Every catch / handled-error / fallback path emits at least:
  - `Debug` when the condition is benign/expected (e.g. an optional resource not yet available, graceful-shutdown cancellation), or
  - `Warning`/`Error` for a genuine problem, with enough context to diagnose.
- Blazor WASM (`FamilyHQ.WebUi`) note: `ILogger` there writes to the **browser console**, not Seq. Still worth adding for kiosk diagnostics, but server-side logging is what reaches Seq.

## Per-environment levels & framework noise
- Base `appsettings.json` `Logging:LogLevel` applies to ALL environments; `appsettings.Development.json` overrides dev.
- `ASPNETCORE_ENVIRONMENT`: dev=`Development`, staging=`Staging`, **preprod AND prod both = `Production`** — so a file-based `appsettings.Production.json` cannot distinguish prod from preprod. Per-environment differences are applied via each environment's `docker-compose.<env>.yml` `environment:` overrides.
- EF Core logs every SQL command at `Information`. To control it, set `Microsoft.EntityFrameworkCore.Database.Command` (the SQL-command category) to `Warning`. This is applied in **prod only** via `docker-compose.prod.yml` so lower envs stay verbose.
