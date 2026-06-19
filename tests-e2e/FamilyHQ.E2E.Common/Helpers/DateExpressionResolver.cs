namespace FamilyHQ.E2E.Common.Helpers;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Resolves relative date expressions like "tomorrow", "today", "in N days"
/// to yyyy-MM-dd date strings. Also passes through absolute yyyy-MM-dd values unchanged.
/// </summary>
public static partial class DateExpressionResolver
{
    public static string Resolve(string expression)
        => Resolve(expression, DateOnly.FromDateTime(DateTime.Today));

    /// <summary>
    /// Resolves the expression using Europe/London local time.
    /// Use this for weather scenarios where the server queries daily data by BST date.
    /// </summary>
    public static string ResolveLondon(string expression)
    {
        TimeZoneInfo londonZone;
        try
        {
            londonZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        }
        catch (TimeZoneNotFoundException)
        {
            londonZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
        }
        var londonToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, londonZone));
        return Resolve(expression, londonToday);
    }

    private static string Resolve(string expression, DateOnly today)
    {
        var trimmed = expression.Trim();

        if (trimmed.Equals("today", StringComparison.OrdinalIgnoreCase))
            return today.ToString("yyyy-MM-dd");

        if (trimmed.Equals("tomorrow", StringComparison.OrdinalIgnoreCase))
            return today.AddDays(1).ToString("yyyy-MM-dd");

        if (trimmed.Equals("next month", StringComparison.OrdinalIgnoreCase))
            return new DateOnly(today.Year, today.Month, 15).AddMonths(1).ToString("yyyy-MM-dd");

        var match = InDaysPattern().Match(trimmed);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var days))
            return today.AddDays(days).ToString("yyyy-MM-dd");

        // Assume absolute date — validate format
        if (DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return trimmed;

        throw new ArgumentException($"Unrecognised date expression: '{expression}'. Use 'today', 'tomorrow', 'next month', 'in N days', or 'yyyy-MM-dd'.");
    }

    [GeneratedRegex(@"^in\s+(\d+)\s+days?$", RegexOptions.IgnoreCase)]
    private static partial Regex InDaysPattern();
}
