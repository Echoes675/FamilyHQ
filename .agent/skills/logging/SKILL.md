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
- The family's home location: `LocationSetting.PlaceName`, `Latitude`, `Longitude`. A place name plus coordinates is the home address to within a few metres. `{LocationId}` correlates just as well (FHQ-166).

Log stable identifiers instead: `{UserId}`, Google `sub`, job ids, `{CalendarInfoId}`, `{LocationId}`.
Cross-references: the `security` and `fail-fast-standard` skills.

### A Google calendar id IS an email address
Nothing about the type `string GoogleCalendarId` says so, which is exactly why it went unnoticed until FHQ-166:

- A Google **primary** calendar's id **is the account's email address** — that is how the Calendar API identifies it.
- `CalendarInfo.DisplayName` is the calendar's Google `summary`, which is **also the email address** for a primary calendar (and a family member's name for a member calendar).

So `{CalendarId}`, `{GoogleCalendarId}` and `{CalendarName}` are all PII placeholders. Instead:

1. **Prefer FamilyHQ's own id.** Log `CalendarInfo.Id` as `{CalendarInfoId}`. It correlates with every other `{CalendarInfoId}` in the sync path, it is already in scope at nearly every call site, and it carries nothing personal.
2. **Otherwise redact.** Where the caller genuinely holds only the Google value (`GoogleCalendarClient` has no FamilyHQ calendar row), inject `IPiiRedactor` and log `redactor.Redact(googleCalendarId)`. That yields a stable, non-reversible token, so one calendar can still be followed across log lines in Seq.

#### The redaction salt

Configuration key **`Security:RedactionSalt`** — an environment variable (`Security__RedactionSalt`) in deployed environments, user secrets locally, **never a literal in the repo**. Generate one with:

```
openssl rand -base64 32
```

- **Absent** → the app still boots and still redacts, using a random per-process salt, and logs a Warning at startup saying correlation is degraded to one process. This is a supported degraded mode.
- **Supplied but shorter than 32 characters** → startup fails. A short salt is guessable, which puts a household-sized candidate list straight back in play; accepting it would claim a protection it does not provide, and unlike the absent case nothing would warn anyone.
- **Changing it** re-tokenises everything: log lines written before the change no longer correlate with lines written after it.

**Guard:** `tests/FamilyHQ.Core.Tests/PiiInLogsGuardTests.cs` scans `src/` and fails the build when one of these values is passed to a log call, a `BeginScope`, or an exception constructor. There is deliberately no allow-list — `IPiiRedactor.Redact(…)` is the only escape hatch. It is a lexical tripwire, not a proof: its XML doc lists what a green run does **not** cover (aliasing, plural locals, values reached through an expression, `[LoggerMessage]`), so never read a passing guard as "audited".

#### Why `tools/FamilyHQ.Simulator` is exempt

The Simulator logs calendar ids and event summaries at Information, and it **is** deployed to dev and staging — so the exemption needs stating rather than assuming. It stands because the Simulator is a Google stand-in whose data is entirely synthetic: `DataSeeder` generates its own calendar ids (`simulated_calendar_family…`) and summaries, and it never holds the family's real account address or their events. If that ever stops being true — a Simulator that proxied or replayed real Google data — the exemption dies with it and the guard must be pointed at `tools/` too.

### Exception messages are a log sink too
An address in an exception message reaches Seq via whatever logs the unhandled exception, and can reach the client through `ProblemDetails.Detail` (`DomainExceptionHandler` surfaces `DomainValidationException.Message`). Apply the same rules to `throw new …($"…")` as to a log template.

## Log on failure
- No silent `catch { }`. Every catch / handled-error / fallback path emits at least:
  - `Debug` when the condition is benign/expected (e.g. an optional resource not yet available, graceful-shutdown cancellation), or
  - `Warning`/`Error` for a genuine problem, with enough context to diagnose.
- Blazor WASM (`FamilyHQ.WebUi`) note: `ILogger` there writes to the **browser console**, not Seq. Still worth adding for kiosk diagnostics, but server-side logging is what reaches Seq.

## Per-environment levels & framework noise
- Base `appsettings.json` `Logging:LogLevel` applies to ALL environments; `appsettings.Development.json` overrides dev.
- `ASPNETCORE_ENVIRONMENT`: dev=`Development`, staging=`Staging`, **preprod AND prod both = `Production`** — so a file-based `appsettings.Production.json` cannot distinguish prod from preprod. Per-environment differences are applied via each environment's `docker-compose.<env>.yml` `environment:` overrides.
- EF Core logs every SQL command at `Information`. To control it, set `Microsoft.EntityFrameworkCore.Database.Command` (the SQL-command category) to `Warning`. This is applied in **prod only** via `docker-compose.prod.yml` so lower envs stay verbose.
