using System.Globalization;
using FamilyHQ.Core.Calendar.Recurrence;

namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// FHQ-199: pure rules for what the event modal's tabs say about their own state. Extracted so
/// they can be unit-tested without rendering <c>EventModal</c> — the project has no bUnit.
/// </summary>
public static class EventModalTabsLogic
{
    /// <summary>Shown beside Save when an unfinished Repeat selection is what disables it.</summary>
    public const string SaveBlockedByRepeatHint = "Finish the Repeat tab to save.";

    /// <summary>Badge for a rule that exists but cannot be parsed: the event still repeats.</summary>
    public const string UnreadableRuleBadge = "Repeats";

    /// <summary>The Reminders badge while the event follows its calendar's default reminders.</summary>
    public const string RemindersDefaultBadge = "default";

    /// <summary>The Reminders badge for a timed event that replaces the calendar's defaults with nothing.</summary>
    public const string RemindersNoneBadge = "none";

    /// <summary>
    /// The Reminders badge for an <b>all-day</b> event that replaces the calendar's defaults with
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="RemindersNoneBadge"/>. Google stores a reminder set for the day
    /// itself and never returns it through its API, so an all-day event the API reports as carrying
    /// nothing may still notify the family. The badge can say what this screen holds; it cannot say
    /// the event has no reminders.
    /// </remarks>
    public const string RemindersNoneHereBadge = "none here";

    /// <summary>
    /// The Repeat tab's badge: null when the event does not repeat, otherwise the rule's frequency
    /// ("Weekly"). The full plain-English description stays in the modal subtitle — a tab needs one word.
    /// </summary>
    public static string? RepeatBadge(string? recurrenceRule)
    {
        if (string.IsNullOrWhiteSpace(recurrenceRule))
        {
            return null;
        }

        try
        {
            return RecurrenceRuleBuilder.ParseRRuleString(recurrenceRule).Frequency.ToString();
        }
        catch (ArgumentException)
        {
            // ParseRRuleString's documented failure for a rule with no valid FREQ. A synced event
            // can carry one; the tab must still show that it repeats rather than fail the render.
            return UnreadableRuleBadge;
        }
    }

    /// <summary>
    /// The Reminders tab's badge: how many reminders the event carries of its own, or that it follows
    /// the calendar's defaults, or that it carries none. Null only when there is no picker to read,
    /// which is the case until the modal has been opened.
    /// </summary>
    /// <param name="reminders">The picker backing the Reminders tab, or null before one is built.</param>
    public static string? RemindersBadge(ReminderPickerModel? reminders) => reminders switch
    {
        null => null,
        { FollowsCalendarDefault: true } => RemindersDefaultBadge,
        { Overrides.Count: 0, IsAllDay: true } => RemindersNoneHereBadge,
        { Overrides.Count: 0 } => RemindersNoneBadge,
        _ => reminders.Overrides.Count.ToString(CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// Save is disabled while Repeat is on with no frequency chosen. With tabs, that reason can sit
    /// on a tab nobody is looking at, so the footer states it. Null when nothing is blocking Save.
    /// </summary>
    public static string? SaveBlockedHint(bool recurrenceComplete) =>
        recurrenceComplete ? null : SaveBlockedByRepeatHint;
}
