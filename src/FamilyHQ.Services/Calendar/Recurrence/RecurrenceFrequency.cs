namespace FamilyHQ.Services.Calendar.Recurrence;

/// <summary>
/// The base unit at which a recurrence repeats (RFC 5545 FREQ).
/// </summary>
public enum RecurrenceFrequency
{
    /// <summary>Repeats every N days (FREQ=DAILY).</summary>
    Daily,

    /// <summary>Repeats every N weeks (FREQ=WEEKLY).</summary>
    Weekly,

    /// <summary>Repeats every N months (FREQ=MONTHLY).</summary>
    Monthly,

    /// <summary>Repeats every N years (FREQ=YEARLY).</summary>
    Yearly
}
