using FamilyHQ.Core.Calendar.Recurrence;
using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// FHQ-18.7: pure state<->RRULE mapping for the RecurrencePicker. The Razor render and
// interaction are covered later by E2E (FHQ-18.11); these tests pin the model only.
// All anchors use a fixed offset so the emitted UNTIL stamps are deterministic.
public class RecurrencePickerModelTests
{
    // Tuesday, 2026-06-16 09:30 (so weekday defaults => TU, day-of-month => 16).
    private static readonly DateTimeOffset Start =
        new(2026, 6, 16, 9, 30, 0, TimeSpan.Zero);

    // --- ToRecurrenceRule per mode --------------------------------------------------------

    [Fact]
    public void ToRecurrenceRule_DoesNotRepeat_ReturnsNull()
    {
        var model = new RecurrencePickerModel(Start) { Mode = RecurrenceMode.DoesNotRepeat };

        model.ToRecurrenceRule().Should().BeNull();
    }

    [Fact]
    public void ToRecurrenceRule_DailyPreset_EmitsFreqDaily()
    {
        var model = new RecurrencePickerModel(Start) { Mode = RecurrenceMode.Daily };

        model.ToRecurrenceRule().Should().Be("RRULE:FREQ=DAILY");
    }

    [Fact]
    public void ToRecurrenceRule_WeeklyPreset_SeedsByDayFromStartWeekday()
    {
        var model = new RecurrencePickerModel(Start) { Mode = RecurrenceMode.Weekly };

        model.ToRecurrenceRule().Should().Be("RRULE:FREQ=WEEKLY;BYDAY=TU");
    }

    [Fact]
    public void ToRecurrenceRule_MonthlyPreset_SeedsByMonthDayFromStartDay()
    {
        var model = new RecurrencePickerModel(Start) { Mode = RecurrenceMode.Monthly };

        model.ToRecurrenceRule().Should().Be("RRULE:FREQ=MONTHLY;BYMONTHDAY=16");
    }

    [Fact]
    public void ToRecurrenceRule_YearlyPreset_SeedsByMonthAndDayFromStart()
    {
        var model = new RecurrencePickerModel(Start) { Mode = RecurrenceMode.Yearly };

        model.ToRecurrenceRule().Should().Be("RRULE:FREQ=YEARLY;BYMONTH=6;BYMONTHDAY=16");
    }

    // --- Custom: frequency x interval -----------------------------------------------------

    [Fact]
    public void ToRecurrenceRule_CustomDailyEveryThreeDays_EmitsInterval()
    {
        var model = new RecurrencePickerModel(Start)
        {
            Mode = RecurrenceMode.Custom,
            Frequency = RecurrenceFrequency.Daily,
            Interval = 3
        };

        model.ToRecurrenceRule().Should().Be("RRULE:FREQ=DAILY;INTERVAL=3");
    }

    [Fact]
    public void ToRecurrenceRule_CustomWeeklyWithSelectedWeekdays_EmitsByDayInWeekOrder()
    {
        var model = new RecurrencePickerModel(Start)
        {
            Mode = RecurrenceMode.Custom,
            Frequency = RecurrenceFrequency.Weekly,
            Interval = 2,
            Weekdays = new() { DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Wednesday }
        };

        // BYDAY is emitted in Sunday-first calendar order regardless of selection order.
        model.ToRecurrenceRule().Should().Be("RRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR");
    }

    [Fact]
    public void ToRecurrenceRule_CustomMonthlyByOrdinalWeekday_EmitsOrdinalByDay()
    {
        var model = new RecurrencePickerModel(Start)
        {
            Mode = RecurrenceMode.Custom,
            Frequency = RecurrenceFrequency.Monthly,
            MonthlyMode = MonthlyMode.ByOrdinalWeekday
        };

        // 2026-06-16 is the third Tuesday of June.
        model.ToRecurrenceRule().Should().Be("RRULE:FREQ=MONTHLY;BYDAY=3TU");
    }

    // --- End conditions -------------------------------------------------------------------

    [Fact]
    public void ToRecurrenceRule_WithCountEnd_EmitsCount()
    {
        var model = new RecurrencePickerModel(Start)
        {
            Mode = RecurrenceMode.Daily,
            EndMode = RecurrenceEndMode.Count,
            Count = 10
        };

        model.ToRecurrenceRule().Should().Be("RRULE:FREQ=DAILY;COUNT=10");
    }

