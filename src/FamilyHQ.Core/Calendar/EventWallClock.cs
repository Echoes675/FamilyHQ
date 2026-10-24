namespace FamilyHQ.Core.Calendar;

/// <summary>
/// The one answer to "which day is this event on, and at what time does the family read it" — the
/// date and the time of day for one stored instant, derived together so they cannot be derived by two
/// different rules.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule.</b> An all-day event's day is the stored instant's UTC date. A timed event's day is
/// its date in the household zone.
/// </para>
/// <para>
/// <b>Why all-day is NOT converted.</b> <see cref="GoogleAllDayDate"/> stores Google's <c>date</c>
/// field as midnight UTC, deliberately and non-configurably, because that is the only anchoring that
/// round-trips byte-for-byte through persistence. The UTC date of that instant therefore <i>is</i> the
/// date Google sent. Converting it through a zone reads a different day for any negative offset — an
/// event on the 15th stored as <c>2026-06-15T00:00:00Z</c> becomes the 14th at 20:00 in New York — and
/// an all-day event has no zone in Google's model to convert it through in the first place. Reading
/// one into it is the substitution the prime directive forbids, and the one the storage type exists
/// because of.
/// </para>
/// <para>
/// <b>Why timed IS converted, and to the HOUSEHOLD's zone.</b> A timed event is a real instant, so the
/// day it falls on is a question about a clock — and the clock that answers it is the one the family
/// reads. Not the owning calendar's zone: Google Calendar shows a timed event in the <i>viewer's</i>
/// zone, and the kiosk is the viewer. Not the kiosk's own OS zone either: a zone the family chose
/// explicitly in settings outranks whatever the kiosk reports, which is what a settings screen is for.
/// </para>
/// <para>
/// <b>Why a null time rather than a flag.</b> <see cref="Time"/> is null if and only if the event is
/// all-day, and that is the only all-day signal anything downstream needs. Carrying a separate flag
/// beside it would be two representations of one fact, which is how a day came to be derived three
/// different ways in the first place.
/// </para>
/// <para>
/// The last day of a multi-day event is this same rule applied to <c>End.AddTicks(-1)</c>, which
/// yields the inclusive last day both for Google's exclusive next-day midnight and for the legacy
/// inclusive end-of-day rows <see cref="GoogleAllDayDate.IsInclusiveEndOfDay"/> describes.
/// </para>
/// </remarks>
public readonly record struct EventWallClock(DateOnly Date, TimeOnly? Time) : IComparable<EventWallClock>
{
    /// <summary>Applies the rule to one stored instant.</summary>
    /// <param name="instant">
    /// The instant as stored. Npgsql reads a <c>timestamptz</c> back at <c>+00:00</c>, so whatever
    /// offset it carries is not the household's — which is exactly why the timed branch converts.
    /// </param>
    /// <param name="isAllDay">Whether the event is all-day, which selects the branch.</param>
    /// <param name="zone">The household zone. Only the timed branch reads it.</param>
    public static EventWallClock Of(DateTimeOffset instant, bool isAllDay, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (isAllDay)
        {
            return new EventWallClock(DateOnly.FromDateTime(instant.UtcDateTime), null);
        }

        var local = TimeZoneInfo.ConvertTime(instant, zone);
        return new EventWallClock(DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime));
    }

    /// <summary>
    /// Day order, then all-day events before every timed one within a day.
    /// </summary>
    /// <remarks>
    /// All-day first is what Google's own day views do, and it is a deliberate choice rather than an
    /// inherited one: ordering by the stored instant put an all-day event's midnight UTC between a
    /// 00:30 and a 02:00 event in a zone at UTC+1, which is an order nobody picked. It lives here
    /// rather than at the two call sites that order rows — the reminders endpoint and the view — so
    /// that "which day" and "where within the day" are one rule stated in one place.
    /// </remarks>
    public int CompareTo(EventWallClock other)
    {
        var byDate = Date.CompareTo(other.Date);
        if (byDate != 0) return byDate;

        // Comparer<TimeOnly?>.Default orders null BEFORE any value, which is the all-day rule and not
        // a coincidence worth re-implementing by hand.
        return Comparer<TimeOnly?>.Default.Compare(Time, other.Time);
    }
}
