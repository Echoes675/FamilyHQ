namespace FamilyHQ.Smoke.Common.Helpers;

/// <summary>
/// Resolving an IANA zone id on whatever host the suite is running on.
/// <para>
/// The suite runs on a Linux agent and on a developer's Windows machine, and the same id has to mean the
/// same zone on both. Windows has no IANA database, so the id is mapped to the Windows one it corresponds
/// to; a failure to resolve is raised rather than swallowed, because a wall-clock assertion made against a
/// zone that silently fell back to something else is worse than no assertion.
/// </para>
/// </summary>
public static class SmokeZones
{
    /// <summary>The zone <paramref name="ianaId"/> names.</summary>
    public static TimeZoneInfo Resolve(string ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId))
        {
            throw new InvalidOperationException(
                "No time zone id was supplied. A wall-clock assertion cannot be made without one.");
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

    /// <summary>
    /// A wall-clock time meant in <paramref name="ianaId"/>, as the instant to put on the wire — so a value
    /// the suite sends means the same moment wherever the test host happens to be.
    /// </summary>
    public static DateTimeOffset ToOffset(string ianaId, DateTime wallClock)
    {
        var zone = Resolve(ianaId);
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }
}