    [Fact]
    public void ToRecurrenceRule_WithUntilEnd_EmitsUntilUtcStamp()
    {
        var model = new RecurrencePickerModel(Start)
        {
            Mode = RecurrenceMode.Daily,
            EndMode = RecurrenceEndMode.Until,
            Until = new DateOnly(2026, 12, 31)
        };

        // UNTIL is emitted as an end-of-day UTC stamp so the final day is inclusive.
        model.ToRecurrenceRule().Should().Be("RRULE:FREQ=DAILY;UNTIL=20261231T235959Z");
    }

    // --- Preset seeding from Start (explicit weekday/day-of-month) -------------------------

    [Fact]
    public void Constructor_WeeklyPresetOnTuesday_SeedsWeekdaysWithTuesday()
    {
        var model = new RecurrencePickerModel(Start) { Mode = RecurrenceMode.Weekly };

        model.Weekdays.Should().ContainSingle().Which.Should().Be(DayOfWeek.Tuesday);
    }

    [Fact]
    public void Constructor_MonthlyOnFifteenth_SeedsByMonthDayFifteen()
    {
        var fifteenth = new DateTimeOffset(2026, 3, 15, 8, 0, 0, TimeSpan.Zero);

        var model = new RecurrencePickerModel(fifteenth);

        model.ByMonthDay.Should().Be(15);
    }

    // --- FromRecurrenceRule round-trips ---------------------------------------------------

    [Theory]
    [InlineData("RRULE:FREQ=DAILY")]
    [InlineData("RRULE:FREQ=DAILY;INTERVAL=3")]
    [InlineData("RRULE:FREQ=WEEKLY;BYDAY=TU")]
    [InlineData("RRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR")]
    [InlineData("RRULE:FREQ=MONTHLY;BYMONTHDAY=16")]
    [InlineData("RRULE:FREQ=MONTHLY;BYDAY=3TU")]
    [InlineData("RRULE:FREQ=YEARLY;BYMONTH=6;BYMONTHDAY=16")]
    [InlineData("RRULE:FREQ=DAILY;COUNT=10")]
    [InlineData("RRULE:FREQ=WEEKLY;BYDAY=MO,FR;COUNT=5")]
    [InlineData("RRULE:FREQ=DAILY;UNTIL=20261231T235959Z")]
    public void FromRecurrenceRule_RoundTrips(string rrule)
    {
        var model = RecurrencePickerModel.FromRecurrenceRule(rrule, Start);

        model.ToRecurrenceRule().Should().Be(rrule);
    }

    [Fact]
    public void FromRecurrenceRule_Null_IsDoesNotRepeat()
    {
        var model = RecurrencePickerModel.FromRecurrenceRule(null, Start);

        model.Mode.Should().Be(RecurrenceMode.DoesNotRepeat);
        model.ToRecurrenceRule().Should().BeNull();
    }

    [Fact]
    public void FromRecurrenceRule_DailyEveryThreeDays_IsCustomBecauseIntervalIsNotOne()
    {
        var model = RecurrencePickerModel.FromRecurrenceRule("RRULE:FREQ=DAILY;INTERVAL=3", Start);

        // A non-default interval cannot be represented by a compact preset, so the picker
        // surfaces it via the Custom drawer.
        model.Mode.Should().Be(RecurrenceMode.Custom);
        model.Frequency.Should().Be(RecurrenceFrequency.Daily);
        model.Interval.Should().Be(3);
    }

    // --- Guards ---------------------------------------------------------------------------

