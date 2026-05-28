using FamilyHQ.Core.Models;

namespace FamilyHQ.Services.Calendar;

/// <summary>
/// Guards the recurrence-related invariants of a <see cref="CalendarEvent"/>.
/// Fails fast when the recurrence fields are in a combination that cannot
/// represent a real Google Calendar event.
/// </summary>
public static class RecurrenceInvariants
{
    /// <summary>
    /// Validates the recurrence fields of the supplied event.
    /// </summary>
    /// <exception cref="ArgumentNullException">When <paramref name="calendarEvent"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// When <see cref="CalendarEvent.OriginalStartTime"/> is set without a
    /// <see cref="CalendarEvent.GoogleRecurringEventId"/>, or when a
    /// <see cref="CalendarEvent.GoogleRecurringEventId"/> is set without a
    /// <see cref="CalendarEvent.RecurrenceRule"/>.
    /// </exception>
    public static void Validate(CalendarEvent calendarEvent)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);

        if (calendarEvent.OriginalStartTime is not null && calendarEvent.GoogleRecurringEventId is null)
        {
            throw new InvalidOperationException(
                $"OriginalStartTime is set on event '{calendarEvent.GoogleEventId}' but GoogleRecurringEventId is null. " +
                "An exception instance must reference its parent series.");
        }

        if (calendarEvent.GoogleRecurringEventId is not null && calendarEvent.RecurrenceRule is null)
        {
            throw new InvalidOperationException(
                $"GoogleRecurringEventId is set on event '{calendarEvent.GoogleEventId}' but RecurrenceRule is null. " +
                "A recurring event must carry its recurrence rule.");
        }
    }
}
