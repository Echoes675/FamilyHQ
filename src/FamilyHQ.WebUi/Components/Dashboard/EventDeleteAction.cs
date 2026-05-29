namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// The action the event modal should take when the Delete button is pressed.
/// Computed by <see cref="EventModalLogic.DecideDelete"/>.
/// </summary>
public enum EventDeleteAction
{
    /// <summary>Delete the single, non-recurring event immediately (<c>DeleteAsync</c>).</summary>
    Delete,

    /// <summary>
    /// The event is part of a recurring series; show the recurrence-scope prompt (delete mode)
    /// before dispatching to <c>DeleteRecurringAsync</c> with the chosen scope.
    /// </summary>
    PromptScope
}
