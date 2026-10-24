using FamilyHQ.Core.Calendar;
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
    /// The two panels the view lays out, with every row filed by the day the server put it on.
    /// </summary>
    /// <remarks>
    /// Ordering happens here and filing happens in <see cref="ReminderBucketing"/>, which is
    /// order-preserving: the row type is known here, so the within-a-day rule can be applied and
    /// tested, while the bucketing stays generic over anything carrying a date.
    /// </remarks>
    public static IReadOnlyList<ReminderSection<UpcomingReminderEventViewModel>> Sections(
        IEnumerable<UpcomingReminderEventViewModel> rows,
        DateOnly today) =>
        ReminderBucketing.File(InDayOrder(rows), r => r.EventDate, today);

    /// <summary>
    /// Rows in render order: by day, all-day events first within a day, then by title.
    /// </summary>
    /// <remarks>
    /// The same key the server orders its response by — <c>EventWallClock</c>'s own comparison, which
    /// is where all-day-first is stated and why. Restated here rather than trusted: the view must
    /// render in a defined order whether or not a response happened to arrive sorted, and relying on
    /// the server's order would make the view's appearance a property of an untested coincidence. It
    /// is the same rule, from the same type, so the two cannot drift.
    /// </remarks>
    public static IReadOnlyList<UpcomingReminderEventViewModel> InDayOrder(
        IEnumerable<UpcomingReminderEventViewModel> rows) =>
        rows
            .OrderBy(r => new EventWallClock(r.EventDate, r.EventTimeOfDay))
            .ThenBy(r => r.EventTitle, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Whether every section is empty — the view's single "No reminders coming up" state, distinct
    /// from a section that is merely empty on its own (which collapses to "Nothing" instead).
    /// </summary>
    public static bool IsEntirelyEmpty(IReadOnlyList<ReminderSection<UpcomingReminderEventViewModel>> sections) =>
        sections.All(s => s.Rows.Count == 0);

    /// <summary>
    /// The rows a section actually renders: all of them once <paramref name="expanded"/>, otherwise
    /// capped at <see cref="PreviewRows"/>. Never fetches and never reorders — <paramref name="section"/>'s
    /// own rows are already in <see cref="InDayOrder"/>'s order, which <see cref="ReminderBucketing"/>
    /// preserves.
    /// </summary>
    public static IReadOnlyList<UpcomingReminderEventViewModel> Preview(
        ReminderSection<UpcomingReminderEventViewModel> section, bool expanded) =>
        expanded ? section.Rows : section.Rows.Take(PreviewRows).ToList();

    /// <summary>
    /// How many of <paramref name="rows"/> have their event today — the number the Reminders tab's
    /// badge carries, and 0 when nothing falls today.
    /// </summary>
    /// <remarks>
    /// A date comparison against the day the server filed each row on, so the badge and the rows
    /// reading "Today" beneath it cannot disagree: they are the same comparison on the same two
    /// values.
    /// <para>
    /// Deliberately NOT a count of reminder ping instants: a "1 day before" reminder fires on a
    /// different day from its event, so a badge counting pings would announce a number the family
    /// then fails to find in the list. The badge answers "how much is on today", which is a question
    /// about events.
    /// </para>
    /// <para>
    /// <b>The badge can legitimately exceed the rows on screen, and must not be "fixed" to match
    /// them.</b> This counts every row falling today; a collapsed panel renders at most
    /// <see cref="PreviewRows"/> of the whole month. With ten events today the tab reads "10
    /// reminders today" above six visible rows and a "Show all 10" button. That is the intended
    /// reading: the badge answers "how much is on today", which is a question about the day, while
    /// the preview cap is about how much of a month fits on a wall display before the family asks
    /// for the rest. Capping this to the preview would make the badge under-report the day whenever
    /// the panel happened to be collapsed — and the panel is collapsed every time the kiosk returns
    /// home.
    /// </para>
    /// <para>
    /// Here rather than in <c>Index.razor</c>'s <c>@code</c> block for the usual reason: there
    /// is no bUnit in this repo, so a count a `.razor` file computes for itself is a count nothing can
    /// assert on — and this one is read while the family is on another tab entirely, where no E2E
    /// assertion about the reminders list would notice it drifting.
    /// </para>
    /// </remarks>
    public static int TodayCount(IEnumerable<UpcomingReminderEventViewModel> rows, DateOnly today) =>
        rows.Count(r => r.EventDate == today);

    /// <summary>
    /// The date the view actually measures against: the anchor the server sent, shifted by the kiosk's
    /// own whole-day offset, falling back to the kiosk's date before the first successful fetch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The offset is how the dev clock bridge moves the kiosk's day in lower environments without a
    /// dev-only input on a production endpoint: the kiosk still shifts its own day, it just shifts it
    /// relative to the day the server named. It is provably zero in production —
    /// <c>KioskTimeProvider.AdvanceDays</c> no-ops unless the override flag is on, and that flag is
    /// off in the shipped configuration.
    /// </para>
    /// <para>
    /// The fallback matters more than it looks: a default <see cref="DateOnly"/> is 0001-01-01, which
    /// files every row into neither panel and renders "No reminders coming up" while rows exist. The
    /// kiosk's clock is a fallback here, never an authority — and
    /// <paramref name="kioskToday"/> already carries the offset, which is why it is not shifted again.
    /// </para>
    /// </remarks>
    public static DateOnly EffectiveToday(DateOnly? serverToday, int dayOffset, DateOnly kioskToday) =>
        serverToday is { } anchor ? anchor.AddDays(dayOffset) : kioskToday;

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
