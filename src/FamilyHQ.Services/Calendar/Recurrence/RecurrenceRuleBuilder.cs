using System.Globalization;
using System.Text;

namespace FamilyHQ.Services.Calendar.Recurrence;

/// <summary>
/// A pure, side-effect-free RFC 5545 RRULE engine. Generates RRULE strings from a
/// <see cref="RecurrenceSpec"/>, parses RRULE strings (including the looser forms Google
/// Calendar emits) back into a spec, and produces human-readable English descriptions.
/// No I/O, no async, no DI.
/// </summary>
public static class RecurrenceRuleBuilder
{
    private const string Prefix = "RRULE:";

    private static readonly string[] WeekdayCodes =
        ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];

    private static readonly string[] WeekdayShortNames =
        ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    private static readonly string[] WeekdayLongNames =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private static readonly string[] MonthLongNames =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];

    private static readonly string[] OrdinalWords =
        ["", "first", "second", "third", "fourth", "fifth"];

    /// <summary>
    /// Emits a single RFC 5545 RRULE line for the supplied spec, beginning with <c>RRULE:</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException">When <paramref name="spec"/> is null.</exception>
    public static string ToRRuleString(RecurrenceSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        spec.Validate();

        var parts = new List<string> { $"FREQ={FreqCode(spec.Frequency)}" };

        if (spec.Interval > 1)
        {
            parts.Add($"INTERVAL={spec.Interval}");
        }

        AppendByParts(spec, parts);
        AppendEnd(spec.End, parts);

        return Prefix + string.Join(';', parts);
    }

    /// <summary>
    /// Parses an RRULE string into a <see cref="RecurrenceSpec"/>. Tolerates a leading
    /// <c>RRULE:</c> prefix or its absence, mixed case, BYDAY with or without ordinals,
    /// UNTIL in <c>yyyyMMddTHHmmssZ</c> or date-only <c>yyyyMMdd</c> form, and ignores
    /// unknown parts (WKST, BYSETPOS, BYHOUR, …).
    /// </summary>
    /// <exception cref="ArgumentException">When the input is empty or carries no valid FREQ.</exception>
    public static RecurrenceSpec ParseRRuleString(string rrule)
    {
        if (string.IsNullOrWhiteSpace(rrule))
        {
            throw new ArgumentException("RRULE string is null or empty.", nameof(rrule));
        }

        var body = rrule.Trim();
        if (body.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            body = body[Prefix.Length..];
        }

        var pairs = ParsePairs(body);

        if (!pairs.TryGetValue("FREQ", out var freqRaw) || !TryParseFreq(freqRaw, out var frequency))
        {
            throw new ArgumentException(
                $"RRULE string '{rrule}' is missing a valid FREQ part.", nameof(rrule));
        }

        var interval = pairs.TryGetValue("INTERVAL", out var intervalRaw)
            ? ParseInterval(intervalRaw, rrule)
            : 1;

        var byDayRaw = pairs.GetValueOrDefault("BYDAY");
        var byMonthDay = ParseIntOrNull(pairs.GetValueOrDefault("BYMONTHDAY"));
        var byMonth = ParseIntOrNull(pairs.GetValueOrDefault("BYMONTH"));

        var spec = new RecurrenceSpec
        {
            Frequency = frequency,
            Interval = interval,
            End = ParseEnd(pairs, rrule)
        };

        // Lenient parse: do NOT call Validate(). Any BY-part we cannot model is dropped by
        // ApplyByParts so that syntactically-legitimate Google RRULEs never throw. Strict
        // validation is the emit path's responsibility (see ToRRuleString).
        return ApplyByParts(spec, frequency, byDayRaw, byMonthDay, byMonth);
    }

    /// <summary>
    /// Produces a human-readable English summary of an RRULE string (English only — localisation
    /// is out of scope). Example: <c>"Repeats every 2 weeks on Mon, Wed, Fri"</c>.
    /// </summary>
    public static string Describe(string rrule)
    {
        var spec = ParseRRuleString(rrule);
        var sb = new StringBuilder("Repeats ");

        sb.Append(spec.Frequency switch
        {
            RecurrenceFrequency.Daily => DescribeDaily(spec),
            RecurrenceFrequency.Weekly => DescribeWeekly(spec),
            RecurrenceFrequency.Monthly => DescribeMonthly(spec),
            RecurrenceFrequency.Yearly => DescribeYearly(spec),
            _ => throw new ArgumentOutOfRangeException(nameof(rrule))
        });

        AppendEndDescription(spec.End, sb);

        return sb.ToString();
    }

    /// <summary>
    /// Defensive cap on occurrence enumeration so that an unbounded (Never) rule can never loop
    /// forever. A bounded COUNT rule stops at its COUNT; an UNTIL or Never rule stops here.
    /// </summary>
    public const int MaxEnumeratedOccurrences = 10_000;

    /// <summary>
    /// Counts how many occurrences of <paramref name="rrule"/>, generated forward from
    /// <paramref name="dtStart"/>, start STRICTLY before <paramref name="boundary"/>.
    /// </summary>
    /// <remarks>
    /// Boundary semantics: an occurrence whose start instant equals <paramref name="boundary"/> is
    /// NOT counted (the boundary is exclusive). This matches the "this and following" split where the
    /// split instance becomes occurrence #1 of the forward series, so only the occurrences before it
    /// remain in the truncated original.
    ///
    /// Enumeration is capped at the rule's COUNT (when bounded) or
    /// <see cref="MaxEnumeratedOccurrences"/> (for UNTIL/Never rules) so a Never rule cannot loop
    /// forever. For an UNTIL rule, enumeration also stops once occurrences pass the UNTIL instant.
    /// Pure: no I/O, no async.
    /// </remarks>
    /// <exception cref="ArgumentException">When <paramref name="rrule"/> is empty or has no valid FREQ.</exception>
    public static int CountOccurrencesBefore(string rrule, DateTimeOffset dtStart, DateTimeOffset boundary) =>
        CountOccurrencesBefore(ParseRRuleString(rrule), dtStart, boundary);

    /// <summary>
    /// Spec-based overload of <see cref="CountOccurrencesBefore(string, DateTimeOffset, DateTimeOffset)"/>
    /// for callers that have already parsed the rule, avoiding a redundant re-parse.
    /// </summary>
    /// <exception cref="ArgumentNullException">When <paramref name="spec"/> is null.</exception>
    public static int CountOccurrencesBefore(RecurrenceSpec spec, DateTimeOffset dtStart, DateTimeOffset boundary)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var hardCap = spec.End.Kind == RecurrenceEndKind.Count
            ? Math.Min(spec.End.Occurrences!.Value, MaxEnumeratedOccurrences)
            : MaxEnumeratedOccurrences;

        var untilUtc = spec.End.Kind == RecurrenceEndKind.Until
            ? spec.End.UntilUtc!.Value
            : (DateTimeOffset?)null;

        var boundaryUtc = boundary.ToUniversalTime();
        var before = 0;

        foreach (var occurrence in EnumerateOccurrences(spec, dtStart.ToUniversalTime(), hardCap))
        {
            if (untilUtc is { } until && occurrence > until)
            {
                break;
            }

            if (occurrence < boundaryUtc)
            {
                before++;
            }
            else
            {
                // Occurrences are monotonically increasing, so once we reach/pass the boundary
                // nothing further can fall before it.
                break;
            }
        }

        return before;
    }

    // Lazily yields up to maxOccurrences occurrence start instants (UTC, monotonically increasing)
    // for the supported frequency shapes. Anchored at dtStart; weekly BYDAY expands each week to its
    // selected weekdays. Pure and side-effect-free.
    private static IEnumerable<DateTimeOffset> EnumerateOccurrences(
        RecurrenceSpec spec, DateTimeOffset dtStart, int maxOccurrences)
    {
        return spec.Frequency switch
        {
            RecurrenceFrequency.Daily => EnumerateDaily(spec, dtStart, maxOccurrences),
            RecurrenceFrequency.Weekly => EnumerateWeekly(spec, dtStart, maxOccurrences),
            RecurrenceFrequency.Monthly => EnumerateMonthly(spec, dtStart, maxOccurrences),
            RecurrenceFrequency.Yearly => EnumerateYearly(spec, dtStart, maxOccurrences),
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Frequency, "Unknown frequency.")
        };
    }

    private static IEnumerable<DateTimeOffset> EnumerateDaily(
        RecurrenceSpec spec, DateTimeOffset dtStart, int maxOccurrences)
    {
        var current = dtStart;
        for (var emitted = 0; emitted < maxOccurrences; emitted++)
        {
            yield return current;
            current = current.AddDays(spec.Interval);
        }
    }

    private static IEnumerable<DateTimeOffset> EnumerateWeekly(
        RecurrenceSpec spec, DateTimeOffset dtStart, int maxOccurrences)
    {
        // Weekdays this rule fires on within each week. No BYDAY → anchored to the start's weekday.
        var weekdays = spec.ByDay is { Count: > 0 }
            ? spec.ByDay.Distinct().OrderBy(d => (int)d).ToList()
            : [dtStart.DayOfWeek];

        // Anchor to the Sunday of dtStart's week, then step INTERVAL weeks at a time.
        var weekStart = dtStart.AddDays(-(int)dtStart.DayOfWeek);
        var emitted = 0;

        // Independent week bound so the outer loop cannot spin even if a future caller consumes this
        // without the boundary break and the rule were to stop emitting (Major 4 defensive cap).
        for (var week = 0; week < MaxEnumeratedOccurrences && emitted < maxOccurrences; week++)
        {
            foreach (var day in weekdays)
            {
                var occurrence = weekStart.AddDays((int)day);
                if (occurrence < dtStart)
                {
                    continue; // skip selected weekdays earlier in the first week than the start
                }

                yield return occurrence;
                if (++emitted >= maxOccurrences)
                {
                    yield break;
                }
            }

            weekStart = weekStart.AddDays(7 * spec.Interval);
        }
    }

    private static IEnumerable<DateTimeOffset> EnumerateMonthly(
        RecurrenceSpec spec, DateTimeOffset dtStart, int maxOccurrences)
    {
        var anchorMonth = new DateTimeOffset(dtStart.Year, dtStart.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var timeOfDay = dtStart.TimeOfDay;

        // Independent iteration bound: a rule that never produces an occurrence (e.g.
        // BYMONTHDAY=31;INTERVAL=12 anchored on a 30-day month) never increments emitted, so the loop
        // must be capped on PERIODS ADVANCED — not just on emitted — to guarantee termination (Major 4).
        var emitted = 0;
        for (var period = 0; period < MaxEnumeratedOccurrences && emitted < maxOccurrences; period++)
        {
            var occurrence = spec.MonthlyMode == Recurrence.MonthlyMode.ByOrdinalWeekday && spec.OrdinalWeekday is { } ow
                ? OrdinalWeekdayInMonth(anchorMonth.Year, anchorMonth.Month, ow, timeOfDay)
                : DayOfMonthOrNull(anchorMonth.Year, anchorMonth.Month, spec.ByMonthDay ?? dtStart.Day, timeOfDay);

            if (occurrence is { } occ && occ >= dtStart)
            {
                yield return occ;
                emitted++;
            }

            // Stop before advancing would exceed the representable date range (a never-emitting rule
            // would otherwise overflow AddMonths long before the period cap is reached).
            if (DateTime.MaxValue.AddMonths(-spec.Interval) < anchorMonth.UtcDateTime)
            {
                yield break;
            }

            anchorMonth = anchorMonth.AddMonths(spec.Interval);
        }
    }

    private static IEnumerable<DateTimeOffset> EnumerateYearly(
        RecurrenceSpec spec, DateTimeOffset dtStart, int maxOccurrences)
    {
        var month = spec.ByMonth ?? dtStart.Month;
        var day = spec.ByMonthDay ?? dtStart.Day;
        var timeOfDay = dtStart.TimeOfDay;
        var year = dtStart.Year;

        // Independent iteration bound: a never-emitting rule (e.g. BYMONTH=2;BYMONTHDAY=30 — February
        // 30 never exists) never increments emitted, so cap on YEARS ADVANCED to guarantee
        // termination rather than spinning until DateTime overflow (Major 4).
        var emitted = 0;
        for (var period = 0; period < MaxEnumeratedOccurrences && emitted < maxOccurrences; period++)
        {
            var occurrence = DayOfMonthOrNull(year, month, day, timeOfDay);
            if (occurrence is { } occ && occ >= dtStart)
            {
                yield return occ;
                emitted++;
            }

            // Stop before the year leaves the representable range (a never-emitting yearly rule would
            // otherwise spin until DateTime construction overflows).
            if (year > DateTime.MaxValue.Year - spec.Interval)
            {
                yield break;
            }

            year += spec.Interval;
        }
    }

    // Returns the given day-of-month in the given month, or null when the month is too short
    // (e.g. day 31 in February) so the occurrence is skipped rather than rolling into the next month.
    private static DateTimeOffset? DayOfMonthOrNull(int year, int month, int day, TimeSpan timeOfDay)
    {
        if (day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }

        return new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero) + timeOfDay;
    }

    // Resolves an ordinal weekday (e.g. 2nd Tuesday, last Friday) within a month, or null when the
    // ordinal does not exist (e.g. a 5th Monday in a month with only four).
    private static DateTimeOffset? OrdinalWeekdayInMonth(int year, int month, OrdinalWeekday ow, TimeSpan timeOfDay)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var matching = new List<int>();
        for (var d = 1; d <= daysInMonth; d++)
        {
            if (new DateTime(year, month, d).DayOfWeek == ow.Day)
            {
                matching.Add(d);
            }
        }

        var index = ow.Ordinal > 0 ? ow.Ordinal - 1 : matching.Count + ow.Ordinal;
        if (index < 0 || index >= matching.Count)
        {
            return null;
        }

        return new DateTimeOffset(year, month, matching[index], 0, 0, 0, TimeSpan.Zero) + timeOfDay;
    }

    private static void AppendByParts(RecurrenceSpec spec, List<string> parts)
    {
        switch (spec.Frequency)
        {
            case RecurrenceFrequency.Weekly when spec.ByDay is { Count: > 0 }:
                parts.Add($"BYDAY={string.Join(',', spec.ByDay.Select(d => WeekdayCodes[(int)d]))}");
                break;

            case RecurrenceFrequency.Monthly when spec.MonthlyMode == Recurrence.MonthlyMode.ByOrdinalWeekday
                                                  && spec.OrdinalWeekday is { } ow:
                parts.Add($"BYDAY={ow.Ordinal}{WeekdayCodes[(int)ow.Day]}");
                break;

            case RecurrenceFrequency.Monthly when spec.ByMonthDay is { } md:
                parts.Add($"BYMONTHDAY={md}");
                break;

            case RecurrenceFrequency.Yearly:
                if (spec.ByMonth is { } bm)
                {
                    parts.Add($"BYMONTH={bm}");
                }

                if (spec.ByMonthDay is { } ymd)
                {
                    parts.Add($"BYMONTHDAY={ymd}");
                }

                break;
        }
    }

    private static void AppendEnd(RecurrenceEnd end, List<string> parts)
    {
        switch (end.Kind)
        {
            case RecurrenceEndKind.Count:
                parts.Add($"COUNT={end.Occurrences}");
                break;

            case RecurrenceEndKind.Until:
                // InvariantCulture forces the proleptic Gregorian calendar so the UTC stamp
                // is well-formed regardless of the ambient culture's calendar.
                var until = end.UntilUtc!.Value.ToUniversalTime()
                    .ToString("yyyyMMddTHHmmss", CultureInfo.InvariantCulture);
                parts.Add($"UNTIL={until}Z");
                break;
        }
    }

    private static RecurrenceSpec ApplyByParts(
        RecurrenceSpec spec,
        RecurrenceFrequency frequency,
        string? byDayRaw,
        int? byMonthDay,
        int? byMonth)
    {
        switch (frequency)
        {
            case RecurrenceFrequency.Weekly when byDayRaw is not null:
                var weekdays = ParseWeekdayList(byDayRaw);
                // If every token was unrecognised, omit BYDAY entirely rather than carrying
                // an empty list (a weekly recurrence anchored to its start day).
                return weekdays.Count > 0 ? spec with { ByDay = weekdays } : spec;

            case RecurrenceFrequency.Monthly when TryParseOrdinalWeekday(byDayRaw, out var ow):
                return spec with { MonthlyMode = Recurrence.MonthlyMode.ByOrdinalWeekday, OrdinalWeekday = ow };

            case RecurrenceFrequency.Monthly when IsModelledMonthDay(byMonthDay):
                return spec with { MonthlyMode = Recurrence.MonthlyMode.ByDate, ByMonthDay = byMonthDay };

            case RecurrenceFrequency.Yearly:
                // Drop a BYMONTHDAY we cannot model (e.g. -1 for last-day-of-month) so that
                // emit-time validation never sees an out-of-range anchor.
                return spec with { ByMonth = byMonth, ByMonthDay = IsModelledMonthDay(byMonthDay) ? byMonthDay : null };

            default:
                return spec;
        }
    }

    private static bool IsModelledMonthDay(int? day) => day is >= 1 and <= 31;

    private static RecurrenceEnd ParseEnd(IReadOnlyDictionary<string, string> pairs, string rrule)
    {
        if (pairs.TryGetValue("COUNT", out var countRaw))
        {
            if (!int.TryParse(countRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            {
                throw new ArgumentException($"RRULE string '{rrule}' has an invalid COUNT value.", nameof(rrule));
            }

            return RecurrenceEnd.Count(count);
        }

        if (pairs.TryGetValue("UNTIL", out var untilRaw))
        {
            return RecurrenceEnd.Until(ParseUntil(untilRaw, rrule));
        }

        return RecurrenceEnd.Never;
    }

    private static DateTimeOffset ParseUntil(string value, string rrule)
    {
        var trimmed = value.Trim();

        if (DateTimeOffset.TryParseExact(
                trimmed, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dateTime))
        {
            return dateTime;
        }

        if (DateTimeOffset.TryParseExact(
                trimmed, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dateOnly))
        {
            return dateOnly;
        }

        throw new ArgumentException(
            $"RRULE string '{rrule}' has an UNTIL value '{value}' in an unsupported format.", nameof(rrule));
    }

    private static Dictionary<string, string> ParsePairs(string body)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            pairs[part[..idx].Trim()] = part[(idx + 1)..].Trim();
        }

        return pairs;
    }

    private static List<DayOfWeek> ParseWeekdayList(string raw)
    {
        var days = new List<DayOfWeek>();
        foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Tolerate Google-style input: silently drop any token we can't model
            // rather than throwing (per the lenient-parse contract).
            if (TryParseWeekdayToken(token, out var day))
            {
                days.Add(day);
            }
        }

        return days;
    }

    private static bool TryParseWeekdayToken(string token, out DayOfWeek day)
    {
        day = default;

        // Strip any ordinal prefix (e.g. "2TU" -> "TU", "-1FR" -> "FR"). A token shorter
        // than the two-letter weekday code can never be valid; guard before slicing.
        if (token.Length < 2)
        {
            return false;
        }

        var code = token[^2..].ToUpperInvariant();
        var index = Array.IndexOf(WeekdayCodes, code);
        if (index < 0)
        {
            return false;
        }

        day = (DayOfWeek)index;
        return true;
    }

    private static bool TryParseOrdinalWeekday(string? raw, out OrdinalWeekday? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var token = raw.Split(',')[0].Trim();
        if (token.Length < 3)
        {
            return false;
        }

        var prefix = token[..^2];
        if (!int.TryParse(prefix, NumberStyles.Integer | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var ordinal))
        {
            return false;
        }

        // Drop ordinals outside the modelled non-zero -5..5 range (e.g. "11TH") rather than
        // letting the OrdinalWeekday constructor throw — a Try* method must never throw.
        if (ordinal is 0 or < -5 or > 5)
        {
            return false;
        }

        if (!TryParseWeekdayToken(token, out var day))
        {
            return false;
        }

        result = new OrdinalWeekday(ordinal, day);
        return true;
    }

    private static bool TryParseFreq(string raw, out RecurrenceFrequency frequency)
    {
        switch (raw.Trim().ToUpperInvariant())
        {
            case "DAILY": frequency = RecurrenceFrequency.Daily; return true;
            case "WEEKLY": frequency = RecurrenceFrequency.Weekly; return true;
            case "MONTHLY": frequency = RecurrenceFrequency.Monthly; return true;
            case "YEARLY": frequency = RecurrenceFrequency.Yearly; return true;
            default: frequency = default; return false;
        }
    }

    private static int ParseInterval(string raw, string rrule)
    {
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval) || interval < 1)
        {
            throw new ArgumentException($"RRULE string '{rrule}' has an invalid INTERVAL value.", nameof(rrule));
        }

        return interval;
    }

    private static int? ParseIntOrNull(string? raw) =>
        raw is not null && int.TryParse(raw, NumberStyles.Integer | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string FreqCode(RecurrenceFrequency frequency) => frequency switch
    {
        RecurrenceFrequency.Daily => "DAILY",
        RecurrenceFrequency.Weekly => "WEEKLY",
        RecurrenceFrequency.Monthly => "MONTHLY",
        RecurrenceFrequency.Yearly => "YEARLY",
        _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Unknown frequency.")
    };

    private static string DescribeDaily(RecurrenceSpec spec) =>
        spec.Interval == 1 ? "daily" : $"every {spec.Interval} days";

    private static string DescribeWeekly(RecurrenceSpec spec)
    {
        var unit = spec.Interval == 1 ? "weekly" : $"every {spec.Interval} weeks";
        if (spec.ByDay is not { Count: > 0 })
        {
            return unit;
        }

        // A single weekday reads naturally in full ("on Tuesday"); multiple are abbreviated
        // to keep the summary compact ("on Mon, Wed, Fri").
        var days = spec.ByDay.Count == 1
            ? WeekdayLongNames[(int)spec.ByDay[0]]
            : string.Join(", ", spec.ByDay.Select(d => WeekdayShortNames[(int)d]));
        return $"{unit} on {days}";
    }

    private static string DescribeMonthly(RecurrenceSpec spec)
    {
        var unit = spec.Interval == 1 ? "monthly" : $"every {spec.Interval} months";

        if (spec.MonthlyMode == Recurrence.MonthlyMode.ByOrdinalWeekday && spec.OrdinalWeekday is { } ow)
        {
            return $"{unit} on the {OrdinalWord(ow.Ordinal)} {WeekdayLongNames[(int)ow.Day]}";
        }

        if (spec.ByMonthDay is { } md)
        {
            return $"{unit} on the {Ordinal(md)}";
        }

        return unit;
    }

    private static string DescribeYearly(RecurrenceSpec spec)
    {
        var unit = spec.Interval == 1 ? "yearly" : $"every {spec.Interval} years";

        if (spec.ByMonth is { } month && spec.ByMonthDay is { } day)
        {
            return $"{unit} on {MonthLongNames[month - 1]} {day}";
        }

        return unit;
    }

    private static void AppendEndDescription(RecurrenceEnd end, StringBuilder sb)
    {
        switch (end.Kind)
        {
            case RecurrenceEndKind.Count:
                sb.Append($", {end.Occurrences} times");
                break;

            case RecurrenceEndKind.Until:
                // Format with InvariantCulture so the month name is always English
                // ("12 June 2026"), never localised by the ambient culture ("12 Juni 2026").
                sb.Append(CultureInfo.InvariantCulture, $", until {end.UntilUtc!.Value.ToUniversalTime():d MMMM yyyy}");
                break;
        }
    }

    private static string OrdinalWord(int ordinal) => ordinal switch
    {
        -1 => "last",
        > 0 and < 6 => OrdinalWords[ordinal],
        _ => Ordinal(ordinal)
    };

    private static string Ordinal(int number)
    {
        var suffix = (number % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (number % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            }
        };

        return $"{number}{suffix}";
    }
}
