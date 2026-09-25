namespace FamilyHQ.Smoke.Steps.Preflight;

/// <summary>
/// The names the preflight checks are known by, shared between the runner that produces them and the
/// feature file that names them. Constants rather than literals on both sides so a renamed check fails
/// to compile instead of silently matching nothing.
/// </summary>
public static class PreflightCheckNames
{
    public const string SessionToken = "FamilyHQ session token";
    public const string GoogleAccessToken = "Google access token";
    public const string GoogleCalendarList = "Google calendar list";
    public const string SavedLocation = "saved location";
    public const string FamilyTimeZone = "family time zone";
    public const string TestCalendars = "test calendars";
    public const string WebhookRegistrations = "webhook registrations";

    /// <summary>Every check, in the order the runner performs them.</summary>
    public static IReadOnlyList<string> All =>
    [
        SessionToken,
        GoogleAccessToken,
        GoogleCalendarList,
        SavedLocation,
        FamilyTimeZone,
        TestCalendars,
        WebhookRegistrations
    ];
}
