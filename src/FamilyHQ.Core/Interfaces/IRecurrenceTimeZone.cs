namespace FamilyHQ.Core.Interfaces;

/// <summary>
/// Maps between absolute instants and the wall-clock reading of ONE time zone, so
/// <see cref="Calendar.Recurrence.RecurrenceRuleBuilder"/> can enumerate occurrences the way Google
/// does: anchored to the series' <c>start.timeZone</c>, holding the WALL CLOCK across a DST
/// transition rather than the UTC instant (FHQ-161).
/// </summary>
/// <remarks>
/// Declared without a time-zone-database dependency on purpose. <c>FamilyHQ.WebUi</c> — the Blazor
/// WASM kiosk client — project-references <c>FamilyHQ.Core</c>, so referencing NodaTime from Core
/// would ship the entire TZDB in the payload a Raspberry Pi downloads over the wire. The zone
/// conversion is therefore INJECTED into the pure engine by callers that already carry a tzdb
/// (<c>FamilyHQ.Services</c> and the Simulator).
///
/// Implementations must be pure, side-effect-free and thread-safe: they are called from inside
/// <see cref="Calendar.Recurrence.RecurrenceRuleBuilder"/>, whose contract is no I/O, no async, no DI.
/// </remarks>
public interface IRecurrenceTimeZone
{
    /// <summary>The identifier this instance resolves (an IANA id such as <c>Europe/London</c>).</summary>
    string Id { get; }

    /// <summary>
    /// The wall-clock reading of <paramref name="instant"/> in this zone, as an unspecified-kind
    /// <see cref="DateTime"/>.
    /// </summary>
    DateTime ToWallClock(DateTimeOffset instant);

    /// <summary>
    /// The instant at which <paramref name="wallClock"/> occurs in this zone.
    /// </summary>
    /// <remarks>
    /// Must never throw on a transition. An AMBIGUOUS reading (the hour repeated when the clocks go
    /// back) resolves to the EARLIER instant, and a SKIPPED reading (the gap when they go forward)
    /// shifts FORWARD by the length of the gap. That is the lenient RFC 5545 / Google behaviour, and
    /// it guarantees every rule step still yields exactly one occurrence.
    /// </remarks>
    DateTimeOffset ToInstant(DateTime wallClock);
}
