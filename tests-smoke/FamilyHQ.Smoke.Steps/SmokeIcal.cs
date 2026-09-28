using System.Globalization;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// The RRULE lines the smoke suite writes <b>into Google</b>, standing in for a phone.
/// <para>
/// Composed here rather than borrowed from FamilyHQ's own rule builder on purpose. These rules are the
/// <i>input</i> to the thing under test: the scenario states a rule in the calendar standard's own
/// syntax, Google expands it, and preprod is then asked whether it agrees. Generating the input with the
/// same code that has to interpret it would let one shared misunderstanding satisfy both sides.
/// </para>
/// <para>
/// Every factory here takes a bound. There is no unbounded rule to be had, for the same reason the kiosk
/// picker has no endless option: a scenario that fails keeps its events, and an endless series left on a
/// live calendar keeps expanding for ever.
/// </para>
/// </summary>
public static class SmokeIcal
{
    /// <summary>The calendar standard's UTC date-time stamp, which is what an <c>UNTIL</c> carries.</summary>
    private const string UntilFormat = "yyyyMMdd'T'HHmmss'Z'";

    /// <summary>A weekly rule on the named weekdays, stopping after <paramref name="occurrences"/>.</summary>
    public static string WeeklyOn(IReadOnlyList<DayOfWeek> weekdays, int occurrences)
    {
        ArgumentNullException.ThrowIfNull(weekdays);

        return "RRULE:FREQ=WEEKLY"
               + $";BYDAY={string.Join(',', weekdays.Select(Day))}"
               + $";COUNT={occurrences}";
    }

    /// <summary>
    /// A weekly rule on one weekday that stops at <paramref name="untilInclusive"/> rather than after a
    /// count — the other of Google's two ways to bound a series, and the one whose boundary is easy to be
    /// a week wrong about, because <c>UNTIL</c> is inclusive and stated in UTC.
    /// </summary>
    public static string WeeklyOnUntil(DayOfWeek weekday, DateTimeOffset untilInclusive) =>
        $"RRULE:FREQ=WEEKLY;BYDAY={Day(weekday)};UNTIL={UntilStamp(untilInclusive)}";

    /// <summary>A daily rule, stopping after <paramref name="occurrences"/>.</summary>
    public static string Daily(int occurrences) => $"RRULE:FREQ=DAILY;COUNT={occurrences}";

    /// <summary>A yearly rule anchored to the event's own start date, stopping after <paramref name="occurrences"/>.</summary>
    public static string Yearly(int occurrences) => $"RRULE:FREQ=YEARLY;COUNT={occurrences}";

    /// <summary>
    /// The same rule, truncated to end just before <paramref name="splitStart"/> — how the Google
    /// Calendar app ends the first half of a "this and following" change.
    /// </summary>
    public static string TruncatedBefore(string rule, DateTimeOffset splitStart)
    {
        ArgumentNullException.ThrowIfNull(rule);

        // Drop whatever bound the rule already carried before adding the new one: two end conditions on
        // one rule is not a rule Google would be asked to interpret, and which one won would be a guess.
        var parts = rule.Split(';')
            .Where(part => !part.StartsWith("COUNT=", StringComparison.Ordinal)
                           && !part.StartsWith("UNTIL=", StringComparison.Ordinal));

        return string.Join(';', parts) + $";UNTIL={UntilStamp(splitStart.AddSeconds(-1))}";
    }

    /// <summary>The calendar standard's two-letter code for a weekday.</summary>
    public static string Day(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MO",
        DayOfWeek.Tuesday => "TU",
        DayOfWeek.Wednesday => "WE",
        DayOfWeek.Thursday => "TH",
        DayOfWeek.Friday => "FR",
        DayOfWeek.Saturday => "SA",
        DayOfWeek.Sunday => "SU",
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, "Not a day of the week.")
    };

    /// <summary>An instant as an <c>UNTIL</c> value: UTC, to the second, with no separators.</summary>
    public static string UntilStamp(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString(UntilFormat, CultureInfo.InvariantCulture);
}
