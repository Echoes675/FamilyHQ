namespace FamilyHQ.Services.Calendar.Recurrence;

/// <summary>
/// An ordinal-prefixed weekday within a month, e.g. the second Tuesday (<c>2TU</c>)
/// or the last Friday (<c>-1FR</c>). Maps to an RFC 5545 BYDAY value carrying a numeric prefix.
/// </summary>
/// <param name="Ordinal">
/// The occurrence within the month. Positive counts from the start (1 = first, 2 = second);
/// negative counts from the end (-1 = last). Must be non-zero and within the range -5..5.
/// </param>
/// <param name="Day">The weekday this ordinal selects.</param>
public sealed record OrdinalWeekday(int Ordinal, DayOfWeek Day)
{
    /// <summary>The occurrence within the month (positive from start, negative from end).</summary>
    public int Ordinal { get; } =
        Ordinal is >= -5 and <= 5 and not 0
            ? Ordinal
            : throw new ArgumentOutOfRangeException(
                nameof(Ordinal), Ordinal, "Ordinal must be a non-zero value between -5 and 5.");
}
