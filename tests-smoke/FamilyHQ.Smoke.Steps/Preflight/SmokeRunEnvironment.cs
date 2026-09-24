using FamilyHQ.Smoke.Common.Configuration;
using FamilyHQ.Smoke.Data.Google;

namespace FamilyHQ.Smoke.Steps.Preflight;

/// <summary>
/// What preflight established about this run: the credentials it obtained, the calendars it found, and
/// the health report it produced.
/// <para>
/// Run-scoped rather than per-scenario because the credentials are: <c>issue-google-access-token</c>
/// refreshes a real Google grant, and asking for a fresh one per scenario would be nine refreshes of the
/// same grant for no benefit, against a live account, with Google's rate limits watching.
/// </para>
/// <para>
/// The nullable members are deliberate: when the environment is unhealthy there genuinely is no oracle
/// and no calendar directory, and the accessors below fail with a message that says which preflight check
/// explains the absence — rather than a <c>NullReferenceException</c> nine scenarios deep.
/// </para>
/// </summary>
public sealed class SmokeRunEnvironment(
    SmokeConfiguration configuration,
    string correlationId,
    string? sessionJwt,
    GoogleCalendarOracle? oracle,
    SmokeCalendarDirectory? calendars,
    PreflightReport report)
{
    public SmokeConfiguration Configuration { get; } = configuration;

    /// <summary>The correlation id preflight's own calls to preprod carried.</summary>
    public string CorrelationId { get; } = correlationId;

    public PreflightReport Report { get; } = report;

    public string SessionJwt => sessionJwt ?? throw Unavailable(
        "a FamilyHQ session token", PreflightCheckNames.SessionToken);

    /// <summary>
    /// The session token if there is one, without throwing. Used by the scenario hook, which must be able to
    /// set a scenario up even when the environment is broken: the preflight feature's whole job is to report
    /// what is broken, and it cannot do that if the hook dies obtaining a credential it does not need.
    /// </summary>
    public string? SessionJwtOrNull => sessionJwt;

    public GoogleCalendarOracle Google => oracle ?? throw Unavailable(
        "a Google access token", PreflightCheckNames.GoogleAccessToken);

    public SmokeCalendarDirectory Calendars => calendars ?? throw Unavailable(
        "the calendar list", PreflightCheckNames.TestCalendars);

    internal GoogleCalendarOracle? OracleOrNull => oracle;

    private InvalidOperationException Unavailable(string what, string checkName)
    {
        var check = Report.Checks.FirstOrDefault(
            candidate => string.Equals(candidate.Name, checkName, StringComparison.Ordinal));

        return new InvalidOperationException(
            $"This smoke run never obtained {what}, so no scenario that needs it can run. "
            + $"Preflight check '{checkName}' reported: {check?.Message ?? "(the check did not run)"}");
    }
}
