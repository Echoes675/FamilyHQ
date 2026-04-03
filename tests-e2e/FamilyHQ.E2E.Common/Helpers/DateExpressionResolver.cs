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
    {
        var trimmed = expression.Trim();

        if (trimmed.Equals("today", StringComparison.OrdinalIgnoreCase))
            return DateTime.Today.ToString("yyyy-MM-dd");

        if (trimmed.Equals("tomorrow", StringComparison.OrdinalIgnoreCase))
            return DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");

        var match = InDaysPattern().Match(trimmed);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var days))
            return DateTime.Today.AddDays(days).ToString("yyyy-MM-dd");

        // Assume absolute date — validate format
        if (DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return trimmed;

        throw new ArgumentException($"Unrecognised date expression: '{expression}'. Use 'today', 'tomorrow', 'in N days', or 'yyyy-MM-dd'.");
    }

    [GeneratedRegex(@"^in\s+(\d+)\s+days?$", RegexOptions.IgnoreCase)]
    private static partial Regex InDaysPattern();
}
