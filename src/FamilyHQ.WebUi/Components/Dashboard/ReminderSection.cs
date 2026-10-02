namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>The five buckets the reminders timeline files pings into, in match order.</summary>
public enum ReminderSectionKey { Today, Tomorrow, ThisWeek, ThisMonth, NextMonth }

/// <summary>
/// One section of the timeline. Always present even when empty — the view collapses an empty one to
/// a single line, which keeps the two-column layout stable instead of letting sections move about as
/// pings fire.
/// </summary>
public record ReminderSection<T>(ReminderSectionKey Key, string Heading, IReadOnlyList<T> Rows);
