namespace FamilyHQ.Core.Calendar.Recurrence;

/// <summary>
/// Describes when a recurrence stops. A closed set of three variants:
/// <see cref="Never"/>, <see cref="Count"/> (after N occurrences) and
/// <see cref="Until"/> (on or before a date/time). COUNT and UNTIL are mutually
/// exclusive in RFC 5545, which this type enforces by construction.
/// </summary>
public sealed record RecurrenceEnd
{
    private RecurrenceEnd(RecurrenceEndKind kind, int? count, DateTimeOffset? until)
    {
        Kind = kind;
        Occurrences = count;
        UntilUtc = until;
    }

    /// <summary>Which end variant this instance represents.</summary>
    public RecurrenceEndKind Kind { get; }

    /// <summary>The number of occurrences when <see cref="Kind"/> is <see cref="RecurrenceEndKind.Count"/>; otherwise null.</summary>
    public int? Occurrences { get; }

    /// <summary>The end instant (UTC) when <see cref="Kind"/> is <see cref="RecurrenceEndKind.Until"/>; otherwise null.</summary>
    public DateTimeOffset? UntilUtc { get; }

    /// <summary>A recurrence that never ends.</summary>
    public static RecurrenceEnd Never { get; } = new(RecurrenceEndKind.Never, null, null);

    /// <summary>A recurrence that ends after <paramref name="occurrences"/> occurrences.</summary>
    /// <exception cref="ArgumentOutOfRangeException">When <paramref name="occurrences"/> is less than 1.</exception>
    public static RecurrenceEnd Count(int occurrences)
    {
        if (occurrences < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(occurrences), occurrences, "COUNT must be at least 1.");
        }

        return new RecurrenceEnd(RecurrenceEndKind.Count, occurrences, null);
    }

    /// <summary>A recurrence that ends on or before <paramref name="until"/> (stored as UTC).</summary>
    public static RecurrenceEnd Until(DateTimeOffset until) =>
        new(RecurrenceEndKind.Until, null, until.ToUniversalTime());
}
