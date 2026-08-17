namespace FamilyHQ.E2E.Common.Helpers;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Resolves relative date expressions like "tomorrow", "today", "in N days"
/// to yyyy-MM-dd date strings. Also passes through absolute yyyy-MM-dd values unchanged.
/// <para>
/// "Today" always means today in <see cref="BrowserClock"/>'s zone — the zone the browser under test
/// is pinned to. There used to be a second entry point (<c>ResolveLondon</c>) that answered with the
/// Europe/London date while this one answered with the test HOST's date; during BST those disagree
/// for the hour between 23:00 and 00:00 UTC, and a seed written through one and asserted through the
/// other silently missed (intermittent-issues #11). The two are now the same method, so the
/// divergence cannot be reintroduced by picking the wrong overload.
/// </para>
/// </summary>
public static partial class DateExpressionResolver
{
    public static string Resolve(string expression)
        => Resolve(expression, BrowserClock.TodayDate);

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
