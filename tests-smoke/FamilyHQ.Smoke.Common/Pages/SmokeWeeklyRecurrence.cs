namespace FamilyHQ.Smoke.Common.Pages;

/// <summary>
/// A weekly repeat for the kiosk's recurrence picker — always bounded (FHQ-141 principle 5).
/// <para>
/// There is no "never ends" option on this type, and that is the design. Smoke events are <b>kept</b>
/// after the run so a failure can be examined, and an unbounded series left on a live calendar keeps
/// generating occurrences for ever. <see cref="Occurrences"/> is required, so a scenario cannot create
/// an endless series by forgetting to say otherwise.
/// </para>
/// </summary>
/// <param name="Weekdays">The weekdays to repeat on. Empty means "the start date's weekday", as Google does.</param>
/// <param name="Occurrences">How many occurrences the series has in total. 2–3 is enough to prove a rule.</param>
public sealed record SmokeWeeklyRecurrence(IReadOnlyList<DayOfWeek> Weekdays, int Occurrences);
