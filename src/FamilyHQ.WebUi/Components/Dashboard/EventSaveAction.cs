namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// The action the event modal should take when the Save button is pressed, before any
/// recurrence-scope prompt is shown. Computed by <see cref="EventModalLogic.DecideSave"/>.
/// </summary>
public enum EventSaveAction
{
    /// <summary>Create a single, non-recurring event.</summary>
    Create,

    /// <summary>Create a native recurring series (the create modal carried an RRULE).</summary>
    CreateSeries,

    /// <summary>Update a non-recurring event with no recurrence change.</summary>
    Update,

    /// <summary>
    /// Update a previously non-recurring event, promoting it to a series in place
    /// (recurrence toggled ON). Uses the single-event update channel — no scope prompt.
    /// </summary>
    UpdateRecurrenceOn,

    /// <summary>
    /// The event is already part of a recurring series; show the recurrence-scope prompt
    /// before dispatching. The post-confirm decision is made by
    /// <see cref="EventModalLogic.DecideRecurringSave"/>.
    /// </summary>
    PromptScope
}
