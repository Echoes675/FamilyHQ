namespace FamilyHQ.Smoke.Common.Configuration;

/// <summary>
/// Everything the preprod smoke suite needs to know about the environment it is pointed at (FHQ-141).
/// <para>
/// <b>Nothing here has a literal in a step definition.</b> Preprod is mid-migration — two calendars
/// still carry their pre-FHQ-211 names and the shared container is not flagged yet — so the expected
/// names, zone and location are configuration, not code. When the environment catches up, the suite
/// runs unchanged.
/// </para>
/// <para>
/// Loaded from <c>appsettings.json</c> (checked in with empty placeholders) plus environment
/// overrides, exactly as the E2E suite loads <c>TestConfiguration</c> — so CI supplies the real
/// values as <c>Smoke__BaseUrl</c>, <c>Smoke__IssueTokenSecret</c> and so on, and no secret is ever
/// committed.
/// </para>
/// </summary>
public class SmokeConfiguration
{
    /// <summary>Configuration section these settings bind from, i.e. the <c>Smoke__</c> prefix.</summary>
    public const string SectionName = "Smoke";

    /// <summary>
    /// The preprod origin. Serves both the kiosk and the API: preprod routes <c>/</c> to the WebUi
    /// and <c>/api</c> to the WebApi behind one hostname, so there is only one address to configure.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Shared secret for the two smoke token endpoints (FHQ-139), presented as
    /// <c>Authorization: Bearer &lt;secret&gt;</c>. Never logged and never written to an artifact.
    /// </summary>
    public string IssueTokenSecret { get; set; } = string.Empty;

    /// <summary>The smoke account's Google subject identifier — the only account those endpoints mint for.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>The place name preprod is expected to have saved. Preflight asserts it; the suite never sets it.</summary>
    public string ExpectedLocation { get; set; } = string.Empty;

    /// <summary>The IANA zone preprod is expected to resolve as the family's. Preflight asserts it.</summary>
    public string ExpectedTimeZone { get; set; } = string.Empty;

    /// <summary>The calendar expected to be flagged shared — the container multi-member events are written to.</summary>
    public string SharedCalendar { get; set; } = string.Empty;

    /// <summary>
    /// The member calendars, comma-separated (e.g. <c>James,Kirk,Lars,Rob</c>). A single string rather
    /// than an array so one <c>Smoke__MemberCalendars</c> environment variable can carry the whole set.
    /// </summary>
    public string MemberCalendars { get; set; } = string.Empty;

    /// <summary>
    /// Calendars that exist on the smoke account but cannot receive Google push — a subscribed
    /// read-only calendar such as a national holidays feed. Comma-separated, and empty is valid.
    /// Preflight requires no webhook registration for these; everything else must have one.
    /// </summary>
    public string PushIncapableCalendars { get; set; } = string.Empty;

    /// <summary>Google Calendar API root. Configurable so the oracle's address is explicit rather than assumed.</summary>
    public string GoogleCalendarApiBaseUrl { get; set; } = "https://www.googleapis.com/calendar/v3";

    /// <summary>Runs the kiosk browser headless. False only for local debugging.</summary>
    public bool Headless { get; set; } = true;

    /// <summary>Playwright's default operation timeout, in milliseconds.</summary>
    public int DefaultTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// How long a scenario waits for a change made in Google to reach preprod through the live push
    /// path (Google → RelayRobin → preprod → sync → API). One bounded wait per assertion: when it
    /// expires the scenario fails. Nothing is retried and no sync is triggered by hand.
    /// </summary>
    public int PushWaitSeconds { get; set; } = 180;

    /// <summary>
    /// How long a scenario waits for a write made on the kiosk to become visible in Google. Shorter
    /// than <see cref="PushWaitSeconds"/> because the kiosk write is synchronous through FamilyHQ —
    /// this only absorbs Google's own read-after-write latency.
    /// </summary>
    public int GoogleWaitSeconds { get; set; } = 60;

    /// <summary>
    /// Accepts preprod's TLS certificate without validating the chain. Defaults to <c>false</c>
    /// deliberately: the right fix for the internal CA is to trust it on the agent, not to stop
    /// checking. Set it only for an exploratory run from a machine that cannot trust the CA.
    /// </summary>
    public bool AllowUntrustedCertificate { get; set; }

    /// <summary>The member calendar names, parsed from <see cref="MemberCalendars"/>.</summary>
    public IReadOnlyList<string> MemberCalendarNames => SplitNames(MemberCalendars);

    /// <summary>The push-incapable calendar names, parsed from <see cref="PushIncapableCalendars"/>.</summary>
    public IReadOnlyList<string> PushIncapableCalendarNames => SplitNames(PushIncapableCalendars);

    /// <summary>
    /// Every calendar expected to be present on the smoke account and to receive push: the shared
    /// container plus each member.
    /// </summary>
    public IReadOnlyList<string> PushCapableCalendarNames =>
        [SharedCalendar, .. MemberCalendarNames];

    private static IReadOnlyList<string> SplitNames(string raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
