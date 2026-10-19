using FamilyHQ.WebUi.ViewModels;

namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Pure display decisions for <see cref="RemindersView"/>, extracted so they have unit coverage
/// instead of living only in a `.razor` file's `@code` block — the same reason
/// <see cref="ReminderRowDisplay"/> sits beside <see cref="ReminderRow"/>. There is no bUnit in
/// this repo, so logic kept in a Razor component is logic nothing exercises directly.
/// </summary>
public static class RemindersViewLogic
{
    /// <summary>
    /// The capped number of rows a collapsed section shows before "Show all N" appears. A constant
    /// here rather than in the component, so the cap the markup enforces and the cap a test asserts
    /// against are the same value by construction, not by two authors agreeing to keep them in sync.
    /// </summary>
    public const int PreviewRows = 6;

    /// <summary>
    /// The two panels the view lays out, with every row filed by its own EVENT's start.
    /// </summary>
    /// <remarks>
    /// The selector is the whole requirement: a row belongs in the panel containing when the event
    /// HAPPENS, not in whichever panel a reminder's own trigger instant falls in. An event with a
    /// reminder on each of the seven days before it must appear once, under its own day; filing by a
    /// reminder instead scattered it across every section those triggers touched, which is the bug the
    /// family asked to have stopped. The row no longer carries any reminder's instant for a selector to
    /// reach for, so that regression now needs a field put back as well — but this still lives here
    /// rather than in the component's <c>@code</c> block, because there is no bUnit in this repo and a
    /// selector only a `.razor` file names is a selector nothing can assert on.
    /// </remarks>
    public static IReadOnlyList<ReminderSection<UpcomingReminderEventViewModel>> Sections(
        IEnumerable<UpcomingReminderEventViewModel> rows,
        DateOnly today,
        TimeZoneInfo zone) =>
        ReminderBucketing.File(rows, r => r.EventStart, today, zone);

    /// <summary>
    /// Whether every section is empty — the view's single "No reminders coming up" state, distinct
    /// from a section that is merely empty on its own (which collapses to "Nothing" instead).
    /// </summary>
    public static bool IsEntirelyEmpty(IReadOnlyList<ReminderSection<UpcomingReminderEventViewModel>> sections) =>
        sections.All(s => s.Rows.Count == 0);

    /// <summary>
    /// The rows a section actually renders: all of them once <paramref name="expanded"/>, otherwise
    /// capped at <see cref="PreviewRows"/>. Never fetches and never reorders — <paramref name="section"/>'s
    /// own rows are already in event-start order from <see cref="ReminderBucketing"/>.
    /// </summary>
    public static IReadOnlyList<UpcomingReminderEventViewModel> Preview(
        ReminderSection<UpcomingReminderEventViewModel> section, bool expanded) =>
        expanded ? section.Rows : section.Rows.Take(PreviewRows).ToList();

    /// <summary>
    /// How many of <paramref name="rows"/> have their event today — the number the Reminders tab's
    /// badge carries, and 0 when nothing falls today.
    /// </summary>
    /// <remarks>
    /// Counts each row's own <see cref="UpcomingReminderEventViewModel.EventStart"/>, read through
    /// <see cref="ReminderRowDisplay.LocalDate"/> — the same conversion the rows reading "Today" and
    /// the panel they sit in are derived from, so the badge and the list beneath it cannot disagree.
    /// <para>
    /// Deliberately NOT a count of reminder ping instants: a "1 day before" reminder fires on a
    /// different day from its event, so a badge counting pings would announce a number the family
    /// then fails to find in the list. The badge answers "how much is on today", which is a question
    /// about events.
    /// </para>
    /// <para>
    /// Here rather than in <c>Index.razor</c>'s <c>@code</c> block for the usual reason: there
    /// is no bUnit in this repo, so a count a `.razor` file computes for itself is a count nothing can
    /// assert on — and this one is read while the family is on another tab entirely, where no E2E
    /// assertion about the reminders list would notice it drifting.
    /// </para>
    /// </remarks>
    public static int TodayCount(
        IEnumerable<UpcomingReminderEventViewModel> rows,
        DateOnly today,
        TimeZoneInfo zone) =>
        rows.Count(r => DateOnly.FromDateTime(ReminderRowDisplay.LocalDate(r.EventStart, zone)) == today);

    /// <summary>
    /// The kebab-case identifier one <see cref="ReminderSectionKey"/> renders as, used for both the
    /// section's <c>data-testid</c> and the CSS class that places it in the two-column grid
    /// (<c>reminders-section--{slug}</c> in <c>app.css</c>). The same value drives both, so a wrong
    /// mapping here does not just mislabel a section — it puts it in the wrong column too, and turns
    /// an E2E assertion into a "selector not found" rather than a clear failure. Throws on a value
    /// outside the two <see cref="ReminderSectionKey"/> carries today, rather than falling through
    /// to a default slug that would silently misplace a third one later.
    /// </summary>
    public static string SectionSlug(ReminderSectionKey key) => key switch
    {
        ReminderSectionKey.ThisMonth => "this-month",
        ReminderSectionKey.NextMonth => "next-month",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown reminder section.")
    };
}
