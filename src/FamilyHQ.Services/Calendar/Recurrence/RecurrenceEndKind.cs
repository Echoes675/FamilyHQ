namespace FamilyHQ.Services.Calendar.Recurrence;

/// <summary>
/// Discriminates the variant of a <see cref="RecurrenceEnd"/>.
/// </summary>
public enum RecurrenceEndKind
{
    /// <summary>The series never ends (no COUNT, no UNTIL).</summary>
    Never,

    /// <summary>The series ends after a fixed number of occurrences (COUNT).</summary>
    Count,

    /// <summary>The series ends on or before a fixed date/time (UNTIL).</summary>
    Until
}
