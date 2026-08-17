using FamilyHQ.Core.Interfaces;
using NodaTime;

namespace FamilyHQ.Simulator.Services;

/// <summary>
/// NodaTime-backed <see cref="IRecurrenceTimeZone"/> over a single tzdb zone (FHQ-161).
/// </summary>
/// <remarks>
/// Deliberately the Simulator's OWN copy rather than a reference to the production implementation in
/// FamilyHQ.Services: the Simulator is a test double whose value is being an independent
/// re-implementation of Google's observable behaviour. Sharing production code here would make it
/// unable to catch the fidelity bugs it exists to catch (see
/// <c>project_simulator_google_write_semantics</c>). It shares only the pure engine in FamilyHQ.Core.
/// </remarks>
internal sealed class NodaTimeRecurrenceTimeZone(DateTimeZone zone) : IRecurrenceTimeZone
{
    public string Id => zone.Id;

    public DateTime ToWallClock(DateTimeOffset instant) =>
        Instant.FromDateTimeOffset(instant).InZone(zone).LocalDateTime.ToDateTimeUnspecified();

    // AtLeniently: an ambiguous wall clock (the repeated hour when the clocks go back) resolves to the
    // earlier instant, a skipped one (the spring-forward gap) shifts forward by the gap — so an
    // occurrence is never dropped, matching what Google's expansion emits.
    public DateTimeOffset ToInstant(DateTime wallClock) =>
        zone.AtLeniently(LocalDateTime.FromDateTime(wallClock)).ToDateTimeOffset();
}
