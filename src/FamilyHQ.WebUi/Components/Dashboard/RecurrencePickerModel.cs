using FamilyHQ.Core.Calendar.Recurrence;
using MonthlyModeEnum = FamilyHQ.Core.Calendar.Recurrence.MonthlyMode;

namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Pure, testable state for the <c>RecurrencePicker</c> component, holding the editable
/// recurrence selection and mapping it to and from a canonical RFC 5545 RRULE string. No
/// Razor, no DI — the Razor render and interaction are covered separately by E2E. Mirrors the
/// extract-logic-from-razor pattern of <see cref="EventModalLogic"/>.
/// </summary>
/// <remarks>
/// The model is anchored to the event's start (<see cref="Start"/>): the compact presets and
/// the custom drawer defaults are all seeded from it (weekly → the start's weekday, monthly →
/// the start's day-of-month, yearly → the start's month and day, monthly-by-ordinal → the
/// "Nth &lt;weekday&gt;" the start falls on). The picker's value is a nullable RRULE: null means
/// "Does not repeat".
/// </remarks>
public sealed class RecurrencePickerModel
{
    private static readonly string[] OrdinalWords =
        ["", "first", "second", "third", "fourth", "fifth"];

    /// <summary>
    /// Creates a model anchored to <paramref name="start"/> with all custom-drawer fields seeded
    /// from it, defaulting to <see cref="RecurrenceMode.DoesNotRepeat"/>.
    /// </summary>
    public RecurrencePickerModel(DateTimeOffset start)
    {
        Start = start;
        Interval = 1;
        Frequency = RecurrenceFrequency.Weekly;
        Weekdays = [start.DayOfWeek];
        MonthlyMode = MonthlyModeEnum.ByDate;
        ByMonthDay = start.Day;
        OrdinalWeekday = StartOrdinalWeekday(start);
        ByMonth = start.Month;
        EndMode = RecurrenceEndMode.Never;
        Count = 1;
        Until = DateOnly.FromDateTime(start.Date).AddMonths(1);
    }

    /// <summary>The event start the picker is anchored to. Read-only after construction.</summary>
    public DateTimeOffset Start { get; }

    /// <summary>The compact top-level selection (Does not repeat / Daily / … / Custom).</summary>
    public RecurrenceMode Mode { get; set; } = RecurrenceMode.DoesNotRepeat;

    /// <summary>
    /// True when the selection is complete enough to save: either non-repeating, or repeating
    /// with a concrete frequency chosen. <see cref="RecurrenceMode.Unset"/> — repeats toggled on
    /// but no frequency picked — is the only incomplete state.
    /// </summary>
    public bool IsSelectionComplete => Mode != RecurrenceMode.Unset;

    /// <summary>The custom-drawer frequency (only consulted when <see cref="Mode"/> is Custom).</summary>
    public RecurrenceFrequency Frequency { get; set; }

    /// <summary>Repeat every N units (only consulted when <see cref="Mode"/> is Custom).</summary>
    public int Interval { get; set; }

    /// <summary>The selected weekdays for a custom WEEKLY rule (BYDAY multi-select).</summary>
    public HashSet<DayOfWeek> Weekdays { get; set; }

    /// <summary>How a custom MONTHLY rule anchors within the month (by-date or by-ordinal-weekday).</summary>
    public MonthlyModeEnum MonthlyMode { get; set; }

    /// <summary>The day-of-month anchor for a custom MONTHLY by-date rule.</summary>
    public int ByMonthDay { get; set; }

    /// <summary>The ordinal-weekday anchor for a custom MONTHLY by-ordinal rule (e.g. 3rd Tuesday).</summary>
    public OrdinalWeekday OrdinalWeekday { get; set; }

    /// <summary>The month anchor for a custom YEARLY rule.</summary>
    public int ByMonth { get; set; }

    /// <summary>The end condition (Never / After N / On date).</summary>
    public RecurrenceEndMode EndMode { get; set; }

    /// <summary>The occurrence count when <see cref="EndMode"/> is <see cref="RecurrenceEndMode.Count"/>.</summary>
    public int Count { get; set; }

    /// <summary>The end date when <see cref="EndMode"/> is <see cref="RecurrenceEndMode.Until"/>.</summary>
    public DateOnly Until { get; set; }

