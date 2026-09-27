namespace FamilyHQ.Smoke.Common.Pages;

/// <summary>
/// A repeat for the kiosk's recurrence picker — always bounded.
/// <para>
/// There is no "never ends" option on this type, and that is the design. Smoke events are <b>kept</b>
/// after the run so a failure can be examined, and an unbounded series left on a live calendar keeps
/// generating occurrences for ever. <see cref="Occurrences"/> is required, so a scenario cannot create
/// an endless series by forgetting to say otherwise.
/// </para>
/// </summary>
/// <param name="Frequency">How the series steps: daily, weekly or yearly.</param>
/// <param name="Occurrences">How many occurrences the series has in total. Two or three is enough to prove a rule.</param>
/// <param name="Interval">Repeat every N units. One unless the scenario is specifically about an interval.</param>
/// <param name="Weekdays">
/// The weekdays a weekly series repeats on. Empty means "the start date's weekday", as Google does.
/// Ignored for any other frequency, because the picker offers the control only for a weekly rule.
/// </param>
public sealed record SmokeRecurrence(
    SmokeRecurrenceFrequency Frequency,
    int Occurrences,
    int Interval = 1,
    IReadOnlyList<DayOfWeek>? Weekdays = null)
{
    /// <summary>A weekly series on the given weekdays.</summary>
    public static SmokeRecurrence Weekly(IReadOnlyList<DayOfWeek> weekdays, int occurrences) =>
        new(SmokeRecurrenceFrequency.Weekly, occurrences, Weekdays: weekdays);

    /// <summary>A weekly series that skips weeks — every <paramref name="interval"/> weeks.</summary>
    public static SmokeRecurrence EveryNWeeks(
        int interval, IReadOnlyList<DayOfWeek> weekdays, int occurrences) =>
        new(SmokeRecurrenceFrequency.Weekly, occurrences, interval, weekdays);

    /// <summary>A daily series.</summary>
    public static SmokeRecurrence Daily(int occurrences) =>
        new(SmokeRecurrenceFrequency.Daily, occurrences);

    /// <summary>A yearly series, anchored to the start date's month and day.</summary>
    public static SmokeRecurrence Yearly(int occurrences) =>
        new(SmokeRecurrenceFrequency.Yearly, occurrences);

    /// <summary>The weekdays to leave selected, which is none unless the series is weekly.</summary>
    public IReadOnlyList<DayOfWeek> SelectedWeekdays =>
        Frequency == SmokeRecurrenceFrequency.Weekly ? Weekdays ?? [] : [];
}
