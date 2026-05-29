using System.Globalization;
using FamilyHQ.Services.Calendar.Recurrence;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class RecurrenceRuleBuilderTests
{
    private static readonly DateTimeOffset UntilMoment =
        new(2026, 6, 12, 23, 59, 59, TimeSpan.Zero);

    // --- ToRRuleString: daily ---

    [Fact]
    public void ToRRuleString_DailyEveryDay_EmitsFreqOnly()
    {
        var spec = new RecurrenceSpec { Frequency = RecurrenceFrequency.Daily };

        RecurrenceRuleBuilder.ToRRuleString(spec).Should().Be("RRULE:FREQ=DAILY");
    }

    [Fact]
    public void ToRRuleString_DailyEveryThreeDays_EmitsInterval()
    {
        var spec = new RecurrenceSpec { Frequency = RecurrenceFrequency.Daily, Interval = 3 };

        RecurrenceRuleBuilder.ToRRuleString(spec).Should().Be("RRULE:FREQ=DAILY;INTERVAL=3");
    }

    // --- ToRRuleString: weekly ---

    [Fact]
    public void ToRRuleString_WeeklyOnTuesday_EmitsByDay()
    {
        var spec = new RecurrenceSpec
        {
            Frequency = RecurrenceFrequency.Weekly,
            ByDay = [DayOfWeek.Tuesday]
        };

        RecurrenceRuleBuilder.ToRRuleString(spec).Should().Be("RRULE:FREQ=WEEKLY;BYDAY=TU");
    }

    [Fact]
    public void ToRRuleString_WeeklyNoByDay_EmitsFreqOnly()
    {
        var spec = new RecurrenceSpec { Frequency = RecurrenceFrequency.Weekly };

        RecurrenceRuleBuilder.ToRRuleString(spec).Should().Be("RRULE:FREQ=WEEKLY");
    }

    [Fact]
    public void ToRRuleString_WeeklyEveryTwoWeeksOnMonWedFri_EmitsIntervalAndByDay()
    {
        var spec = new RecurrenceSpec
        {
            Frequency = RecurrenceFrequency.Weekly,
            Interval = 2,
            ByDay = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]
        };

        RecurrenceRuleBuilder.ToRRuleString(spec)
            .Should().Be("RRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR");
    }

    // --- ToRRuleString: monthly ---

    [Fact]
    public void ToRRuleString_MonthlyByDate_EmitsByMonthDay()
    {
        var spec = new RecurrenceSpec
        {
            Frequency = RecurrenceFrequency.Monthly,
            MonthlyMode = MonthlyMode.ByDate,
            ByMonthDay = 15
        };

        RecurrenceRuleBuilder.ToRRuleString(spec).Should().Be("RRULE:FREQ=MONTHLY;BYMONTHDAY=15");
    }

    [Fact]
    public void ToRRuleString_MonthlyBySecondTuesday_EmitsOrdinalByDay()
    {
        var spec = new RecurrenceSpec
        {
            Frequency = RecurrenceFrequency.Monthly,
            MonthlyMode = MonthlyMode.ByOrdinalWeekday,
            OrdinalWeekday = new OrdinalWeekday(2, DayOfWeek.Tuesday)
        };

        RecurrenceRuleBuilder.ToRRuleString(spec).Should().Be("RRULE:FREQ=MONTHLY;BYDAY=2TU");
    }

    [Fact]
    public void ToRRuleString_MonthlyByLastFriday_EmitsNegativeOrdinalByDay()
    {
        var spec = new RecurrenceSpec
        {
            Frequency = RecurrenceFrequency.Monthly,
            MonthlyMode = MonthlyMode.ByOrdinalWeekday,
            OrdinalWeekday = new OrdinalWeekday(-1, DayOfWeek.Friday)
        };

        RecurrenceRuleBuilder.ToRRuleString(spec).Should().Be("RRULE:FREQ=MONTHLY;BYDAY=-1FR");
    }

    // --- ToRRuleString: yearly ---

    [Fact]
    public void ToRRuleString_YearlyByMonthAndDay_EmitsByMonthAndByMonthDay()
    {
        var spec = new RecurrenceSpec
        {
            Frequency = RecurrenceFrequency.Yearly,
            ByMonth = 6,
            ByMonthDay = 12
        };

        RecurrenceRuleBuilder.ToRRuleString(spec).Should().Be("RRULE:FREQ=YEARLY;BYMONTH=6;BYMONTHDAY=12");
    }

    // --- ToRRuleString: end conditions ---

    [Fact]
    public void ToRRuleString_WithCount_EmitsCount()
    {
        var spec = new RecurrenceSpec
        {
            Frequency = RecurrenceFrequency.Daily,
            End = RecurrenceEnd.Count(8)
        };

        RecurrenceRuleBuilder.ToRRuleString(spec).Should().Be("RRULE:FREQ=DAILY;COUNT=8");
    }

    [Fact]
    public void ToRRuleString_WithUntil_EmitsUtcUntil()
    {
        var spec = new RecurrenceSpec
        {
            Frequency = RecurrenceFrequency.Weekly,
            ByDay = [DayOfWeek.Monday],
            End = RecurrenceEnd.Until(UntilMoment)
        };

        RecurrenceRuleBuilder.ToRRuleString(spec)
            .Should().Be("RRULE:FREQ=WEEKLY;BYDAY=MO;UNTIL=20260612T235959Z");
    }

    [Fact]
    public void ToRRuleString_NeverEmitsBothCountAndUntil()
    {
        // RecurrenceEnd is a closed type that can only hold one variant, so exclusivity
        // is guaranteed by construction. Verify the emitted string carries at most one.
        var withCount = RecurrenceRuleBuilder.ToRRuleString(
            new RecurrenceSpec { Frequency = RecurrenceFrequency.Daily, End = RecurrenceEnd.Count(3) });
        var withUntil = RecurrenceRuleBuilder.ToRRuleString(
            new RecurrenceSpec { Frequency = RecurrenceFrequency.Daily, End = RecurrenceEnd.Until(UntilMoment) });

        withCount.Should().Contain("COUNT=").And.NotContain("UNTIL=");
        withUntil.Should().Contain("UNTIL=").And.NotContain("COUNT=");
    }

    // --- Round-trip ---

    public static TheoryData<RecurrenceSpec> RoundTripSpecs() =>
    [
        new() { Frequency = RecurrenceFrequency.Daily },
        new() { Frequency = RecurrenceFrequency.Daily, Interval = 5 },
        new() { Frequency = RecurrenceFrequency.Daily, End = RecurrenceEnd.Count(10) },
        new() { Frequency = RecurrenceFrequency.Daily, End = RecurrenceEnd.Until(UntilMoment) },
        new() { Frequency = RecurrenceFrequency.Weekly },
        new() { Frequency = RecurrenceFrequency.Weekly, ByDay = [DayOfWeek.Tuesday] },
        new()
        {
            Frequency = RecurrenceFrequency.Weekly,
            Interval = 2,
            ByDay = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
            End = RecurrenceEnd.Count(6)
        },
        new()
        {
            Frequency = RecurrenceFrequency.Weekly,
            ByDay = [DayOfWeek.Sunday],
            End = RecurrenceEnd.Until(UntilMoment)
        },
        new()
        {
            Frequency = RecurrenceFrequency.Monthly,
            MonthlyMode = MonthlyMode.ByDate,
            ByMonthDay = 15
        },
        new()
        {
            Frequency = RecurrenceFrequency.Monthly,
            Interval = 3,
            MonthlyMode = MonthlyMode.ByDate,
            ByMonthDay = 1,
            End = RecurrenceEnd.Count(4)
        },
        new()
        {
            Frequency = RecurrenceFrequency.Monthly,
            MonthlyMode = MonthlyMode.ByOrdinalWeekday,
            OrdinalWeekday = new OrdinalWeekday(2, DayOfWeek.Tuesday)
        },
        new()
        {
            Frequency = RecurrenceFrequency.Monthly,
            MonthlyMode = MonthlyMode.ByOrdinalWeekday,
            OrdinalWeekday = new OrdinalWeekday(-1, DayOfWeek.Friday),
            End = RecurrenceEnd.Until(UntilMoment)
        },
        new() { Frequency = RecurrenceFrequency.Yearly, ByMonth = 6, ByMonthDay = 12 },
        new()
        {
            Frequency = RecurrenceFrequency.Yearly,
            Interval = 2,
            ByMonth = 12,
            ByMonthDay = 25,
            End = RecurrenceEnd.Count(3)
        }
    ];

    [Theory]
    [MemberData(nameof(RoundTripSpecs))]
    public void ParseRRuleString_RoundTripsGeneratedRRule(RecurrenceSpec spec)
    {
        var rrule = RecurrenceRuleBuilder.ToRRuleString(spec);

        var parsed = RecurrenceRuleBuilder.ParseRRuleString(rrule);

        parsed.Should().Be(spec);
    }

    // --- Tolerance of Google-style input ---

    [Fact]
    public void ParseRRuleString_NoRRulePrefix_Parses()
    {
        var parsed = RecurrenceRuleBuilder.ParseRRuleString("FREQ=WEEKLY;BYDAY=TU");

        parsed.Frequency.Should().Be(RecurrenceFrequency.Weekly);
        parsed.ByDay.Should().Equal(DayOfWeek.Tuesday);
    }

    [Fact]
    public void ParseRRuleString_IgnoresUnknownPartsWkstAndBySetPos()
    {
        var parsed = RecurrenceRuleBuilder.ParseRRuleString(
            "RRULE:FREQ=MONTHLY;BYDAY=2TU;BYSETPOS=2;WKST=SU;BYHOUR=9");

        parsed.Frequency.Should().Be(RecurrenceFrequency.Monthly);
        parsed.MonthlyMode.Should().Be(MonthlyMode.ByOrdinalWeekday);
        parsed.OrdinalWeekday.Should().Be(new OrdinalWeekday(2, DayOfWeek.Tuesday));
    }

    [Fact]
    public void ParseRRuleString_DateOnlyUntil_Parses()
    {
        var parsed = RecurrenceRuleBuilder.ParseRRuleString("FREQ=DAILY;UNTIL=20260612");

        parsed.End.Kind.Should().Be(RecurrenceEndKind.Until);
        parsed.End.UntilUtc!.Value.Should().Be(new DateTimeOffset(2026, 6, 12, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ParseRRuleString_LowercaseInput_Parses()
    {
        var parsed = RecurrenceRuleBuilder.ParseRRuleString("rrule:freq=weekly;byday=mo,we");

        parsed.Frequency.Should().Be(RecurrenceFrequency.Weekly);
        parsed.ByDay.Should().Equal(DayOfWeek.Monday, DayOfWeek.Wednesday);
    }

    [Fact]
    public void ParseRRuleString_GoogleStyleFullUntilWithWkst_DoesNotThrow()
    {
        var act = () => RecurrenceRuleBuilder.ParseRRuleString(
            "RRULE:FREQ=WEEKLY;WKST=MO;UNTIL=20261231T000000Z;BYDAY=MO,TU,WE,TH,FR");

        act.Should().NotThrow();
    }

    // --- Invalid input ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseRRuleString_EmptyInput_Throws(string input)
    {
        var act = () => RecurrenceRuleBuilder.ParseRRuleString(input);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ParseRRuleString_MissingFreq_Throws()
    {
        var act = () => RecurrenceRuleBuilder.ParseRRuleString("RRULE:INTERVAL=2;BYDAY=TU");

        act.Should().Throw<ArgumentException>().WithMessage("*FREQ*");
    }

    [Fact]
    public void ParseRRuleString_InvalidFreq_Throws()
    {
        var act = () => RecurrenceRuleBuilder.ParseRRuleString("RRULE:FREQ=FORTNIGHTLY");

        act.Should().Throw<ArgumentException>().WithMessage("*FREQ*");
    }

    // --- Validation invariants ---

    [Fact]
    public void ToRRuleString_IntervalLessThanOne_Throws()
    {
        var spec = new RecurrenceSpec { Frequency = RecurrenceFrequency.Daily, Interval = 0 };

        var act = () => RecurrenceRuleBuilder.ToRRuleString(spec);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RecurrenceEnd_CountLessThanOne_Throws()
    {
        var act = () => RecurrenceEnd.Count(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void OrdinalWeekday_ZeroOrdinal_Throws()
    {
        var act = () => new OrdinalWeekday(0, DayOfWeek.Monday);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // --- Describe: required exact outputs ---

    [Theory]
    [InlineData("RRULE:FREQ=WEEKLY;BYDAY=TU", "Repeats weekly on Tuesday")]
    [InlineData("RRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR", "Repeats every 2 weeks on Mon, Wed, Fri")]
    [InlineData("RRULE:FREQ=MONTHLY;BYMONTHDAY=15", "Repeats monthly on the 15th")]
    [InlineData("RRULE:FREQ=YEARLY;BYMONTH=6;BYMONTHDAY=12", "Repeats yearly on June 12")]
    public void Describe_RequiredCases_ReturnExactStrings(string rrule, string expected)
    {
        RecurrenceRuleBuilder.Describe(rrule).Should().Be(expected);
    }

    // --- Describe: additional phrasings ---

    [Theory]
    [InlineData("RRULE:FREQ=DAILY", "Repeats daily")]
    [InlineData("RRULE:FREQ=DAILY;INTERVAL=3", "Repeats every 3 days")]
    [InlineData("RRULE:FREQ=MONTHLY;BYDAY=2TU", "Repeats monthly on the second Tuesday")]
    [InlineData("RRULE:FREQ=MONTHLY;BYDAY=-1FR", "Repeats monthly on the last Friday")]
    [InlineData("RRULE:FREQ=MONTHLY;BYMONTHDAY=1", "Repeats monthly on the 1st")]
    [InlineData("RRULE:FREQ=MONTHLY;BYMONTHDAY=3", "Repeats monthly on the 3rd")]
    [InlineData("RRULE:FREQ=MONTHLY;BYMONTHDAY=21", "Repeats monthly on the 21st")]
    [InlineData("RRULE:FREQ=MONTHLY;BYMONTHDAY=23", "Repeats monthly on the 23rd")]
    public void Describe_AdditionalPhrasings_ReturnExpected(string rrule, string expected)
    {
        RecurrenceRuleBuilder.Describe(rrule).Should().Be(expected);
    }

    [Fact]
    public void Describe_WithCount_AppendsTimes()
    {
        RecurrenceRuleBuilder.Describe("RRULE:FREQ=DAILY;COUNT=8")
            .Should().Be("Repeats daily, 8 times");
    }

    [Fact]
    public void Describe_WithUntil_AppendsUntilDate()
    {
        RecurrenceRuleBuilder.Describe("RRULE:FREQ=WEEKLY;BYDAY=MO;UNTIL=20260612T235959Z")
            .Should().Be("Repeats weekly on Monday, until 12 June 2026");
    }

    // --- Describe: teens ordinal suffixes (11th, 12th, 13th) ---

    [Theory]
    [InlineData("RRULE:FREQ=MONTHLY;BYMONTHDAY=11", "Repeats monthly on the 11th")]
    [InlineData("RRULE:FREQ=MONTHLY;BYMONTHDAY=12", "Repeats monthly on the 12th")]
    [InlineData("RRULE:FREQ=MONTHLY;BYMONTHDAY=13", "Repeats monthly on the 13th")]
    public void Describe_TeensOrdinalSuffix_ReturnExpected(string rrule, string expected)
    {
        RecurrenceRuleBuilder.Describe(rrule).Should().Be(expected);
    }

    // --- Tolerance: malformed BYDAY weekday tokens are dropped, never throw (B1) ---

    [Fact]
    public void ParseRRuleString_WeeklyByDaySingleUnknownToken_DropsByDayAndDoesNotThrow()
    {
        var parsed = RecurrenceRuleBuilder.ParseRRuleString("RRULE:FREQ=WEEKLY;BYDAY=X");

        parsed.Frequency.Should().Be(RecurrenceFrequency.Weekly);
        parsed.ByDay.Should().BeNullOrEmpty();
    }

    [Fact]
    public void ParseRRuleString_WeeklyByDayWithUnknownTokenAmongValid_KeepsValidTokens()
    {
        var parsed = RecurrenceRuleBuilder.ParseRRuleString("RRULE:FREQ=WEEKLY;BYDAY=MO,X,WE");

        parsed.ByDay.Should().Equal(DayOfWeek.Monday, DayOfWeek.Wednesday);
    }

    [Fact]
    public void Describe_WeeklyByDaySingleUnknownToken_DegradesGracefully()
    {
        RecurrenceRuleBuilder.Describe("RRULE:FREQ=WEEKLY;BYDAY=X")
            .Should().Be("Repeats weekly");
    }

    // --- Tolerance: unmodelled monthly anchors are dropped, never throw (B2) ---

    [Fact]
    public void ParseRRuleString_MonthlyByMonthDayLastDay_DropsAnchorAndDoesNotThrow()
    {
        var parsed = RecurrenceRuleBuilder.ParseRRuleString("RRULE:FREQ=MONTHLY;BYMONTHDAY=-1");

        parsed.Frequency.Should().Be(RecurrenceFrequency.Monthly);
        parsed.ByMonthDay.Should().BeNull();
    }

    [Fact]
    public void Describe_MonthlyByMonthDayLastDay_DegradesToMonthly()
    {
        RecurrenceRuleBuilder.Describe("RRULE:FREQ=MONTHLY;BYMONTHDAY=-1")
            .Should().Be("Repeats monthly");
    }

    [Fact]
    public void ParseRRuleString_MonthlyByDayOrdinalOutOfRange_DropsAnchorAndDoesNotThrow()
    {
        var parsed = RecurrenceRuleBuilder.ParseRRuleString("RRULE:FREQ=MONTHLY;BYDAY=11TH");

        parsed.Frequency.Should().Be(RecurrenceFrequency.Monthly);
        parsed.OrdinalWeekday.Should().BeNull();
    }

    [Fact]
    public void Describe_MonthlyByDayOrdinalOutOfRange_DegradesToMonthly()
    {
        RecurrenceRuleBuilder.Describe("RRULE:FREQ=MONTHLY;BYDAY=11TH")
            .Should().Be("Repeats monthly");
    }

    [Fact]
    public void ParseRRuleString_MonthlyByLastFriday_StillRoundTrips()
    {
        var parsed = RecurrenceRuleBuilder.ParseRRuleString("RRULE:FREQ=MONTHLY;BYDAY=-1FR");

        parsed.MonthlyMode.Should().Be(MonthlyMode.ByOrdinalWeekday);
        parsed.OrdinalWeekday.Should().Be(new OrdinalWeekday(-1, DayOfWeek.Friday));
        RecurrenceRuleBuilder.ToRRuleString(parsed).Should().Be("RRULE:FREQ=MONTHLY;BYDAY=-1FR");
    }

    // --- Culture independence (B3) ---

    [Fact]
    public void Describe_UnderGermanCulture_RemainsEnglish()
    {
        // Guards B3 (culture-dependent date formatting). Where a non-invariant culture exists we
        // switch to de-DE to actively exercise the leak; in globalization-invariant mode (e.g. the
        // CI container) de-DE can't be constructed, but Describe is invariant by construction there,
        // so we still assert the same English output without switching.
        CultureInfo? german = null;
        try
        {
            german = new CultureInfo("de-DE");
        }
        catch (CultureNotFoundException)
        {
            // Invariant-globalization mode: leave culture as-is.
        }

        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            if (german is not null)
            {
                CultureInfo.CurrentCulture = german;
                CultureInfo.CurrentUICulture = german;
            }

            RecurrenceRuleBuilder.Describe("RRULE:FREQ=WEEKLY;BYDAY=MO;UNTIL=20260612T235959Z")
                .Should().Be("Repeats weekly on Monday, until 12 June 2026");
            RecurrenceRuleBuilder.Describe("RRULE:FREQ=YEARLY;BYMONTH=6;BYMONTHDAY=12")
                .Should().Be("Repeats yearly on June 12");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    // --- CountOccurrencesBefore (FHQ-18.5 Part B) ---
    //
    // Counts occurrences of a rule, generated from dtStart, whose start instant falls STRICTLY
    // before the boundary. Used to compute the remaining COUNT for a "this and following" split:
    // the split instance becomes occurrence #1 of the forward series, so everything before it
    // stays in the truncated original. boundary is exclusive; an occurrence landing exactly on
    // the boundary is NOT counted (it is the split instance itself).

    private static readonly DateTimeOffset DailyStart =
        new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero); // a Monday

    [Fact]
    public void CountOccurrencesBefore_Daily_CountsDaysStrictlyBeforeBoundary()
    {
        // Occurrences: Mar 2, 3, 4, 5, ... The boundary is Mar 5 09:00 → Mar 2,3,4 are before it.
        var count = RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=DAILY", DailyStart, DailyStart.AddDays(3));

        count.Should().Be(3);
    }

    [Fact]
    public void CountOccurrencesBefore_BoundaryEqualsAnOccurrence_ExcludesThatOccurrence()
    {
        // boundary exactly on the start → zero occurrences strictly before it.
        var count = RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=DAILY", DailyStart, DailyStart);

        count.Should().Be(0);
    }

    [Fact]
    public void CountOccurrencesBefore_DailyWithInterval_RespectsInterval()
    {
        // Every 2 days from Mar 2: Mar 2, 4, 6, 8, ... boundary Mar 8 09:00 → Mar 2,4,6 before it.
        var count = RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=DAILY;INTERVAL=2", DailyStart, DailyStart.AddDays(6));

        count.Should().Be(3);
    }

    [Fact]
    public void CountOccurrencesBefore_DailyCountCapsEnumeration()
    {
        // COUNT=4 → only 4 occurrences exist (Mar 2,3,4,5). A boundary far in the future counts all 4.
        var count = RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=DAILY;COUNT=4", DailyStart, DailyStart.AddYears(1));

        count.Should().Be(4);
    }

    [Fact]
    public void CountOccurrencesBefore_Weekly_CountsWeeksBeforeBoundary()
    {
        // FREQ=WEEKLY anchored to its start day (Monday): Mar 2, 9, 16, 23 ...
        // boundary Mar 23 → Mar 2, 9, 16 before it.
        var count = RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=WEEKLY", DailyStart, DailyStart.AddDays(21));

        count.Should().Be(3);
    }

    [Fact]
    public void CountOccurrencesBefore_WeeklyByDay_CountsEachMatchingWeekday()
    {
        // FREQ=WEEKLY;BYDAY=MO,WE,FR from Mon Mar 2.
        // Occurrences: Mon Mar 2, Wed Mar 4, Fri Mar 6, Mon Mar 9, Wed Mar 11, ...
        // boundary Mar 9 09:00 → Mar 2, 4, 6 before it (Mar 9 excluded).
        var count = RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR", DailyStart, DailyStart.AddDays(7));

        count.Should().Be(3);
    }

    [Fact]
    public void CountOccurrencesBefore_MonthlyByDate_CountsMonthsBeforeBoundary()
    {
        // FREQ=MONTHLY;BYMONTHDAY=2 from Mar 2: Mar 2, Apr 2, May 2, Jun 2 ...
        // boundary Jun 2 → Mar, Apr, May before it.
        var count = RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=MONTHLY;BYMONTHDAY=2", DailyStart, DailyStart.AddMonths(3));

        count.Should().Be(3);
    }

    [Fact]
    public void CountOccurrencesBefore_MonthlyByOrdinalWeekday_CountsFirstMondayEachMonth()
    {
        // FREQ=MONTHLY;BYDAY=1MO (first Monday). From Mar 2026 (1st Monday = Mar 2):
        // Mar 2, Apr 6, May 4, Jun 1 ... boundary = May 4 09:00 → Mar 2, Apr 6 before it.
        var firstMonMarch = new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);
        var firstMonMay = new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero);

        var count = RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=MONTHLY;BYDAY=1MO", firstMonMarch, firstMonMay);

        count.Should().Be(2);
    }

    [Fact]
    public void CountOccurrencesBefore_Yearly_CountsYearsBeforeBoundary()
    {
        // FREQ=YEARLY;BYMONTH=3;BYMONTHDAY=2 from Mar 2 2026: 2026, 2027, 2028 ...
        // boundary Mar 2 2028 → 2026, 2027 before it.
        var count = RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=YEARLY;BYMONTH=3;BYMONTHDAY=2", DailyStart, DailyStart.AddYears(2));

        count.Should().Be(2);
    }

    [Fact]
    public void CountOccurrencesBefore_NeverRuleWithFarBoundary_IsCappedAndDoesNotHang()
    {
        // A Never rule with a boundary far enough out to exceed the defensive cap must terminate
        // and return the cap rather than loop forever.
        var count = RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=DAILY", DailyStart, DailyStart.AddYears(100));

        count.Should().BeLessThanOrEqualTo(10_000);
        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CountOccurrencesBefore_BoundaryAfterLastOccurrence_CountsAllOccurrences()
    {
        // COUNT=3 → only Mar 2, 3, 4 exist. A boundary at/after the last occurrence counts ALL three
        // (the enumeration is clamped at COUNT; nothing beyond it is generated). For a "this and
        // following" split this is the remaining<=0 case the caller (ReshapeRule) then rejects: the
        // builder itself clamps rather than throwing.
        var count = RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=DAILY;COUNT=3", DailyStart, DailyStart.AddDays(10));

        count.Should().Be(3);
    }

    [Fact]
    public async Task CountOccurrencesBefore_YearlyNeverEmittingRule_TerminatesWithinCap()
    {
        // FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=30 (February 30 never exists) produces NO occurrence in
        // any year. Without an iteration bound independent of the emitted count the enumerator would
        // advance the year forever (emitted never increments). It must terminate (returning 0) rather
        // than hang. The timeout race is the regression guard against the infinite loop (Major 4).
        var work = Task.Run(() => RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=YEARLY;BYMONTH=2;BYMONTHDAY=30", DailyStart, DailyStart.AddYears(5)));

        var finished = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(5)));
        finished.Should().BeSameAs(work, "a never-emitting rule must not loop forever");
        (await work).Should().Be(0);
    }

    [Fact]
    public async Task CountOccurrencesBefore_MonthlyNeverEmittingRule_TerminatesWithinCap()
    {
        // FREQ=MONTHLY;BYMONTHDAY=31;INTERVAL=2 anchored on an even (31-less) month never lands on a
        // 31-day month, so it emits nothing and would advance the anchor month forever. Anchor on
        // April (30 days): April→June→August... none of which the rule can satisfy at day 31 stepping
        // by 2 from a 30-day month (Apr, Jun, Aug, Oct, Dec, Feb — only Aug/Oct/Dec have 31, so it
        // DOES emit). Use the clean never-emit form instead: BYMONTHDAY=31 with INTERVAL=12 from a
        // 30-day month only ever revisits that same 30-day month → never emits.
        var aprilStart = new DateTimeOffset(2026, 4, 15, 9, 0, 0, TimeSpan.Zero);
        var work = Task.Run(() => RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=MONTHLY;BYMONTHDAY=31;INTERVAL=12", aprilStart, aprilStart.AddYears(5)));

        var finished = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(5)));
        finished.Should().BeSameAs(work, "a never-emitting monthly rule must terminate within the cap");
        (await work).Should().Be(0);
    }
}
