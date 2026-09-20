namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// FHQ-199: the tabs of the event create/edit modal, in display order. A new tab is a new value
/// here plus a tab button and a pane in <c>EventModal.razor</c> — nothing else in the shell changes.
/// </summary>
public enum EventModalTab
{
    /// <summary>Calendars, title, all-day, dates, times, location and description.</summary>
    Details,

    /// <summary>The recurrence picker.</summary>
    Repeat
}
