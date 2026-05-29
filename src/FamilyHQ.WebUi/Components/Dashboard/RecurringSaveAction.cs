namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// The service call to dispatch once the user confirms the recurrence-scope prompt for a
/// save of an already-recurring event. Computed by <see cref="EventModalLogic.DecideRecurringSave"/>.
/// </summary>
public enum RecurringSaveAction
{
    /// <summary>
    /// Edit the existing series at the chosen scope via the recurring update channel
    /// (<c>UpdateRecurringAsync</c>).
    /// </summary>
    UpdateRecurring,

    /// <summary>
    /// Collapse the series back to a single event (recurrence toggled OFF) via the
    /// single-event update channel with <c>ClearRecurrence = true</c>. This is inherently
    /// a whole-series operation; the chosen pill is ignored (see
    /// <see cref="EventModalLogic.EffectiveScope"/>).
    /// </summary>
    ClearRecurrence
}
