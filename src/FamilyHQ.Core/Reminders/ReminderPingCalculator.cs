using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Reminders;

/// <summary>
/// Turns an event's stored reminders into the moments a phone will actually go off.
/// </summary>
/// <remarks>
/// <para>
/// Two things in here are easy to get wrong and are the reason it is a separate, pure class.
/// </para>
/// <para>
/// <b>An all-day reminder is counted back from local midnight</b>, not from the event's start
/// instant. Google stores "1 day before at 17:00" as 420 minutes before the event day's midnight.
/// Reading it as minutes-before-start agrees whenever the zone offset happens to be zero and
/// diverges by exactly the offset the rest of the year, which makes it a bug that only appears in
/// summer.
/// </para>
/// <para>
/// <b>Four reminder states are not three.</b> Null means "not synced yet" and must produce no ping
/// at all: falling back to the calendar's defaults there would invent a notification the phone is
/// never going to make, and showing a row for it is worse than showing nothing, because the family
/// would rely on it.
/// </para>
/// </remarks>
public static class ReminderPingCalculator
{
    /// <summary>
    /// Every ping this event will produce, in no particular order. Empty when nothing will fire.
    /// </summary>
    /// <param name="start">The event's stored start. For an all-day event this is a date boundary,
    /// which is why the zone is needed rather than the instant alone.</param>
    /// <param name="isAllDay">Selects the arithmetic; see the remarks.</param>
    /// <param name="eventReminders">The event's own reminders as stored, including null.</param>
    /// <param name="calendarDefaults">The owning calendar's defaults, used only when the event
    /// inherits.</param>
    /// <param name="zone">The zone whose midnight an all-day offset is measured from.</param>
    public static IReadOnlyList<ReminderPing> Compute(
        DateTimeOffset start,
        bool isAllDay,
        EventReminders? eventReminders,
        EventReminders? calendarDefaults,
        TimeZoneInfo zone)
    {
        var effective = EffectiveReminders(eventReminders, calendarDefaults);
        if (effective.Count == 0)
        {
            return [];
        }

        var anchor = isAllDay ? LocalMidnightOf(start, zone) : start;

        return [.. effective.Select(reminder => new ReminderPing(
            anchor.AddMinutes(-reminder.Minutes),
            reminder.Method,
            reminder.Minutes))];
    }

    /// <summary>
    /// The reminders that actually apply, resolving inheritance. Empty for all three of the states
    /// that will not ping: never synced, explicitly none, and inheriting from a calendar that has no
    /// defaults of its own.
    /// </summary>
    /// <remarks>
    /// Deliberately does not report WHICH of the two sources the answer came from, because no caller
    /// needs to be told: <c>EventReminders.UseDefault</c> on the stored event — the field Google
    /// itself sets — already answers that question for anyone who does.
    /// </remarks>
    private static IReadOnlyList<EventReminder> EffectiveReminders(
        EventReminders? eventReminders,
        EventReminders? calendarDefaults)
    {
        // Never synced. NOT the same as "no reminders" — the calendar's defaults must not stand in
        // for an answer nobody has yet.
        if (eventReminders is null)
        {
            return [];
        }

        if (eventReminders.UseDefault)
        {
            return calendarDefaults?.Overrides ?? [];
        }

        // Explicit, including the explicitly-empty set the family asked for.
        return eventReminders.Overrides;
    }

    /// <summary>
    /// The instant of midnight at the start of <paramref name="start"/>'s calendar day, in
    /// <paramref name="zone"/>. Uses the offset in effect on that day, so a date either side of a
    /// daylight-saving transition anchors correctly.
    /// </summary>
    /// <remarks>
    /// The calendar date is read straight off <paramref name="start"/>'s UTC date component — it is
    /// NEVER re-derived by converting the instant into <paramref name="zone"/> first and reading the
    /// date back off that. An all-day boundary is stored as midnight UTC specifically so the UTC date
    /// component already IS Google's calendar date, independent of any zone (see the remarks on
    /// <c>GoogleAllDayDate</c> for why midnight UTC, not the calendar's own zone, is the stored
    /// anchor). Converting into the zone before taking the date looks equivalent, and for every zone
    /// at a non-negative offset it IS equivalent, because adding a non-negative offset to a 00:00Z
    /// instant can only move the local clock later in the same day, never earlier across midnight.
    /// For a negative offset it is not: <c>2026-07-15T00:00:00Z</c> converted to UTC-05:00 is
    /// <c>2026-07-14T19:00:00-05:00</c>, whose date is 14 July — a full calendar day before the date
    /// Google actually sent, and every reminder on that event would fire a day early.
    /// <paramref name="zone"/> is consulted ONLY for the offset in effect on the date already taken
    /// from <paramref name="start"/>, never to decide what that date is.
    /// </remarks>
    private static DateTimeOffset LocalMidnightOf(DateTimeOffset start, TimeZoneInfo zone)
    {
        var calendarDate = start.UtcDateTime.Date;
        var offset = zone.GetUtcOffset(new DateTimeOffset(calendarDate, TimeSpan.Zero));
        return new DateTimeOffset(DateTime.SpecifyKind(calendarDate, DateTimeKind.Unspecified), offset);
    }
}
