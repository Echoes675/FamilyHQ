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

    /// <summary>The prompt header, switching between the edit and delete flows.</summary>
    public static string HeaderText(bool isDelete) =>
        isDelete ? "Delete recurring event" : "Edit recurring event";
}
