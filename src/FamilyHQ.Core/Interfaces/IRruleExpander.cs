namespace FamilyHQ.Core.Interfaces;

public interface IRruleExpander
{
    /// <summary>
    /// Expands a recurring event's RRULE into concrete instances within the given date range.
    /// Returns the master event with adjusted start/end times for each occurrence.
    /// Does NOT return exception instances (those are fetched separately from the DB).
    /// </summary>
    global::System.Collections.Generic.IEnumerable<global::FamilyHQ.Core.Models.CalendarEvent> ExpandRecurringEvent(
        global::FamilyHQ.Core.Models.CalendarEvent masterEvent,
        global::System.DateTimeOffset rangeStart,
        global::System.DateTimeOffset rangeEnd);
}
