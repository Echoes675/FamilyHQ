using System.Globalization;

namespace FamilyHQ.Core.Calendar;

/// <summary>
/// The one conversion between Google Calendar's all-day <c>date</c> field and the instant FamilyHQ
/// stores for it. Every read of a <c>start.date</c> / <c>end.date</c> / <c>originalStartTime.date</c>,
/// and every all-day boundary the kiosk builds, goes through here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists (FHQ-174).</b> <c>DateTimeOffset.Parse</c> and <c>DateTime.Parse</c> on a
/// date-only string stamp the <b>host machine's</b> current UTC offset. On a host at UTC+1,
/// <c>Parse("2026-06-15")</c> yields <c>2026-06-15T00:00:00+01:00</c> — an instant of
/// <c>2026-06-14T23:00:00Z</c>. Google sent a date; the code turned it into an instant using a local
/// setting Google never sent, which is exactly the substitution the prime directive forbids. The
/// damage is not theoretical: the <c>Start</c>/<c>End</c> EF converter
/// (<c>v =&gt; v.ToUniversalTime()</c>) then strips the offset, and the outbound all-day mapping
/// formats <c>"yyyy-MM-dd"</c> off what survives — so a one-day all-day event synced on a UTC+1 host
/// and later written back reaches the Google Calendar app starting a day early.
/// </para>
/// <para>
/// <b>Why midnight UTC and not the calendar's zone.</b> A zone-anchored value does not survive
/// persistence. The EF converter reduces it to a UTC instant, and any positive offset lands the
/// previous day — the corruption above. Midnight UTC round-trips byte-for-byte: it passes through
/// <c>ToUniversalTime()</c> unchanged and formats back to the exact string Google sent. That is the
/// property this type is here to guarantee, so anchoring is deliberately NOT configurable.
/// </para>
/// <para>
/// <b>Why <c>ParseExact</c>.</b> Google's <c>date</c> is an RFC 3339 full-date. Anything else is a
/// contract violation and must not be coerced into a plausible instant. Whether that is loud or
/// quiet is the CALLER's decision, which is why both <see cref="Parse"/> (throws) and
/// <see cref="TryParse"/> (returns false) exist: a single kiosk edit should fail visibly, whereas one
/// malformed item in a sync page must not take the other 249 with it.
/// </para>
/// </remarks>
public static class GoogleAllDayDate
{
    /// <summary>
    /// The RFC 3339 full-date shape of Google's <c>date</c> field — the only form accepted, and the
    /// form written back out.
    /// </summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Resolves a Google all-day <c>date</c> value to midnight UTC on that calendar date.
    /// </summary>
    /// <exception cref="FormatException">
    /// The value is not an RFC 3339 full-date. Coercing an unexpected shape would hide a change in
    /// what Google sends behind a silently wrong instant. Callers that must not fail a whole batch
    /// on one bad item use <see cref="TryParse"/> instead.
    /// </exception>
    public static DateTimeOffset Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!TryParse(value, out var parsed))
        {
            throw new FormatException(
                $"A Google all-day date must be an RFC 3339 full-date ('{DateFormat}'); received a value of length {value.Length}.");
        }

        return parsed;
    }

    /// <summary>
    /// Resolves a Google all-day <c>date</c> value to midnight UTC, returning false rather than
    /// throwing when the value is not an RFC 3339 full-date.
    /// </summary>
    /// <remarks>
    /// FHQ-174. For the sync's per-item loop, where one malformed item must not take the whole page
    /// — and, because a retry re-fetches the same page, must not take the calendar's sync forever.
    /// The caller logs and skips, matching how that loop already handles an item with no resolvable
    /// start.
    /// <para>
    /// <c>AssumeUniversal</c> supplies the zone the string does not carry; <c>AdjustToUniversal</c>
    /// normalises the result to offset zero. WITHOUT THE PAIR the parse falls back to the host's
    /// offset — <c>AdjustToUniversal</c> alone does nothing to a zone-less string. The flags are
    /// written out here rather than hoisted into a named constant so that
    /// <c>DateOnlyParseGuardTests</c> can see them: the guard reads the argument list of each parse
    /// call, and a constant would hide the very thing it checks for. Same combination as
    /// <c>RecurrenceRuleBuilder.ParseUntil</c>, for the same reason.
    /// </para>
    /// </remarks>
    public static bool TryParse(string value, out DateTimeOffset result)
    {
        ArgumentNullException.ThrowIfNull(value);

        return DateTimeOffset.TryParseExact(
            value,
            DateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result);
    }

    /// <summary>
    /// Builds the all-day boundary instant for a calendar date the user picked — midnight UTC, the
    /// same representation <see cref="Parse"/> produces for the same day.
    /// </summary>
    /// <remarks>
    /// The kiosk's date pickers hand back a <see cref="DateTime"/> that stands for a date, not an
    /// instant, so its <see cref="DateTime.Kind"/> is irrelevant and is discarded — passing a
    /// <see cref="DateTimeKind.Local"/> value would otherwise make the <see cref="DateTimeOffset"/>
    /// constructor throw on any host that is not at UTC.
    /// </remarks>
    public static DateTimeOffset AtMidnightUtc(DateTime date) =>
        new(DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified), TimeSpan.Zero);

    /// <summary>
    /// Whether an all-day boundary is stored in the canonical representation. False means the row
    /// carries a day-shift hazard: the outbound <c>"yyyy-MM-dd"</c> formatting can name a different
    /// day from the one Google sent.
    /// </summary>
    public static bool IsMidnightUtc(DateTimeOffset value) => value.UtcDateTime.TimeOfDay == TimeSpan.Zero;

    /// <summary>
    /// Whether an all-day <c>End</c> carries the legacy INCLUSIVE end-of-day tick
    /// (<c>23:59:59.9999999</c> UTC) rather than Google's exclusive next-day midnight.
    /// </summary>
    /// <remarks>
    /// FHQ-174. This is a second, unrelated legacy shape, and the audit has to tell the two apart:
    /// such a row fails <see cref="IsMidnightUtc"/> for a reason that has nothing to do with the
    /// host's UTC offset, so counting it alongside the day-shifted rows would corrupt the one number
    /// the existing-data decision rests on. <c>EventModal</c> still reads these rows
    /// (<c>AddTicks(-1).Date</c> yields the same inclusive last day either way).
    /// </remarks>
    public static bool IsInclusiveEndOfDay(DateTimeOffset value) =>
        value.UtcDateTime.TimeOfDay == TimeSpan.FromDays(1) - TimeSpan.FromTicks(1);
}
