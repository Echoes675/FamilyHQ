namespace FamilyHQ.E2E.Common.Helpers;

/// <summary>
/// The single timezone the whole E2E stack agrees on, and "today" as the browser under test sees it.
/// <para>
/// The suite used to have three different answers to "what day is it?": the Playwright browser
/// (whatever zone the CI host happened to be in — UTC), the test host (<c>DateTime.Today</c> — also
/// UTC in CI), and the weather seed (<c>Europe/London</c>, forced). During BST those disagree for the
/// hour between 23:00 and 00:00 UTC, which is exactly how intermittent-issues #11 failed: the seed
/// wrote hourly rows under the London date while the day view asked the API for the UTC date.
/// </para>
/// <para>
/// <see cref="TimeZoneId"/> is pinned onto the Playwright browser context, and every test-side date
/// calculation resolves through <see cref="Today"/>, so the browser, the test host and the seed
/// cannot drift apart by construction rather than by convention.
/// </para>
/// <para>
/// <c>Europe/London</c> rather than <c>Europe/Dublin</c> (which shares its offsets): the E2E fixture
/// saves a location in Edinburgh, and the server derives <c>Europe/London</c> from those coordinates
/// via <c>GeoTimeZoneLookup</c> for the weather ingest and read windows. Matching the zone the server
/// actually computes from the test's own data makes the alignment a derivation, not a coincidence.
/// It is also the production-accurate setup — a real kiosk browser sits in the family's zone. A UTC
/// browser paired with a Scottish location was the anomaly.
/// </para>
/// </summary>
public static class BrowserClock
{
    public const string TimeZoneId = "Europe/London";

    /// <summary>Windows before ICU mapping exposes the zone under its own id instead of the IANA one.</summary>
    private const string WindowsFallbackTimeZoneId = "GMT Standard Time";

    public static TimeZoneInfo Zone { get; } = ResolveZone();

    /// <summary>Today's date in <see cref="Zone"/> — the date the browser under test renders as "today".</summary>
    public static DateOnly TodayDate => DateOnly.FromDateTime(Now);

    /// <summary>
    /// Midnight today in <see cref="Zone"/>. Drop-in replacement for <c>DateTime.Today</c> in E2E code:
    /// a bare <c>DateTime.Today</c> is the TEST HOST's date, which is not the browser's during the
    /// 23:00–00:00 UTC window.
    /// </summary>
    public static DateTime Today => Now.Date;

    /// <summary>Current wall-clock time in <see cref="Zone"/>, i.e. what the browser's clock reads.</summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    /// <summary>
    /// Turns a wall-clock time the scenario means in the browser's zone ("an event at 14:30") into the
    /// absolute instant to seed, tagged <see cref="DateTimeKind.Utc"/> so it serialises with a `Z`.
    /// <para>
    /// Seed times used to go on the wire naive (no offset), which left the instant to be decided by
    /// whichever process parsed it first: the Simulator's EF converter stamps an unspecified DateTime
    /// as its own container's zone (UTC in Docker), so "14:30" silently became 14:30Z. That only
    /// rendered as 14:30 because the browser happened to be in the same zone as the server — true for
    /// a UTC CI host and true for a UK developer running both locally, but a coincidence either way,
    /// and it breaks the moment the browser's zone is pinned independently. Sending the instant
    /// explicitly makes the seed mean the same wall-clock time regardless of the server's zone, which
    /// is also what real Google does — it always sends an offset.
    /// </para>
    /// </summary>
    public static DateTime ToUtcInstant(DateTime browserWallClock)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(browserWallClock, DateTimeKind.Unspecified), Zone);

    private static TimeZoneInfo ResolveZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(WindowsFallbackTimeZoneId);
        }
    }
}
