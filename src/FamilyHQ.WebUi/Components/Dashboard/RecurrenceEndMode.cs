namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// How the recurrence stops, as selected in the picker's end-condition pills. Maps to a
/// <see cref="Core.Calendar.Recurrence.RecurrenceEnd"/> when the rule is built.
/// </summary>
public enum RecurrenceEndMode
{
    /// <summary>The series never ends (no COUNT, no UNTIL).</summary>
    Never,

    /// <summary>The series ends after a fixed number of occurrences (COUNT).</summary>
    Count,

    /// <summary>The series ends on or before a chosen date (UNTIL).</summary>
    Until
}
