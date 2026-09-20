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
    /// Save is disabled while Repeat is on with no frequency chosen. With tabs, that reason can sit
    /// on a tab nobody is looking at, so the footer states it. Null when nothing is blocking Save.
    /// </summary>
    public static string? SaveBlockedHint(bool recurrenceComplete) =>
        recurrenceComplete ? null : SaveBlockedByRepeatHint;
}
