using FamilyHQ.Smoke.Data.Models;

namespace FamilyHQ.Smoke.Steps.Preflight;

/// <summary>
/// The calendar set, seen from both sides: FamilyHQ's rows and Google's list, joined by display name.
/// <para>
/// Name is the only join key available. FamilyHQ stores the Google calendar id, but does not publish it
/// — and rightly so, because a primary calendar's id is the account's email address (FHQ-166). So the
/// suite matches on the name preprod shows against the name Google shows, which is also exactly the
/// relationship FHQ-211 made FamilyHQ maintain, and preflight fails loudly when they disagree.
/// </para>
/// <para>
/// Built once per run during preflight. Calendars do not appear or disappear mid-run, and re-deriving
/// this per scenario would put two extra round trips in front of every one of them.
/// </para>
/// </summary>
public sealed class SmokeCalendarDirectory(
    IReadOnlyList<PreprodCalendar> preprodCalendars,
    IReadOnlyList<GoogleCalendarListEntry> googleCalendars)
{
    public IReadOnlyList<PreprodCalendar> PreprodCalendars { get; } = preprodCalendars;

    public IReadOnlyList<GoogleCalendarListEntry> GoogleCalendars { get; } = googleCalendars;

    /// <summary>The calendar preprod has flagged shared, or null when none is.</summary>
    public PreprodCalendar? SharedCalendar =>
        PreprodCalendars.SingleOrDefault(calendar => calendar.IsShared);

    /// <summary>FamilyHQ's row for <paramref name="displayName"/>.</summary>
    public PreprodCalendar RequirePreprod(string displayName) =>
        PreprodCalendars.FirstOrDefault(
            calendar => string.Equals(calendar.DisplayName, displayName, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"preprod has no calendar named '{displayName}'. It has: "
            + $"{string.Join(", ", PreprodCalendars.Select(calendar => $"'{calendar.DisplayName}'"))}.");

    /// <summary>The Google calendar id behind <paramref name="displayName"/>.</summary>
    public string RequireGoogleId(string displayName) =>
        GoogleCalendars.FirstOrDefault(
            entry => string.Equals(entry.DisplayName, displayName, StringComparison.Ordinal))?.Id
        ?? throw new InvalidOperationException(
            $"The smoke Google account has no calendar named '{displayName}'.");

    /// <summary>True when preprod knows a calendar by this name.</summary>
    public bool PreprodHas(string displayName) =>
        PreprodCalendars.Any(
            calendar => string.Equals(calendar.DisplayName, displayName, StringComparison.Ordinal));
}
