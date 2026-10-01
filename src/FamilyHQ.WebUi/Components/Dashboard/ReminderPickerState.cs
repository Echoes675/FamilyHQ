namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// The three reminder states Google distinguishes, as the Reminders tab sees them.
/// </summary>
/// <remarks>
/// These must never collapse into one another: each writes a different body back to Google, so
/// treating "no reminders of its own" and "no reminders at all" as the same thing would change an
/// event the family never asked to change. "Not yet synced" is not a value here — it is the absent
/// reminders object, and the picker reads it as following the calendar.
/// </remarks>
public enum ReminderPickerState
{
    /// <summary>The event carries no reminders of its own and changes with the calendar's defaults.</summary>
    FollowsCalendarDefault,

    /// <summary>The event replaces the calendar's defaults with its own list.</summary>
    Explicit,

    /// <summary>
    /// The event replaces the calendar's defaults with nothing. On an all-day event this also covers
    /// reminders Google stores but will not return, so nothing may describe it as "no reminders".
    /// </summary>
    ExplicitlyNone
}
