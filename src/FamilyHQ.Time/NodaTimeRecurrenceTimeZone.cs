using FamilyHQ.Core.Interfaces;
using NodaTime;

namespace FamilyHQ.Time;

/// <summary>
/// NodaTime-backed <see cref="IRecurrenceTimeZone"/> over a single tzdb zone.
/// </summary>
/// <remarks>
/// <para>
/// This is the ONE adapter, shared by <c>FamilyHQ.Services</c> and the Simulator. It sits in its own
/// project because <c>FamilyHQ.Core</c> — which declares the interface — must not take a NodaTime
/// dependency: <c>FamilyHQ.WebUi</c> project-references Core, so a tz database there would ship in
/// the payload the kiosk downloads.
/// </para>
/// <para>
/// There used to be a copy here and a copy in the Simulator, and the Simulator's claimed to be an
/// independent re-implementation of Google's behaviour. It was not — both were the same three
/// delegations to NodaTime, so they agreed because NodaTime agrees with itself, not because two
/// implementations had been checked against each other. Sharing one adapter removes the possibility
/// of the two drifting apart; it does not make either an oracle for what Google does.
/// </para>
/// <para>
/// NodaTime's bundled tzdb rather than <see cref="TimeZoneInfo"/>: the CI/test runtime is
/// globalization-invariant, so the framework's zone lookup is not available there. Immutable and
/// thread-safe, matching <see cref="IRecurrenceTimeZone"/>'s purity contract.
/// </para>
/// </remarks>
public sealed class NodaTimeRecurrenceTimeZone(DateTimeZone zone) : IRecurrenceTimeZone
{
    public string Id => zone.Id;

    public DateTime ToWallClock(DateTimeOffset instant) =>
        Instant.FromDateTimeOffset(instant).InZone(zone).LocalDateTime.ToDateTimeUnspecified();

    // AtLeniently maps an AMBIGUOUS reading (the hour repeated when the clocks go back) to the
    // EARLIER instant and a SKIPPED one (the gap when they go forward) forward by the length of the
    // gap, so a rule step can never throw and never loses an occurrence — the lenient behaviour
    // IRecurrenceTimeZone.ToInstant requires of every implementation.
    public DateTimeOffset ToInstant(DateTime wallClock) =>
        zone.AtLeniently(LocalDateTime.FromDateTime(wallClock)).ToDateTimeOffset();
}
