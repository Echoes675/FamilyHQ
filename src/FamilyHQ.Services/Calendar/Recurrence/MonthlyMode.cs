namespace FamilyHQ.Services.Calendar.Recurrence;

/// <summary>
/// How a monthly recurrence anchors within each month.
/// </summary>
public enum MonthlyMode
{
    /// <summary>Anchored to a day-of-month, e.g. the 15th (BYMONTHDAY).</summary>
    ByDate,

    /// <summary>Anchored to an ordinal weekday, e.g. the second Tuesday (BYDAY=2TU).</summary>
    ByOrdinalWeekday
}
