namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// The units the timed reminder form offers, matching the ones Google's own editor offers. Google
/// itself stores only minutes before the start, so a unit is a way of typing a number, not a value
/// that survives the write.
/// </summary>
public enum ReminderUnit
{
    /// <summary>Minutes before the event starts.</summary>
    Minutes,

    /// <summary>Hours before the event starts.</summary>
    Hours,

    /// <summary>Days before the event starts.</summary>
    Days,

    /// <summary>Weeks before the event starts.</summary>
    Weeks
}
