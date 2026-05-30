namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// The top-level selection in the recurrence picker's compact dropdown, mirroring Google
/// Calendar's wording. The four presets seed a default rule from the event's start; Custom
/// exposes the full drawer state.
/// </summary>
public enum RecurrenceMode
{
    /// <summary>No recurrence — the picker emits a null RRULE.</summary>
    DoesNotRepeat,

    /// <summary>Daily preset (FREQ=DAILY, interval 1).</summary>
    Daily,

    /// <summary>Weekly preset, anchored to the start's weekday (FREQ=WEEKLY;BYDAY=&lt;start&gt;).</summary>
    Weekly,

    /// <summary>Monthly preset, anchored to the start's day-of-month (FREQ=MONTHLY;BYMONTHDAY=&lt;start&gt;).</summary>
    Monthly,

    /// <summary>Yearly preset, anchored to the start's month and day (FREQ=YEARLY;BYMONTH;BYMONTHDAY).</summary>
    Yearly,

    /// <summary>Custom — the full editable drawer (frequency, interval, weekdays, monthly mode, end).</summary>
    Custom
}