    [Fact]
    public void ToRecurrenceRule_CountLessThanOne_Throws()
    {
        var model = new RecurrencePickerModel(Start)
        {
            Mode = RecurrenceMode.Daily,
            EndMode = RecurrenceEndMode.Count,
            Count = 0
        };

        var act = () => model.ToRecurrenceRule();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToRecurrenceRule_IntervalLessThanOne_Throws()
    {
        var model = new RecurrencePickerModel(Start)
        {
            Mode = RecurrenceMode.Custom,
            Frequency = RecurrenceFrequency.Daily,
            Interval = 0
        };

        var act = () => model.ToRecurrenceRule();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToRecurrenceRule_CustomWeeklyWithNoWeekdaysSelected_FallsBackToStartWeekday()
    {
        var model = new RecurrencePickerModel(Start)
        {
            Mode = RecurrenceMode.Custom,
            Frequency = RecurrenceFrequency.Weekly
        };
        model.Weekdays.Clear();

        // An empty weekly selection is invalid for the user, but the model must never emit a
        // BYDAY-less weekly that silently drifts; it anchors to the start weekday instead.
        model.ToRecurrenceRule().Should().Be("RRULE:FREQ=WEEKLY;BYDAY=TU");
    }

    // --- Ordinal-weekday clamp (start on the 5th weekday of the month) ---------------------

    [Fact]
    public void Constructor_StartOnFifthWeekdayOfMonth_SeedsOrdinalFiveAndEmitsByDay5()
    {
        // 2026-01-29 is day 29 => ordinal (29-1)/7 + 1 = 5 (the 5th of its weekday in January).
        // The clamp to 5 is exercised; the weekday code is derived from the start, so assert the
        // ordinal and the BYDAY-5 prefix rather than hard-coding the weekday.
        var lateInMonth = new DateTimeOffset(2026, 1, 29, 9, 0, 0, TimeSpan.Zero);

        new RecurrencePickerModel(lateInMonth).OrdinalWeekday.Ordinal.Should().Be(5);

        var model = new RecurrencePickerModel(lateInMonth)
        {
            Mode = RecurrenceMode.Custom,
            Frequency = RecurrenceFrequency.Monthly,
            MonthlyMode = MonthlyMode.ByOrdinalWeekday
        };

        model.ToRecurrenceRule().Should().StartWith("RRULE:FREQ=MONTHLY;BYDAY=5");
    }

    // --- Preset classification carries the end condition ----------------------------------

    [Fact]
    public void FromRecurrenceRule_DefaultDailyWithCount_StillClassifiesAsDailyPreset()
    {
        // A start-seeded default anchor with only an end condition added must surface as the
        // compact preset (end carried through MatchingPresetMode), not forced into Custom.
        var model = RecurrencePickerModel.FromRecurrenceRule("RRULE:FREQ=DAILY;COUNT=10", Start);

        model.Mode.Should().Be(RecurrenceMode.Daily);
        model.EndMode.Should().Be(RecurrenceEndMode.Count);
        model.Count.Should().Be(10);
    }

    [Fact]
    public void FromRecurrenceRule_WeeklyOnStartWeekdayWithUntil_ClassifiesAsWeeklyPreset()
    {
        // Start is a Tuesday; a single-Tuesday weekly with an UNTIL is the Weekly preset's
        // seeded anchor plus an end — it must read as the Weekly preset, not Custom.
        var model = RecurrencePickerModel.FromRecurrenceRule(
            "RRULE:FREQ=WEEKLY;BYDAY=TU;UNTIL=20261231T235959Z", Start);

        model.Mode.Should().Be(RecurrenceMode.Weekly);
        model.EndMode.Should().Be(RecurrenceEndMode.Until);
    }

    // --- Unset (repeats toggled on, no frequency chosen yet) -------------------------------

    [Fact]
    public void ToRecurrenceRule_Unset_ReturnsNull()
    {
        var model = new RecurrencePickerModel(Start) { Mode = RecurrenceMode.Unset };

        model.ToRecurrenceRule().Should().BeNull();
    }

    [Fact]
    public void IsSelectionComplete_Unset_IsFalse()
    {
        var model = new RecurrencePickerModel(Start) { Mode = RecurrenceMode.Unset };

        model.IsSelectionComplete.Should().BeFalse();
    }

    [Theory]
    [InlineData(RecurrenceMode.DoesNotRepeat)]
    [InlineData(RecurrenceMode.Daily)]
    [InlineData(RecurrenceMode.Weekly)]
    [InlineData(RecurrenceMode.Custom)]
    public void IsSelectionComplete_NonUnset_IsTrue(RecurrenceMode mode)
    {
        var model = new RecurrencePickerModel(Start) { Mode = mode };

        model.IsSelectionComplete.Should().BeTrue();
    }
}
