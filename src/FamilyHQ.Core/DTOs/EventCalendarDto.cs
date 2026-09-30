using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.DTOs;

/// <param name="DefaultReminders">
/// The calendar's own default reminders, as Google reports them on its calendarList entry. The
/// reminder tab needs them for two things it cannot do otherwise: say what inheriting the defaults
/// actually means, and pre-fill them as editable entries when the family stops inheriting — which is
/// what the Google Calendar app does, and the only alternative is landing on an empty list, silently
/// turning "inherit" into the different state "none".
/// <para>
/// Null when no calendar-list sync has reported them, and also null where this record stands for one
/// of an event's member calendars: the month feed repeats every event once per day it spans, so
/// carrying a copy of the defaults on each member would inflate the grid payload for data nobody
/// reads there. The tab reads them from the calendar list instead.
/// </para>
/// </param>
public record EventCalendarDto(
    Guid Id,
    string DisplayName,
    string? Color,
    bool IsShared = false,
    bool IsVisible = true,
    EventReminders? DefaultReminders = null);