    /// <summary>
    /// Builds the canonical RRULE string for the current state, or null for
    /// <see cref="RecurrenceMode.DoesNotRepeat"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// When the state is invalid (interval &lt; 1, count &lt; 1, anchors out of range) — fail-fast
    /// rather than emitting a malformed rule.
    /// </exception>
    public string? ToRecurrenceRule()
    {
        if (Mode is RecurrenceMode.DoesNotRepeat or RecurrenceMode.Unset)
        {
            return null;
        }

        var spec = Mode == RecurrenceMode.Custom ? BuildCustomSpec() : BuildPresetSpec(Mode);
        return RecurrenceRuleBuilder.ToRRuleString(spec);
    }

    /// <summary>
    /// Initialises a model from an existing RRULE (or its absence) anchored to
    /// <paramref name="start"/>. A null/blank rule yields <see cref="RecurrenceMode.DoesNotRepeat"/>.
    /// A rule that matches a compact preset's default surfaces as that preset; anything else
    /// (non-unit interval, multi-day weekly, explicit end, …) surfaces as Custom. Round-trips:
    /// the resulting model's <see cref="ToRecurrenceRule"/> reproduces the supported rule.
    /// </summary>
    public static RecurrencePickerModel FromRecurrenceRule(string? rrule, DateTimeOffset start)
    {
        var model = new RecurrencePickerModel(start);
        if (string.IsNullOrWhiteSpace(rrule))
        {
            model.Mode = RecurrenceMode.DoesNotRepeat;
            return model;
        }

        var spec = RecurrenceRuleBuilder.ParseRRuleString(rrule);
        model.ApplySpec(spec);
        return model;
    }

    private void ApplySpec(RecurrenceSpec spec)
    {
        Frequency = spec.Frequency;
        Interval = spec.Interval;
        EndMode = MapEnd(spec.End);
        if (spec.End.Kind == RecurrenceEndKind.Count)
        {
            Count = spec.End.Occurrences!.Value;
        }
        else if (spec.End.Kind == RecurrenceEndKind.Until)
        {
            Until = DateOnly.FromDateTime(spec.End.UntilUtc!.Value.UtcDateTime);
        }

        switch (spec.Frequency)
        {
            case RecurrenceFrequency.Weekly:
                Weekdays.Clear();
                foreach (var day in spec.ByDay ?? [Start.DayOfWeek])
                {
                    Weekdays.Add(day);
                }

                break;

            case RecurrenceFrequency.Monthly when spec.MonthlyMode == MonthlyModeEnum.ByOrdinalWeekday
                                                  && spec.OrdinalWeekday is { } ow:
                MonthlyMode = MonthlyModeEnum.ByOrdinalWeekday;
                OrdinalWeekday = ow;
                break;

            case RecurrenceFrequency.Monthly:
                MonthlyMode = MonthlyModeEnum.ByDate;
                ByMonthDay = spec.ByMonthDay ?? Start.Day;
                break;

            case RecurrenceFrequency.Yearly:
                ByMonth = spec.ByMonth ?? Start.Month;
                ByMonthDay = spec.ByMonthDay ?? Start.Day;
                break;
        }

        // A rule is a "preset" only when it equals the preset's seeded default (unit interval,
        // Never end, start-derived anchors). Otherwise it must be edited in the Custom drawer.
        Mode = MatchingPresetMode(spec) ?? RecurrenceMode.Custom;
    }

    // A parsed rule surfaces as a compact preset only when its frequency/anchors match the
    // start-seeded default AND the interval is 1. The end condition (COUNT/UNTIL) is allowed on a
    // preset — it is carried through by reusing the spec's own End in the comparison — so an
    // otherwise-default daily-with-COUNT still reads as "Daily" rather than forcing the Custom drawer.
    private RecurrenceMode? MatchingPresetMode(RecurrenceSpec spec)
    {
        if (spec.Interval != 1)
        {
            return null;
        }

        foreach (var preset in (RecurrenceMode[])[RecurrenceMode.Daily, RecurrenceMode.Weekly, RecurrenceMode.Monthly, RecurrenceMode.Yearly])
        {
            if (BuildPresetAnchor(preset) with { End = spec.End } == spec)
            {
                return preset;
            }
        }

        return null;
    }

    private RecurrenceSpec BuildPresetSpec(RecurrenceMode mode) =>
        BuildPresetAnchor(mode) with { End = BuildEnd() };

