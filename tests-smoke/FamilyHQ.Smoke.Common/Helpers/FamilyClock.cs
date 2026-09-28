using FamilyHQ.Smoke.Common.Configuration;

namespace FamilyHQ.Smoke.Common.Helpers;

/// <summary>
/// The family's own zone — <c>Smoke__ExpectedTimeZone</c> — and "now" as the kiosk under test reads it.
/// <para>
/// A wall-clock assertion is the whole point of several smoke scenarios ("the kiosk shows Google's
/// instances at the same wall-clock time"), so there must be exactly one answer to "what time is it
/// there?". That answer is pinned onto the Playwright browser context, used for every date the suite
/// computes, and used again to interpret what Google sends back. The test host's zone never enters it:
/// the Jenkins agent is UTC and the family is not.
/// </para>
/// <para>
/// The zone is read from configuration rather than hard-coded — preflight has already asserted that
/// preprod resolves the same value, so agreement is a checked fact rather than a coincidence.
/// </para>
/// </summary>
public static class FamilyClock
{
    /// <summary>
    /// How far ahead <see cref="NextOffsetChangeDate"/> looks for a daylight-saving transition. A
    /// little over a year, so a zone that observes daylight saving cannot be missed whatever the date
    /// the run happens on, and a zone that does not observe it is reported rather than searched for
    /// ever.
    /// </summary>
    private const int MaxDaysToOffsetChange = 400;

    /// <summary>The IANA id the whole suite and the kiosk browser agree on.</summary>
    public static string TimeZoneId => SmokeConfigurationLoader.Load().ExpectedTimeZone;

    /// <summary>The resolved zone. Falls back to the Windows id when the platform has no IANA database.</summary>
    public static TimeZoneInfo Zone => ResolveZone(TimeZoneId);

    /// <summary>Current wall-clock time in the family's zone — what the kiosk's clock reads.</summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    /// <summary>Today's date in the family's zone.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>
    /// Turns a wall-clock time the scenario means in the family's zone into the instant to send, so a
    /// value put on the wire means the same moment regardless of where the test host is.
    /// </summary>
    public static DateTimeOffset ToFamilyOffset(DateTime familyWallClock)
    {
        var unspecified = DateTime.SpecifyKind(familyWallClock, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, Zone.GetUtcOffset(unspecified));
    }

    /// <summary>The wall-clock time <paramref name="instant"/> reads as in the family's zone.</summary>
    public static DateTime ToFamilyWallClock(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Zone).DateTime;

    /// <summary>
    /// The next date, strictly in the future, on which the family's zone changes its UTC offset —
    /// the next daylight-saving transition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Discovered at run time rather than written down, because a scenario that straddles a
    /// transition by carrying its date in the source stops straddling anything the moment that date
    /// passes — and does so silently, degrading into an ordinary recurrence check that still goes
    /// green. The scenarios built on this also assert that the occurrences either side of the date
    /// really do sit at different offsets, so a transition that cannot be found or a series that fails
    /// to span it is a failure rather than a quiet loss of coverage.
    /// </para>
    /// <para>
    /// The offset is sampled at midday on each candidate date, not at midnight: a transition happens
    /// in the small hours, so midday is unambiguously on the far side of it and is the offset a
    /// mid-morning event actually gets.
    /// </para>
    /// <para>
    /// Day-by-day rather than through the zone's adjustment rules, because the rules have a different
    /// shape depending on whether the host resolved an IANA zone or the Windows one it maps to, and
    /// the answer wanted here — a date — is the same either way.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The zone changes offset at no point in the coming year, so there is no transition to test
    /// against. That is a statement about the configured zone, not a transient condition.
    /// </exception>
    public static DateOnly NextOffsetChangeDate()
    {
        var zone = Zone;
        var first = Today.AddDays(1);
        var offsetAtFirst = OffsetAtMiddayOn(zone, first);

        for (var day = 1; day <= MaxDaysToOffsetChange; day++)
        {
            var candidate = first.AddDays(day);
            if (OffsetAtMiddayOn(zone, candidate) != offsetAtFirst)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"The family's zone '{TimeZoneId}' does not change its UTC offset within "
            + $"{MaxDaysToOffsetChange} days, so no series can be made to straddle a daylight-saving "
            + "transition in it. A scenario about a transition cannot be run against a zone that has "
            + "none; either the configured zone is wrong or these scenarios do not apply to this "
            + "environment.");
    }

    /// <summary>The family zone's UTC offset for a wall-clock time on <paramref name="date"/>.</summary>
    public static TimeSpan OffsetOn(DateOnly date, TimeOnly time) =>
        Zone.GetUtcOffset(DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified));

    private static TimeSpan OffsetAtMiddayOn(TimeZoneInfo zone, DateOnly date) =>
        zone.GetUtcOffset(
            DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Unspecified));

    private static TimeZoneInfo ResolveZone(string ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId))
        {
            throw new InvalidOperationException(
                "Smoke__ExpectedTimeZone is not configured. The smoke suite cannot assert a wall-clock "
                + "time without knowing the family's zone; set it to preprod's IANA zone "
                + "(e.g. Europe/Dublin).");
        }

        return SmokeZones.Resolve(ianaId);
    }
}
