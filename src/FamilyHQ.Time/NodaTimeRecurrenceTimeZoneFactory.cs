using FamilyHQ.Core.Interfaces;
using NodaTime;

namespace FamilyHQ.Time;

/// <summary>
/// Resolves IANA identifiers against NodaTime's bundled tzdb.
/// </summary>
/// <remarks>
/// The only purpose of this type is to put the static <see cref="DateTimeZoneProviders.Tzdb"/> lookup
/// behind an injectable interface, so recurrence callers can be unit-tested with a substitute zone.
/// Stateless and thread-safe — registered as a singleton by both <c>FamilyHQ.Services</c> and the
/// Simulator, which share this one implementation rather than each carrying a copy.
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
