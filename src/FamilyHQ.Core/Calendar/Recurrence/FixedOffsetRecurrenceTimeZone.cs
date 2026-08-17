using System.Globalization;
using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Core.Calendar.Recurrence;

/// <summary>
/// A zone with a single fixed UTC offset and NO daylight-saving rules, so wall clock and instant
/// move together. This is what <see cref="RecurrenceRuleBuilder"/> falls back to when a caller
/// supplies no series time zone.
/// </summary>
/// <remarks>
/// FHQ-161: with <see cref="TimeSpan.Zero"/> this reproduces, exactly, the fixed-UTC enumeration the
/// engine used before the series zone was plumbed through — deliberately, so a zone-less caller sees
/// no behaviour change. It is EXACT for date-anchored all-day series (which carry no zone and are
/// unaffected by DST) and is NOT DST-aware for anything else. Production's zone-less path is logged
/// at Warning by <c>CalendarEventService</c> so a real occurrence is diagnosable.
/// </remarks>
internal sealed class FixedOffsetRecurrenceTimeZone(TimeSpan offset) : IRecurrenceTimeZone
{
    public string Id { get; } = FormatId(offset);

    public DateTime ToWallClock(DateTimeOffset instant) => instant.ToOffset(offset).DateTime;

    public DateTimeOffset ToInstant(DateTime wallClock) =>
        new(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), offset);

    // InvariantCulture so the id reads the same regardless of the ambient culture's digits.
    private static string FormatId(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero)
        {
            return "UTC";
        }

        var magnitude = offset.Duration();
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        return string.Create(
            CultureInfo.InvariantCulture,
            $"UTC{sign}{magnitude.Hours:D2}:{magnitude.Minutes:D2}");
    }
}
