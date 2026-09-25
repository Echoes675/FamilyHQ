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

    /// <summary>The next occurrence of <paramref name="weekday"/> strictly after today, in the family's zone.</summary>
    public static DateOnly NextWeekdayAfterToday(DayOfWeek weekday)
    {
        var date = Today.AddDays(1);
        while (date.DayOfWeek != weekday)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    private static TimeZoneInfo ResolveZone(string ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId))
        {
            throw new InvalidOperationException(
                "Smoke__ExpectedTimeZone is not configured. The smoke suite cannot assert a wall-clock "
                + "time without knowing the family's zone; set it to preprod's IANA zone "
                + "(e.g. Europe/Dublin).");
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (TimeZoneNotFoundException) when (TimeZoneInfo.TryConvertIanaIdToWindowsId(ianaId, out var windowsId))
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
    }
}
