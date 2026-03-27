using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace FamilyHQ.Services.Calendar;

public sealed class RruleExpander : IRruleExpander
{
    public global::System.Collections.Generic.IEnumerable<global::FamilyHQ.Core.Models.CalendarEvent> ExpandRecurringEvent(
        global::FamilyHQ.Core.Models.CalendarEvent masterEvent,
        global::System.DateTimeOffset rangeStart,
        global::System.DateTimeOffset rangeEnd)
    {
        if (string.IsNullOrEmpty(masterEvent.RecurrenceRule))
            yield break;

        // Build an iCal calendar with the master event
        var calendar = new Ical.Net.Calendar();
        var duration = masterEvent.End - masterEvent.Start;
        
        var icalEvent = new Ical.Net.CalendarComponents.CalendarEvent
        {
            DtStart = new CalDateTime(masterEvent.Start.UtcDateTime),
            DtEnd = new CalDateTime(masterEvent.End.UtcDateTime),
            Summary = masterEvent.Title,
            Uid = masterEvent.GoogleEventId ?? masterEvent.Id.ToString()
        };
        
        // Parse and add the RRULE
        var rruleString = masterEvent.RecurrenceRule;
        if (!rruleString.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
            rruleString = "RRULE:" + rruleString;
        
        var rrule = new RecurrencePattern(rruleString.Replace("RRULE:", "", StringComparison.OrdinalIgnoreCase));
        icalEvent.RecurrenceRules.Add(rrule);
        calendar.Events.Add(icalEvent);
        
        // Get occurrences in range
        var occurrences = icalEvent.GetOccurrences(
            new CalDateTime(rangeStart.UtcDateTime),
            new CalDateTime(rangeEnd.UtcDateTime));
        
        foreach (var occurrence in occurrences)
        {
            var occurrenceStart = new DateTimeOffset(occurrence.Period.StartTime.AsUtc, TimeSpan.Zero);
            var occurrenceEnd = occurrenceStart + duration;
            
            // Create a virtual event instance (not persisted)
            yield return new global::FamilyHQ.Core.Models.CalendarEvent
            {
                Id = masterEvent.Id, // Same ID as master — caller must handle this
                Title = masterEvent.Title,
                Description = masterEvent.Description,
                Start = occurrenceStart,
                End = occurrenceEnd,
                IsAllDay = masterEvent.IsAllDay,
                GoogleEventId = masterEvent.GoogleEventId!,
                RecurrenceRule = masterEvent.RecurrenceRule,
                RecurrenceId = occurrenceStart.ToString("O"), // ISO 8601 = the "instance key"
                IsRecurrenceException = false,
                MasterEventId = masterEvent.Id,
                // Copy calendar associations
                Calendars = masterEvent.Calendars
            };
        }
    }
}
