namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Pure selection logic for the event modal, extracted so it can be unit-tested
/// without rendering the Blazor component (the project has no bUnit).
/// </summary>
public static class EventModalLogic
{
    /// <summary>
    /// Computes the calendar selection a freshly-opened create modal should start with.
    /// </summary>
    /// <remarks>
    /// FHQ-32: the create modal must NOT silently default to any calendar. Only an
    /// explicitly-passed <paramref name="calendarId"/> (e.g. the Agenda view tapping a
    /// specific column) seeds the selection; otherwise the modal opens with nothing
    /// selected and the existing empty-selection validation blocks Save until the user
    /// picks one. This method deliberately takes no calendar list — the default can never
    /// depend on list order or shared/personal composition.
    /// </remarks>
    public static HashSet<Guid> InitialCreateSelection(Guid? calendarId) =>
        calendarId is { } id && id != Guid.Empty ? [id] : [];
}
