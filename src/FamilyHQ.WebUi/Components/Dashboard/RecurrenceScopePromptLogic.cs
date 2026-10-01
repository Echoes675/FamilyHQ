using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Pure decision logic for <c>RecurrenceScopePrompt.razor</c>, extracted so the
/// member-change gating and header wording can be unit-tested without rendering
/// the Blazor component (the project has no bUnit; render/interaction is covered
/// by E2E in FHQ-18.11).
/// </summary>
public static class RecurrenceScopePromptLogic
{
    /// <summary>
    /// Shown when a reminder change is about to be applied to a whole series.
    /// </summary>
    /// <remarks>
    /// Google replaces the reminders on every occurrence when the series master is patched, including
    /// an occurrence whose reminders were set on their own — so an "all events" reminder change loses
    /// that customisation. The kiosk mirrors Google rather than trying to protect the exceptions,
    /// because diverging would leave the family with a series the Google Calendar app then reports
    /// differently. What it can do is say so first.
    /// </remarks>
    public const string ReminderScopeWarningMessage =
        "Changing reminders for all events replaces them on every occurrence, including any single "
        + "date whose reminders were set on their own. Google Calendar does the same.";

    /// <summary>
    /// Whether the OK button may confirm the prompt at the given scope.
    /// </summary>
    /// <remarks>
    /// FHQ-18 §10.1: a pending member change is only valid for the whole series, so
    /// confirmation is blocked at <see cref="RecurrenceScope.ThisOnly"/> and
    /// <see cref="RecurrenceScope.ThisAndFollowing"/> while a member change is pending.
    /// The service rejects it too (FHQ-18.4) — this keeps the user from even trying.
    /// </remarks>
    public static bool IsConfirmAllowed(RecurrenceScope scope, bool memberChangePending) =>
        !memberChangePending || scope == RecurrenceScope.AllInSeries;

    /// <summary>
    /// Whether the inline "member changes apply to the whole series" warning should show:
    /// only when a member change is pending and the chosen scope is not the whole series.
    /// </summary>
    public static bool ShouldShowMemberChangeWarning(RecurrenceScope scope, bool memberChangePending) =>
        memberChangePending && scope != RecurrenceScope.AllInSeries;

    /// <summary>
    /// Whether to warn that a reminder change is about to overwrite per-occurrence reminders: only
    /// when the family changed the Reminders tab and chose the whole series.
    /// </summary>
    /// <remarks>
    /// A warning, never a block — unlike a member change, this is something Google itself does, and
    /// refusing it would leave a reminder the family asked for unset. The narrower scopes are not
    /// warned about: they write to one occurrence or to a fresh series, neither of which touches
    /// another occurrence's own reminders.
    /// </remarks>
    public static bool ShouldShowReminderChangeWarning(RecurrenceScope scope, bool reminderChangePending) =>
        reminderChangePending && scope == RecurrenceScope.AllInSeries;

    /// <summary>The prompt header, switching between the edit and delete flows.</summary>
    public static string HeaderText(bool isDelete) =>
        isDelete ? "Delete recurring event" : "Edit recurring event";
}
