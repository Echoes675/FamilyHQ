using FamilyHQ.Core.Interfaces;
using NodaTime;

namespace FamilyHQ.Services.Calendar;

/// <summary>
/// NodaTime-backed <see cref="IRecurrenceTimeZone"/> over a single tzdb zone (FHQ-161).
/// </summary>
/// <remarks>
/// NodaTime's bundled tzdb rather than <see cref="TimeZoneInfo"/>: the CI/test runtime is
/// globalization-invariant, so the framework's zone lookup is not available there. Immutable and
/// thread-safe, matching <see cref="IRecurrenceTimeZone"/>'s purity contract.
/// </remarks>
internal sealed class NodaTimeRecurrenceTimeZone(DateTimeZone zone) : IRecurrenceTimeZone
{
    public string Id => zone.Id;

    public DateTime ToWallClock(DateTimeOffset instant) =>
        Instant.FromDateTimeOffset(instant).InZone(zone).LocalDateTime.ToDateTimeUnspecified();

    // AtLeniently maps an AMBIGUOUS reading (the hour repeated when the clocks go back) to the
    // EARLIER instant and a SKIPPED one (the gap when they go forward) forward by the length of the
    // gap, so a rule step can never throw and never loses an occurrence — the behaviour Google's own
    // expansion shows.
    public DateTimeOffset ToInstant(DateTime wallClock) =>
        zone.AtLeniently(LocalDateTime.FromDateTime(wallClock)).ToDateTimeOffset();
}
