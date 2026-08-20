using FamilyHQ.Core.Interfaces;
using NodaTime;

namespace FamilyHQ.Simulator.Services;

/// <summary>
/// Resolves IANA identifiers against NodaTime's bundled tzdb so the Simulator can expand a series
/// master anchored to its <c>start.timeZone</c>, as Google does (FHQ-161).
/// </summary>
/// <remarks>
/// NodaTime's bundled database rather than <see cref="TimeZoneInfo"/> so the Simulator's zone rules
/// are identical to production's regardless of the container's tzdata or globalization mode — a
/// Simulator that disagrees with production about a DST transition is exactly the fidelity gap this
/// ticket exists to close. Stateless and thread-safe — registered as a singleton.
/// </remarks>
public sealed class NodaTimeRecurrenceTimeZoneFactory : IRecurrenceTimeZoneFactory
{
    public IRecurrenceTimeZone? TryCreate(string? ianaTimeZoneId)
    {
        if (string.IsNullOrWhiteSpace(ianaTimeZoneId))
        {
            return null;
        }

        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaTimeZoneId);
        return zone is null ? null : new NodaTimeRecurrenceTimeZone(zone);
    }
}
