namespace FamilyHQ.Services.Calendar.Recurrence;

/// <summary>
/// A structured, side-effect-free representation of a recurrence rule. Round-trips to and
/// from an RFC 5545 RRULE line via <see cref="RecurrenceRuleBuilder"/>. Models only the
/// subset of RRULE features FamilyHQ supports; unknown parts are dropped on parse.
/// </summary>
public sealed record RecurrenceSpec
{
    /// <summary>The base repetition unit (FREQ).</summary>
    public required RecurrenceFrequency Frequency { get; init; }

    /// <summary>Repeat every N units (INTERVAL). Always at least 1; 1 means "every" unit.</summary>
    public int Interval { get; init; } = 1;

    /// <summary>
    /// The weekdays a weekly recurrence falls on (BYDAY without ordinals). Null or empty for
    /// non-weekly frequencies, or for a weekly recurrence anchored to its start day only.
    /// </summary>
    public IReadOnlyList<DayOfWeek>? ByDay { get; init; }

    /// <summary>
    /// For monthly recurrences, how the recurrence anchors within the month. Null for
    /// non-monthly frequencies.
    /// </summary>
    public MonthlyMode? MonthlyMode { get; init; }

    /// <summary>
    /// The day-of-month anchor (BYMONTHDAY, 1–31) for <see cref="Recurrence.MonthlyMode.ByDate"/>
    /// monthly recurrences and for yearly-by-date recurrences. Null otherwise.
    /// </summary>
    public int? ByMonthDay { get; init; }

    /// <summary>
    /// The ordinal-weekday anchor (BYDAY with a numeric prefix, e.g. 2TU) for
    /// <see cref="Recurrence.MonthlyMode.ByOrdinalWeekday"/> monthly recurrences. Null otherwise.
    /// </summary>
    public OrdinalWeekday? OrdinalWeekday { get; init; }

    /// <summary>The month anchor (BYMONTH, 1–12) for yearly recurrences. Null otherwise.</summary>
    public int? ByMonth { get; init; }

    /// <summary>When the recurrence stops. Never null; defaults to <see cref="RecurrenceEnd.Never"/>.</summary>
    public RecurrenceEnd End { get; init; } = RecurrenceEnd.Never;

    /// <summary>
    /// Structural equality, comparing <see cref="ByDay"/> by sequence rather than by reference.
    /// Two specs that round-trip to the same RRULE are equal.
    /// </summary>
    public bool Equals(RecurrenceSpec? other) =>
        other is not null
        && Frequency == other.Frequency
        && Interval == other.Interval
        && MonthlyMode == other.MonthlyMode
        && ByMonthDay == other.ByMonthDay
        && ByMonth == other.ByMonth
        && OrdinalWeekday == other.OrdinalWeekday
        && End == other.End
        && ByDaySequenceEqual(ByDay, other.ByDay);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Frequency);
        hash.Add(Interval);
        hash.Add(MonthlyMode);
        hash.Add(ByMonthDay);
        hash.Add(ByMonth);
        hash.Add(OrdinalWeekday);
        hash.Add(End);
        if (ByDay is not null)
        {
            foreach (var day in ByDay)
            {
                hash.Add(day);
            }
        }

        return hash.ToHashCode();
    }

    private static bool ByDaySequenceEqual(IReadOnlyList<DayOfWeek>? left, IReadOnlyList<DayOfWeek>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates the cross-field invariants of this spec, failing fast on contradictions.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">When <see cref="Interval"/> is less than 1, or anchors are out of range.</exception>
    public RecurrenceSpec Validate()
    {
        if (Interval < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Interval), Interval, "Interval must be at least 1.");
        }

        if (ByMonthDay is { } day && day is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ByMonthDay), day, "BYMONTHDAY must be between 1 and 31.");
        }

        if (ByMonth is { } month && month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ByMonth), month, "BYMONTH must be between 1 and 12.");
        }

        return this;
    }
}
