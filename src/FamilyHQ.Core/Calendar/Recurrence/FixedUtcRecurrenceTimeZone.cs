using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Core.Calendar.Recurrence;

/// <summary>
/// UTC as an <see cref="IRecurrenceTimeZone"/>: one fixed +00:00 offset and NO daylight-saving
/// rules, so wall clock and instant move together.
/// </summary>
/// <remarks>
/// FHQ-161: this reproduces, exactly, the fixed-UTC enumeration the engine used before the series
/// zone was plumbed through. It is EXACT for date-anchored all-day series (which carry no zone, and
/// which DST cannot move) and NOT DST-aware for anything else — so
/// <see cref="RecurrenceRuleBuilder"/> deliberately offers no default and every caller must name
/// this zone to opt into it. Production's zone-less path on a TIMED series is logged at Warning by
/// <c>CalendarEventService</c> so a real occurrence is diagnosable.
/// </remarks>
public sealed class FixedUtcRecurrenceTimeZone : IRecurrenceTimeZone
{
    /// <summary>The single shared instance — the type is stateless, immutable and thread-safe.</summary>
    public static readonly FixedUtcRecurrenceTimeZone Instance = new();

    private FixedUtcRecurrenceTimeZone()
    {
    }

    public string Id => "UTC";

    // DateTimeOffset.DateTime is always Kind=Unspecified, which is the wall-clock reading the
    // interface asks for (a local reading, not a UTC-kinded instant).
    public DateTime ToWallClock(DateTimeOffset instant) => instant.ToOffset(TimeSpan.Zero).DateTime;

    // No transitions exist here, so no reading is ever ambiguous or skipped.
    public DateTimeOffset ToInstant(DateTime wallClock) =>
        new(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), TimeSpan.Zero);
}
