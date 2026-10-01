using FamilyHQ.Core.Models;

namespace FamilyHQ.WebUi.ViewModels;

/// <param name="DefaultReminders">
/// The calendar's default reminders. Populated for the calendars the dashboard loads from the
/// calendar list, which is where the event modal looks them up by the selected calendar's id; null on
/// a summary derived from an event's member list, and null until a calendar-list sync has reported
/// them at all.
/// </param>
public record CalendarSummaryViewModel(
    Guid Id,
    string DisplayName,
    string? Color,
    bool IsShared = false,
    bool IsVisible = true,
    EventReminders? DefaultReminders = null);
