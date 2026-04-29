using System.Globalization;

namespace FamilyHQ.WebUi.Components.Dashboard;

public static class TimePickerLogic
{
    public const int MinuteStep = 5;

    public static TimeOnly IncrementHour(TimeOnly value) =>
        new((value.Hour + 1) % 24, value.Minute);

    public static TimeOnly DecrementHour(TimeOnly value) =>
        new((value.Hour + 23) % 24, value.Minute);

    public static TimeOnly IncrementMinute(TimeOnly value)
    {
        var totalMinutes = (value.Hour * 60 + value.Minute + MinuteStep) % (24 * 60);
        return new TimeOnly(totalMinutes / 60, totalMinutes % 60);
    }

    public static TimeOnly DecrementMinute(TimeOnly value)
    {
        var totalMinutes = (value.Hour * 60 + value.Minute - MinuteStep + 24 * 60) % (24 * 60);
        return new TimeOnly(totalMinutes / 60, totalMinutes % 60);
    }

    public static bool TryParse(string? text, out TimeOnly value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}