    // The frequency + start-seeded anchors for a preset, with a unit interval and no end. The
    // end is layered on by callers (emit applies the picker's end; matching reuses the parsed end).
    private RecurrenceSpec BuildPresetAnchor(RecurrenceMode mode) => mode switch
    {
        RecurrenceMode.Daily => new RecurrenceSpec { Frequency = RecurrenceFrequency.Daily },
        RecurrenceMode.Weekly => new RecurrenceSpec
        {
            Frequency = RecurrenceFrequency.Weekly,
            ByDay = [Start.DayOfWeek]
        },
        RecurrenceMode.Monthly => new RecurrenceSpec
        {
            Frequency = RecurrenceFrequency.Monthly,
            MonthlyMode = MonthlyModeEnum.ByDate,
            ByMonthDay = Start.Day
        },
        RecurrenceMode.Yearly => new RecurrenceSpec
        {
            Frequency = RecurrenceFrequency.Yearly,
            ByMonth = Start.Month,
            ByMonthDay = Start.Day
        },
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a compact preset mode.")
    };

    private RecurrenceSpec BuildCustomSpec()
    {
        var end = BuildEnd();

        return Frequency switch
        {
            RecurrenceFrequency.Daily => new RecurrenceSpec
            {
                Frequency = RecurrenceFrequency.Daily,
                Interval = Interval,
                End = end
            },
            RecurrenceFrequency.Weekly => new RecurrenceSpec
            {
                Frequency = RecurrenceFrequency.Weekly,
                Interval = Interval,
                // An empty selection would silently drift the series, so anchor to the start
                // weekday rather than emitting a BYDAY-less weekly rule.
                ByDay = SelectedWeekdaysOrStart(),
                End = end
            },
            RecurrenceFrequency.Monthly when MonthlyMode == MonthlyModeEnum.ByOrdinalWeekday =>
                new RecurrenceSpec
                {
                    Frequency = RecurrenceFrequency.Monthly,
                    Interval = Interval,
                    MonthlyMode = MonthlyModeEnum.ByOrdinalWeekday,
                    OrdinalWeekday = OrdinalWeekday,
                    End = end
                },
            RecurrenceFrequency.Monthly => new RecurrenceSpec
            {
                Frequency = RecurrenceFrequency.Monthly,
                Interval = Interval,
                MonthlyMode = MonthlyModeEnum.ByDate,
                ByMonthDay = ByMonthDay,
                End = end
            },
            RecurrenceFrequency.Yearly => new RecurrenceSpec
            {
                Frequency = RecurrenceFrequency.Yearly,
                Interval = Interval,
                ByMonth = ByMonth,
                ByMonthDay = ByMonthDay,
                End = end
            },
            _ => throw new ArgumentOutOfRangeException(nameof(Frequency), Frequency, "Unknown frequency.")
        };
    }

    private IReadOnlyList<DayOfWeek> SelectedWeekdaysOrStart() =>
        Weekdays.Count > 0
            ? Weekdays.OrderBy(d => (int)d).ToList()
            : [Start.DayOfWeek];

    private RecurrenceEnd BuildEnd() => EndMode switch
    {
        RecurrenceEndMode.Never => RecurrenceEnd.Never,
        // RecurrenceEnd.Count fails fast on Count < 1 (ArgumentOutOfRangeException).
        RecurrenceEndMode.Count => RecurrenceEnd.Count(Count),
        // UNTIL is the end of the chosen day in UTC so the final day is inclusive.
        RecurrenceEndMode.Until => RecurrenceEnd.Until(
            new DateTimeOffset(Until.Year, Until.Month, Until.Day, 23, 59, 59, TimeSpan.Zero)),
        _ => throw new ArgumentOutOfRangeException(nameof(EndMode), EndMode, "Unknown end mode.")
    };

    private static RecurrenceEndMode MapEnd(RecurrenceEnd end) => end.Kind switch
    {
        RecurrenceEndKind.Count => RecurrenceEndMode.Count,
        RecurrenceEndKind.Until => RecurrenceEndMode.Until,
        _ => RecurrenceEndMode.Never
    };

    // The ordinal-weekday the start falls on within its month (e.g. 2026-06-16 is the 3rd
    // Tuesday → 3TU). Clamped to the modelled 1..5 ordinal range.
    private static OrdinalWeekday StartOrdinalWeekday(DateTimeOffset start)
    {
        var ordinal = ((start.Day - 1) / 7) + 1;
        return new OrdinalWeekday(Math.Min(ordinal, 5), start.DayOfWeek);
    }

    /// <summary>
    /// The "Nth &lt;weekday&gt;" English label for the start's ordinal-weekday position, used by the
    /// monthly by-ordinal pill (e.g. "On the third Tuesday").
    /// </summary>
    public string StartOrdinalWeekdayLabel()
    {
        var word = OrdinalWeekday.Ordinal is > 0 and < 6 ? OrdinalWords[OrdinalWeekday.Ordinal] : "last";
        return $"the {word} {OrdinalWeekday.Day}";
    }
}
