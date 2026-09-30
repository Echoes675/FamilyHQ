using System.Globalization;
using FamilyHQ.Core.Models;
using FamilyHQ.Core.Validators;

namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Puts a stored reminder into words for the Reminders tab. Pure and separate from
/// <see cref="ReminderPicker"/> so the wording can be tested without rendering it.
/// </summary>
/// <remarks>
/// Every value Google can hold has to read truthfully here, including ones the kiosk's own form
/// cannot produce: a phone can set a delivery method Google's write path would drop, and an offset
/// the form would never choose. Nothing is "corrected" on the way to the screen — a reminder the
/// kiosk cannot recreate is still one the family should be able to see.
/// </remarks>
public static class ReminderDescription
{
    /// <summary>What a <c>popup</c> reminder is called on screen: a notification on the phone.</summary>
    public const string PopupLabel = "Notification";

    /// <summary>What an <c>email</c> reminder is called on screen.</summary>
    public const string EmailLabel = "Email";

    private const int MinutesPerHour = 60;
    private const int MinutesPerWeek = 7 * ReminderPickerModel.MinutesPerDay;

    /// <summary>A whole reminder — when it fires and how it is delivered.</summary>
    public static string For(EventReminder reminder, bool isAllDay) =>
        $"{Timing(reminder.Minutes, isAllDay)} · {MethodLabel(reminder.Method)}";

    /// <summary>
    /// When a reminder fires, in the terms its own event type uses: an offset from the start for a
    /// timed event, a day and a time for an all-day one.
    /// </summary>
    public static string Timing(int minutes, bool isAllDay) =>
        isAllDay ? AllDayTiming(minutes) : TimedTiming(minutes);

    /// <summary>
    /// How the reminder is delivered. A method Google did not name is shown exactly as Google sent
    /// it rather than hidden or renamed — it is the family's data, not the kiosk's.
    /// </summary>
    public static string MethodLabel(string method) => method switch
    {
        EventRemindersValidator.PopupMethod => PopupLabel,
        EventRemindersValidator.EmailMethod => EmailLabel,
        _ => method
    };

    /// <summary>
    /// The name of a unit beside the timed form's amount, singular or plural to match it. The amount
    /// itself is in the field next to it, so this is the word only.
    /// </summary>
    public static string UnitLabel(int amount, ReminderUnit unit)
    {
        var word = unit switch
        {
            ReminderUnit.Minutes => "minute",
            ReminderUnit.Hours => "hour",
            ReminderUnit.Days => "day",
            ReminderUnit.Weeks => "week",
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown reminder unit.")
        };

        return amount == 1 ? word : word + "s";
    }

    private static string TimedTiming(int minutes) => minutes switch
    {
        0 => "As the event starts",
        // Google's schema declares no minimum, and its own UI can express a reminder that fires
        // after the start even though its API will not accept one. If such a value arrives, say so.
        < 0 => $"{Plural(-minutes, "minute")} after the event starts",
        _ when minutes % MinutesPerWeek == 0 => $"{Plural(minutes / MinutesPerWeek, "week")} before",
        _ when minutes % ReminderPickerModel.MinutesPerDay == 0 =>
            $"{Plural(minutes / ReminderPickerModel.MinutesPerDay, "day")} before",
        _ when minutes % MinutesPerHour == 0 => $"{Plural(minutes / MinutesPerHour, "hour")} before",
        _ => $"{Plural(minutes, "minute")} before"
    };

    private static string AllDayTiming(int minutes)
    {
        // An all-day offset is counted backwards from local midnight on the event's first day, so
        // anything at or below zero lands on the day itself. The form cannot reach that (its floor is
        // one whole day), but Google clamps a negative offset to 0 and stores it, so a value that
        // arrived from elsewhere still has to read as what it actually does.
        if (minutes <= 0)
        {
            return "At midnight as the day begins";
        }

        var (daysBefore, timeOfDay) = ReminderPickerModel.ToDaysBeforeAndTime(minutes);
        return $"{Plural(daysBefore, "day")} before at {timeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture)}";
    }

    private static string Plural(int count, string unit) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {unit}{(count == 1 ? "" : "s")}";
}
