using FamilyHQ.Smoke.Common.Configuration;
using FamilyHQ.Smoke.Data.Api;
using FamilyHQ.Smoke.Data.Google;
using FamilyHQ.Smoke.Data.Models;

namespace FamilyHQ.Smoke.Steps.Preflight;

/// <summary>
/// Checks preprod. Repairs nothing.
/// <para>
/// FHQ-141 principle 1: a bad environment followed by a green run is false confidence. So every one of
/// these checks asserts a fact about preprod and, when the fact is wrong, records what to do about it —
/// it never sets the setting, never registers the webhook, never renames the calendar. The environment
/// is somebody's deployment, and a test suite that quietly fixes it destroys the evidence of how it got
/// that way.
/// </para>
/// <para>
/// It runs <b>once per run</b>, and the whole suite is gated on the result: the preflight feature reports
/// each check individually, and every core scenario refuses to start unless all of them passed. That
/// removes any dependence on the order xUnit happens to run the feature files in.
/// </para>
/// <para>
/// A failing check does not stop the remaining ones. Whoever has to fix the environment wants the whole
/// list, not the first item on it.
/// </para>
/// </summary>
public static class SmokePreflight
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static SmokeRunEnvironment? _environment;
    private static Exception? _startupFailure;

    /// <summary>
    /// Runs preflight if it has not run, and returns what it established. Concurrent callers wait for the
    /// first one rather than each starting their own; the checks make real calls against a live account.
    /// </summary>
    public static async Task<SmokeRunEnvironment> EnsureAsync(CancellationToken ct = default)
    {
        if (_environment is not null)
        {
            return _environment;
        }

        await Gate.WaitAsync(ct);
        try
        {
            if (_environment is not null)
            {
                return _environment;
            }

            // A misconfigured suite is not an unhealthy environment, and it is not worth re-discovering once
            // per scenario: the first attempt's exception is kept and re-thrown, so sixteen scenarios report
            // the same single cause instead of making sixteen attempts at it.
            if (_startupFailure is not null)
            {
                throw _startupFailure;
            }

            try
            {
                return _environment = await RunAsync(ct);
            }
            catch (Exception ex)
            {
                _startupFailure = ex;
                throw;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Releases the run-scoped Google client. Called once from an after-test-run hook.</summary>
    public static void Release()
    {
        _environment?.OracleOrNull?.Dispose();
        _environment = null;
        _startupFailure = null;
    }

    private static async Task<SmokeRunEnvironment> RunAsync(CancellationToken ct)
    {
        var configuration = SmokeConfigurationLoader.Load();
        var report = new PreflightReport();
        var correlationId = $"smoke-preflight-{Guid.NewGuid()}";

        RequireConfiguration(configuration);

        using var tokenClient = new SmokeTokenClient(configuration);

        var sessionJwt = await CheckSessionTokenAsync(tokenClient, configuration, report, ct);
        var oracle = await CheckGoogleAccessTokenAsync(tokenClient, configuration, report, ct);
        var googleCalendars = await CheckGoogleCalendarListAsync(oracle, report, ct);

        SmokeCalendarDirectory? directory = null;

        if (sessionJwt is not null)
        {
            using var api = new PreprodApiClient(configuration, sessionJwt, correlationId);
            await CheckSavedLocationAsync(api, configuration, report, ct);
            await CheckFamilyTimeZoneAsync(api, configuration, report, ct);

            var preprodCalendars = await api.GetCalendarsAsync(ct);
            directory = new SmokeCalendarDirectory(preprodCalendars, googleCalendars ?? []);

            CheckTestCalendars(directory, configuration, report);
            await CheckWebhookRegistrationsAsync(api, directory, configuration, report, ct);
        }
        else
        {
            SkipBecauseNoSession(report, PreflightCheckNames.SavedLocation);
            SkipBecauseNoSession(report, PreflightCheckNames.FamilyTimeZone);
            SkipBecauseNoSession(report, PreflightCheckNames.TestCalendars);
            SkipBecauseNoSession(report, PreflightCheckNames.WebhookRegistrations);
        }

        return new SmokeRunEnvironment(
            configuration, correlationId, sessionJwt, oracle, directory, report);
    }

    /// <summary>
    /// Missing configuration is not an unhealthy environment — it is a suite that was pointed at nothing,
    /// and it fails before a single request goes out rather than as seven mysterious 404s.
    /// </summary>
    private static void RequireConfiguration(SmokeConfiguration configuration)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration.BaseUrl)) missing.Add("Smoke__BaseUrl");
        if (string.IsNullOrWhiteSpace(configuration.IssueTokenSecret)) missing.Add("Smoke__IssueTokenSecret");
        if (string.IsNullOrWhiteSpace(configuration.UserId)) missing.Add("Smoke__UserId");
        if (string.IsNullOrWhiteSpace(configuration.ExpectedLocation)) missing.Add("Smoke__ExpectedLocation");
        if (string.IsNullOrWhiteSpace(configuration.ExpectedTimeZone)) missing.Add("Smoke__ExpectedTimeZone");
        if (string.IsNullOrWhiteSpace(configuration.SharedCalendar)) missing.Add("Smoke__SharedCalendar");
        if (configuration.MemberCalendarNames.Count == 0) missing.Add("Smoke__MemberCalendars");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "The preprod smoke suite is not configured. Supply these as environment variables (the "
                + "checked-in appsettings.json carries empty placeholders on purpose, so a secret is never "
                + $"committed): {string.Join(", ", missing)}. See .agent/docs/preprod-smoke-maintenance.md.");
        }
    }

    private static async Task<string?> CheckSessionTokenAsync(
        SmokeTokenClient tokenClient, SmokeConfiguration configuration, PreflightReport report,
        CancellationToken ct)
    {
        var result = await tokenClient.IssueSessionTokenAsync(ct);

        switch (result.Outcome)
        {
            case SmokeTokenOutcome.Issued:
                report.Pass(PreflightCheckNames.SessionToken, "preprod issued a FamilyHQ session token.");
                return result.Token;

            case SmokeTokenOutcome.NotAvailable:
                report.Fail(
                    PreflightCheckNames.SessionToken,
                    $"POST {configuration.BaseUrl}/api/auth/issue-token answered 404. That is one of four "
                    + "things, and all four look identical on purpose: the endpoint is disabled "
                    + "(Auth__IssueTokenEndpoint__Enabled), the deployment reports a production tier, "
                    + "Smoke__IssueTokenSecret does not match the environment's, or Smoke__UserId names an "
                    + "account with no stored Google connection. Check the flag and the secret first.");
                return null;

            case SmokeTokenOutcome.BadRequest:
                report.Fail(
                    PreflightCheckNames.SessionToken,
                    "issue-token answered 400 — the request named no user, so Smoke__UserId is empty. "
                    + "(The shared secret was accepted.)");
                return null;

            default:
                report.Fail(
                    PreflightCheckNames.SessionToken,
                    $"issue-token answered HTTP {result.StatusCode}, which is not a documented response. "
                    + "Check that Smoke__BaseUrl points at preprod and not at something in front of it.");
                return null;
        }
    }

    private static async Task<GoogleCalendarOracle?> CheckGoogleAccessTokenAsync(
        SmokeTokenClient tokenClient, SmokeConfiguration configuration, PreflightReport report,
        CancellationToken ct)
    {
        var result = await tokenClient.IssueGoogleAccessTokenAsync(ct);

        switch (result.Outcome)
        {
            case SmokeTokenOutcome.Issued:
                report.Pass(
                    PreflightCheckNames.GoogleAccessToken,
                    $"preprod refreshed the stored Google grant; the token is valid until "
                    + $"{result.ExpiresAt:u}.");
                return new GoogleCalendarOracle(configuration, result.AccessToken!);

            case SmokeTokenOutcome.ReauthRequired:
                // The one failure a smoke run must report rather than retry, and the reason the endpoint
                // answers 409 instead of 404 (FHQ-139).
                report.Fail(
                    PreflightCheckNames.GoogleAccessToken,
                    "the stored Google grant for the smoke account has been revoked or has expired. "
                    + $"Sign in to preprod again at {configuration.BaseUrl} with the smoke Google account "
                    + "and reconnect Google; no amount of retrying will restore it.");
                return null;

            case SmokeTokenOutcome.NotAvailable:
                report.Fail(
                    PreflightCheckNames.GoogleAccessToken,
                    "issue-google-access-token answered 404 — either the endpoint is not available on this "
                    + "environment, or the account named by Smoke__UserId has never connected Google. "
                    + $"Sign in to preprod at {configuration.BaseUrl} with the smoke account if it has not.");
                return null;

            default:
                report.Fail(
                    PreflightCheckNames.GoogleAccessToken,
                    $"issue-google-access-token answered HTTP {result.StatusCode}, which is not a "
                    + "documented response.");
                return null;
        }
    }

    private static async Task<IReadOnlyList<GoogleCalendarListEntry>?> CheckGoogleCalendarListAsync(
        GoogleCalendarOracle? oracle, PreflightReport report, CancellationToken ct)
    {
        if (oracle is null)
        {
            SkipBecause(
                report, PreflightCheckNames.GoogleCalendarList,
                "there is no Google access token to read it with.");
            return null;
        }

        try
        {
            var calendars = await oracle.ListCalendarsAsync(ct);
            if (calendars.Count == 0)
            {
                report.Fail(
                    PreflightCheckNames.GoogleCalendarList,
                    "Google accepted the token but the smoke account has no calendars at all. Check the "
                    + "account is the intended one.");
                return calendars;
            }

            report.Pass(
                PreflightCheckNames.GoogleCalendarList,
                $"the token read {calendars.Count} calendar(s) from Google.");
            return calendars;
        }
        catch (HttpRequestException ex)
        {
            report.Fail(
                PreflightCheckNames.GoogleCalendarList,
                "preprod issued a Google access token but Google refused to use it. This usually means the "
                + $"granted scopes no longer cover the Calendar API. Google said: {ex.Message}");
            return null;
        }
    }

    private static async Task CheckSavedLocationAsync(
        PreprodApiClient api, SmokeConfiguration configuration, PreflightReport report,
        CancellationToken ct)
    {
        var location = await api.GetLocationAsync(ct);

        if (location is null)
        {
            report.Fail(
                PreflightCheckNames.SavedLocation,
                "preprod has no saved location, so the weather widget has nothing to render and the "
                + $"Open-Meteo scenario cannot mean anything. Save '{configuration.ExpectedLocation}' on "
                + "the preprod kiosk's settings page. The suite will not save it for you — writing a "
                + "setting would also geocode it, which is a third party this ticket keeps out of scope.");
            return;
        }

        // Case- and whitespace-insensitive: the place name is free text a human typed into a settings
        // field, and "Dublin" versus "dublin" is not an environment fault.
        if (!string.Equals(location.PlaceName.Trim(), configuration.ExpectedLocation.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            report.Fail(
                PreflightCheckNames.SavedLocation,
                $"preprod's saved location does not match Smoke__ExpectedLocation "
                + $"('{configuration.ExpectedLocation}'). Either the environment was re-pointed or the "
                + "configured expectation is stale — decide which, then change that one.");
            return;
        }

        report.Pass(
            PreflightCheckNames.SavedLocation,
            $"preprod's saved location matches Smoke__ExpectedLocation "
            + $"('{configuration.ExpectedLocation}').");
    }

    private static async Task CheckFamilyTimeZoneAsync(
        PreprodApiClient api, SmokeConfiguration configuration, PreflightReport report,
        CancellationToken ct)
    {
        var timeZone = await api.GetTimeZoneAsync(ct);

        if (!string.Equals(timeZone.EffectiveIanaZone, configuration.ExpectedTimeZone, StringComparison.Ordinal))
        {
            report.Fail(
                PreflightCheckNames.FamilyTimeZone,
                $"preprod's effective family time zone is '{timeZone.EffectiveIanaZone}' but "
                + $"Smoke__ExpectedTimeZone says '{configuration.ExpectedTimeZone}'. Every wall-clock and "
                + "recurrence assertion in this suite is anchored to that zone, so it must be the zone "
                + "preprod will actually stamp on an outbound write. Fix whichever of the two is wrong — "
                + "the suite will not set it.");
            return;
        }

        report.Pass(
            PreflightCheckNames.FamilyTimeZone,
            $"preprod's effective family time zone is '{timeZone.EffectiveIanaZone}'"
            + $"{(timeZone.IsExplicit ? " (set explicitly)" : " (derived)")}.");
    }

    /// <summary>
    /// The calendar model under test: the expected names exist, and exactly one calendar is flagged
    /// shared — the one configured as the container. More than one shared calendar is not a cosmetic
    /// problem: multi-member events are placed into "the" shared calendar, and there must be one answer.
    /// </summary>
    private static void CheckTestCalendars(
        SmokeCalendarDirectory directory, SmokeConfiguration configuration, PreflightReport report)
    {
        var problems = new List<string>();

        var missingFromPreprod = configuration.PushCapableCalendarNames
            .Where(name => !directory.PreprodHas(name))
            .ToList();

        if (missingFromPreprod.Count > 0)
        {
            problems.Add(
                $"preprod has no calendar named {Quote(missingFromPreprod)}. It knows: "
                + $"{Quote(NamesSafeToPrint(directory.PreprodCalendars.Select(c => c.DisplayName)))}. If a "
                + "name was changed on the Google side, preprod adopts it on the next sync (FHQ-211) — check "
                + "that this environment carries that fix, then re-run.");
        }

        var missingFromGoogle = configuration.PushCapableCalendarNames
            .Where(name => !directory.GoogleCalendars.Any(
                entry => string.Equals(entry.DisplayName, name, StringComparison.Ordinal)))
            .ToList();

        if (missingFromGoogle.Count > 0)
        {
            problems.Add(
                $"the smoke Google account has no calendar named {Quote(missingFromGoogle)}. Create or "
                + "rename it in Google Calendar, or correct Smoke__SharedCalendar / "
                + "Smoke__MemberCalendars.");
        }

        var shared = directory.PreprodCalendars.Where(calendar => calendar.IsShared).ToList();

        if (shared.Count == 0)
        {
            problems.Add(
                $"no calendar is flagged shared. Flag '{configuration.SharedCalendar}' as the shared "
                + "calendar on the preprod kiosk's calendar settings page — multi-member events have "
                + "nowhere to go without it.");
        }
        else if (shared.Count > 1)
        {
            problems.Add(
                $"{shared.Count} calendars are flagged shared "
                + $"({Quote(shared.Select(calendar => calendar.DisplayName))}); there must be exactly one.");
        }
        else if (!string.Equals(shared[0].DisplayName, configuration.SharedCalendar, StringComparison.Ordinal))
        {
            problems.Add(
                $"the calendar flagged shared is not the configured one: Smoke__SharedCalendar says "
                + $"'{configuration.SharedCalendar}'. Move the flag, or correct the configuration.");
        }

        if (problems.Count > 0)
        {
            report.Fail(PreflightCheckNames.TestCalendars, string.Join(" ", problems));
            return;
        }

        report.Pass(
            PreflightCheckNames.TestCalendars,
            $"'{configuration.SharedCalendar}' is the only shared calendar, and every member calendar "
            + $"({Quote(configuration.MemberCalendarNames)}) is present on both sides.");
    }

    /// <summary>
    /// Every push-capable calendar has an unexpired channel.
    /// <para>
    /// A read-only subscription such as a holidays feed legitimately has none — Google will not watch a
    /// calendar the account does not own — so those are named in <c>Smoke__PushIncapableCalendars</c> and
    /// excluded rather than silently tolerated. "Silently tolerated" is how a real missing channel hides.
    /// </para>
    /// </summary>
    private static async Task CheckWebhookRegistrationsAsync(
        PreprodApiClient api, SmokeCalendarDirectory directory, SmokeConfiguration configuration,
        PreflightReport report, CancellationToken ct)
    {
        IReadOnlyList<PreprodWebhookRegistration> registrations;
        try
        {
            registrations = await api.GetWebhookRegistrationsAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            report.Fail(
                PreflightCheckNames.WebhookRegistrations,
                "preprod could not report its Google push channels. The diagnostics endpoint "
                + "GET /api/diagnostics/webhook-registrations arrived with FHQ-141 — if this environment "
                + $"predates it, deploy this branch to preprod first. preprod said: {ex.Message}");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var byCalendar = registrations
            .GroupBy(registration => registration.CalendarInfoId)
            .ToDictionary(group => group.Key, group => group.Max(registration => registration.ExpiresAt));

        var missing = new List<string>();
        var expired = new List<string>();

        foreach (var name in configuration.PushCapableCalendarNames)
        {
            if (!directory.PreprodHas(name))
            {
                // Already reported by the calendar check; do not say it twice.
                continue;
            }

            var calendar = directory.RequirePreprod(name);

            if (!byCalendar.TryGetValue(calendar.Id, out var expiresAt))
            {
                missing.Add(name);
            }
            else if (expiresAt <= now)
            {
                expired.Add($"{name} (expired {expiresAt:u})");
            }
        }

        if (missing.Count > 0 || expired.Count > 0)
        {
            var problems = new List<string>();

            if (missing.Count > 0)
            {
                problems.Add($"no Google push channel is registered for {Quote(missing)}");
            }

            if (expired.Count > 0)
            {
                problems.Add($"the channel has expired for {Quote(expired)}");
            }

            report.Fail(
                PreflightCheckNames.WebhookRegistrations,
                $"{string.Join("; and ", problems)}. Without a live channel, nothing made on a phone reaches "
                + "the kiosk, so the Google-to-kiosk scenarios would fail for an environment reason and "
                + "look like a FamilyHQ bug. Re-register with POST /api/sync/register-webhooks on preprod "
                + "and check Sync:WebhookBaseUrl still points at RelayRobin.");
            return;
        }

        var excluded = configuration.PushIncapableCalendarNames;
        report.Pass(
            PreflightCheckNames.WebhookRegistrations,
            $"every push-capable calendar has an unexpired channel"
            + (excluded.Count == 0
                ? "."
                : $"; {Quote(excluded)} excluded as push-incapable by configuration."));
    }

    private static void SkipBecauseNoSession(PreflightReport report, string checkName) =>
        SkipBecause(report, checkName, "there is no FamilyHQ session token to read preprod with.");

    private static void SkipBecause(PreflightReport report, string checkName, string why) =>
        report.Fail(checkName, $"not checked — {why}");

    private static string Quote(IEnumerable<string> names) =>
        string.Join(", ", names.Select(name => $"'{name}'"));

    /// <summary>
    /// Drops any calendar name that is an email address before it reaches the test log.
    /// <para>
    /// A Google <b>primary</b> calendar's summary <i>is</i> the account's address (FHQ-166), so listing "the
    /// calendars preprod knows about" would put the smoke account's address into archived CI output. The
    /// primary calendar is never one of the expected test calendars — FamilyHQ hides it — so nothing
    /// diagnostic is lost by leaving it out, while the useful part of the message (that a calendar is still
    /// called <c>Work</c> rather than <c>James</c>) survives intact.
    /// </para>
    /// </summary>
    private static IEnumerable<string> NamesSafeToPrint(IEnumerable<string> names) =>
        names.Where(name => !name.Contains('@', StringComparison.Ordinal));
}
