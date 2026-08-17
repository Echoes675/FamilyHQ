using FamilyHQ.Core.Calendar.Recurrence;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// FHQ-161 — the recurrence engine must anchor a rule to the SERIES' time zone and hold its WALL
/// CLOCK across a DST transition, which is what Google does. Enumerating at fixed UTC instants
/// instead silently shifts every occurrence after a transition by an hour.
///
/// All dates use <c>Europe/London</c>, whose 2026 transitions are 29 March (BST starts, offset
/// +00:00 → +01:00) and 25 October (BST ends, +01:00 → +00:00); in 2027 they are 28 March and
/// 31 October. Expectations below are the instants Google would emit, stated explicitly rather than
/// derived from the engine.
/// </summary>
public class RecurrenceRuleBuilderZoneAnchorTests
{
    private const string London = "Europe/London";

    private static IRecurrenceTimeZone LondonZone() =>
        new NodaTimeRecurrenceTimeZoneFactory().TryCreate(London)
        ?? throw new InvalidOperationException($"tzdb has no zone '{London}'.");

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    // ── Weekly: the shape the E2E scenarios and the ticket's worked example exercise ───────────

    [Fact]
    public void Expand_WeeklySpanningTheAutumnTransition_HoldsLocalWallClockAndMovesTheUtcInstant()
    {
        // Tuesday 19:00 Europe/London from Tue 13 Oct 2026; the clocks go back Sun 25 Oct.
        var zone = LondonZone();
        var seriesStart = Utc(2026, 10, 13, 18, 0); // 19:00 BST

        var occurrences = RecurrenceRuleBuilder
            .Expand("RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=5", seriesStart,
                Utc(2026, 10, 1, 0, 0), Utc(2026, 12, 1, 0, 0), zone)
            .ToList();

        // The UTC instant MOVES across the transition — occurrences 3-5 are an hour later than a
        // fixed-instant expansion would place them (this is the ticket's table).
        occurrences.Should().Equal(
            Utc(2026, 10, 13, 18, 0),
            Utc(2026, 10, 20, 18, 0),
            Utc(2026, 10, 27, 19, 0),
            Utc(2026, 11, 3, 19, 0),
            Utc(2026, 11, 10, 19, 0));

        // …precisely so the LOCAL wall clock does not.
        occurrences.Select(o => zone.ToWallClock(o).TimeOfDay)
            .Should().AllBeEquivalentTo(TimeSpan.FromHours(19));
    }

    [Fact]
    public void Expand_WeeklySpanningTheSpringTransition_HoldsLocalWallClockAndMovesTheUtcInstant()
    {
        // Tuesday 19:00 from Tue 17 Mar 2026; the clocks go forward Sun 29 Mar.
        var zone = LondonZone();
        var seriesStart = Utc(2026, 3, 17, 19, 0); // 19:00 GMT

        var occurrences = RecurrenceRuleBuilder
            .Expand("RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=5", seriesStart,
                Utc(2026, 3, 1, 0, 0), Utc(2026, 5, 1, 0, 0), zone)
            .ToList();

        occurrences.Should().Equal(
            Utc(2026, 3, 17, 19, 0),
            Utc(2026, 3, 24, 19, 0),
            Utc(2026, 3, 31, 18, 0),
            Utc(2026, 4, 7, 18, 0),
            Utc(2026, 4, 14, 18, 0));

        occurrences.Select(o => zone.ToWallClock(o).TimeOfDay)
            .Should().AllBeEquivalentTo(TimeSpan.FromHours(19));
    }

    // ── The other three frequencies are affected identically ──────────────────────────────────

    [Fact]
    public void Expand_DailySpanningTheAutumnTransition_HoldsLocalWallClock()
    {
        var zone = LondonZone();
        var seriesStart = Utc(2026, 10, 24, 18, 0); // Sat 24 Oct, 19:00 BST

        var occurrences = RecurrenceRuleBuilder
            .Expand("RRULE:FREQ=DAILY;COUNT=4", seriesStart,
                Utc(2026, 10, 1, 0, 0), Utc(2026, 11, 1, 0, 0), zone)
            .ToList();

        occurrences.Should().Equal(
            Utc(2026, 10, 24, 18, 0),
            Utc(2026, 10, 25, 19, 0),
            Utc(2026, 10, 26, 19, 0),
            Utc(2026, 10, 27, 19, 0));

        occurrences.Select(o => zone.ToWallClock(o).TimeOfDay)
            .Should().AllBeEquivalentTo(TimeSpan.FromHours(19));
    }

    [Fact]
    public void Expand_MonthlyByDateSpanningTheAutumnTransition_HoldsLocalWallClock()
    {
        var zone = LondonZone();
        var seriesStart = Utc(2026, 9, 28, 18, 0); // Mon 28 Sep, 19:00 BST

        var occurrences = RecurrenceRuleBuilder
            .Expand("RRULE:FREQ=MONTHLY;BYMONTHDAY=28;COUNT=3", seriesStart,
                Utc(2026, 9, 1, 0, 0), Utc(2026, 12, 31, 0, 0), zone)
            .ToList();

        occurrences.Should().Equal(
            Utc(2026, 9, 28, 18, 0),
            Utc(2026, 10, 28, 19, 0),
            Utc(2026, 11, 28, 19, 0));

        occurrences.Select(o => zone.ToWallClock(o).TimeOfDay)
            .Should().AllBeEquivalentTo(TimeSpan.FromHours(19));
    }

    [Fact]
    public void Expand_MonthlyByOrdinalWeekdaySpanningTheAutumnTransition_HoldsLocalWallClock()
    {
        // Second Tuesday of each month: 8 Sep and 13 Oct are BST, 10 Nov is GMT.
        var zone = LondonZone();
        var seriesStart = Utc(2026, 9, 8, 18, 0); // 19:00 BST

        var occurrences = RecurrenceRuleBuilder
            .Expand("RRULE:FREQ=MONTHLY;BYDAY=2TU;COUNT=3", seriesStart,
                Utc(2026, 9, 1, 0, 0), Utc(2026, 12, 31, 0, 0), zone)
            .ToList();

        occurrences.Should().Equal(
            Utc(2026, 9, 8, 18, 0),
            Utc(2026, 10, 13, 18, 0),
            Utc(2026, 11, 10, 19, 0));

        occurrences.Select(o => zone.ToWallClock(o).TimeOfDay)
            .Should().AllBeEquivalentTo(TimeSpan.FromHours(19));
    }

    [Fact]
    public void Expand_YearlySpanningTheAutumnTransition_HoldsLocalWallClock()
    {
        // 28 October falls AFTER the 2026 transition (25 Oct) but BEFORE the 2027 one (31 Oct), so
        // consecutive yearly occurrences sit on opposite sides of the offset change.
        var zone = LondonZone();
        var seriesStart = Utc(2026, 10, 28, 19, 0); // 19:00 GMT

        var occurrences = RecurrenceRuleBuilder
            .Expand("RRULE:FREQ=YEARLY;BYMONTH=10;BYMONTHDAY=28;COUNT=2", seriesStart,
                Utc(2026, 1, 1, 0, 0), Utc(2028, 1, 1, 0, 0), zone)
            .ToList();

        occurrences.Should().Equal(
            Utc(2026, 10, 28, 19, 0),
            Utc(2027, 10, 28, 18, 0));

        occurrences.Select(o => zone.ToWallClock(o).TimeOfDay)
            .Should().AllBeEquivalentTo(TimeSpan.FromHours(19));
    }

    // ── Transition edge cases: the local reading is ambiguous or does not exist ────────────────

    [Fact]
    public void Expand_DailyThroughTheSpringForwardGap_EmitsEveryOccurrenceShiftedPastTheGap()
    {
        // 01:30 on 29 Mar 2026 does not exist in Europe/London (01:00 → 02:00). The occurrence must
        // shift forward past the gap rather than be dropped or throw.
        var zone = LondonZone();
        var seriesStart = Utc(2026, 3, 28, 1, 30); // 01:30 GMT

        var occurrences = RecurrenceRuleBuilder
            .Expand("RRULE:FREQ=DAILY;COUNT=3", seriesStart,
                Utc(2026, 3, 1, 0, 0), Utc(2026, 4, 1, 0, 0), zone)
            .ToList();

        occurrences.Should().Equal(
            Utc(2026, 3, 28, 1, 30),  // 01:30 GMT
            Utc(2026, 3, 29, 1, 30),  // skipped 01:30 → 02:30 BST
            Utc(2026, 3, 30, 0, 30)); // 01:30 BST

        occurrences.Should().BeInAscendingOrder();
        zone.ToWallClock(occurrences[1]).TimeOfDay.Should().Be(TimeSpan.FromHours(2.5));
    }

    [Fact]
    public void Expand_DailyThroughTheAutumnAmbiguousHour_ResolvesToTheEarlierInstant()
    {
        // 01:30 on 25 Oct 2026 happens TWICE in Europe/London (02:00 BST → 01:00 GMT). RFC 5545 /
        // Google take the first, so the occurrence stays on the pre-transition offset.
        var zone = LondonZone();
        var seriesStart = Utc(2026, 10, 24, 0, 30); // 01:30 BST

        var occurrences = RecurrenceRuleBuilder
            .Expand("RRULE:FREQ=DAILY;COUNT=3", seriesStart,
                Utc(2026, 10, 1, 0, 0), Utc(2026, 11, 1, 0, 0), zone)
            .ToList();

        occurrences.Should().Equal(
            Utc(2026, 10, 24, 0, 30), // 01:30 BST
            Utc(2026, 10, 25, 0, 30), // ambiguous 01:30 → the EARLIER (BST) instant
            Utc(2026, 10, 26, 1, 30)); // 01:30 GMT

        occurrences.Should().BeInAscendingOrder();
    }

    // ── The unknown/absent-zone fallback is deliberate, and pinned ────────────────────────────

    [Fact]
    public void Expand_WithNoZone_StepsFixedUtcInstantsAndShiftsTheLocalWallClock()
    {
        // Documented fallback (FixedOffsetRecurrenceTimeZone): a caller that supplies no series zone
        // gets EXACTLY the pre-FHQ-161 fixed-UTC enumeration. That is exact for date-anchored all-day
        // series and NOT DST-aware for anything else — production logs a Warning when it lands here.
        var zone = LondonZone();
        var seriesStart = Utc(2026, 10, 13, 18, 0);

        var occurrences = RecurrenceRuleBuilder
            .Expand("RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=5", seriesStart,
                Utc(2026, 10, 1, 0, 0), Utc(2026, 12, 1, 0, 0), zone: null)
            .ToList();

        occurrences.Should().Equal(
            Utc(2026, 10, 13, 18, 0),
            Utc(2026, 10, 20, 18, 0),
            Utc(2026, 10, 27, 18, 0),
            Utc(2026, 11, 3, 18, 0),
            Utc(2026, 11, 10, 18, 0));

        // The instant is held, so the LOCAL reading slips by an hour after the transition.
        occurrences.Select(o => zone.ToWallClock(o).TimeOfDay).Should().Equal(
            TimeSpan.FromHours(19), TimeSpan.FromHours(19),
            TimeSpan.FromHours(18), TimeSpan.FromHours(18), TimeSpan.FromHours(18));
    }

    // ── CountOccurrencesBefore: the production "this and following" split seam ─────────────────

    [Fact]
    public void CountOccurrencesBefore_SplitAfterAnAutumnTransition_ExcludesTheSplitOccurrencesOwnTwin()
    {
        // The ticket's worked example. Weekly Tue 19:00 London, COUNT=5 from Tue 13 Oct 2026, split
        // at occurrence 4 (Tue 3 Nov, 19:00 GMT). Three occurrences precede it.
        var count = RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=5",
            Utc(2026, 10, 13, 18, 0),
            Utc(2026, 11, 3, 19, 0),
            LondonZone());

        count.Should().Be(3);
    }

    [Fact]
    public void CountOccurrencesBefore_SplitAfterAnAutumnTransitionWithNoZone_OverCountsByOne()
    {
        // Pins the fallback's known limitation: without the zone the enumerated twin of the split
        // occurrence lands an hour early (18:00Z < 19:00Z) and is wrongly counted as "before".
        var count = RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=5",
            Utc(2026, 10, 13, 18, 0),
            Utc(2026, 11, 3, 19, 0),
            zone: null);

        count.Should().Be(4);
    }

    [Fact]
    public void CountOccurrencesBefore_SplitAfterASpringTransition_IsUnaffected()
    {
        // The spring transition is benign for this path — the twin lands an hour LATE and is
        // correctly excluded — so the zone-aware and zone-less counts agree. Pinned so a future
        // change cannot silently break the direction that already worked.
        const string Rule = "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=5";
        var anchor = Utc(2026, 3, 17, 19, 0);   // Tue 17 Mar, 19:00 GMT
        var split = Utc(2026, 4, 7, 18, 0);     // occurrence 4, 19:00 BST

        RecurrenceRuleBuilder.CountOccurrencesBefore(Rule, anchor, split, LondonZone()).Should().Be(3);
        RecurrenceRuleBuilder.CountOccurrencesBefore(Rule, anchor, split, zone: null).Should().Be(3);
    }

    // ── Termination guards still hold with a zone injected ────────────────────────────────────

    [Fact]
    public void Expand_ZoneAnchoredUnboundedRule_StaysWithinTheHardCap()
    {
        var occurrences = RecurrenceRuleBuilder
            .Expand("RRULE:FREQ=DAILY", Utc(2026, 1, 1, 9, 0),
                Utc(2026, 1, 1, 0, 0), Utc(4000, 1, 1, 0, 0), LondonZone())
            .Count();

        occurrences.Should().Be(RecurrenceRuleBuilder.MaxEnumeratedOccurrences);
    }

    [Fact]
    public async Task CountOccurrencesBefore_ZoneAnchoredMonthlyNeverEmittingRule_TerminatesWithinCap()
    {
        // BYMONTHDAY=31 with INTERVAL=12 anchored on a 30-day month never emits, so the loop must be
        // bounded on PERIODS ADVANCED. Injecting a zone must not defeat that guard.
        var work = Task.Run(() => RecurrenceRuleBuilder.CountOccurrencesBefore(
            "RRULE:FREQ=MONTHLY;BYMONTHDAY=31;INTERVAL=12",
            Utc(2026, 4, 30, 9, 0),
            Utc(3000, 1, 1, 0, 0),
            LondonZone()));

        var finished = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(10)));

        finished.Should().BeSameAs(work, "a never-emitting rule must terminate on the period cap");
        (await work).Should().Be(0);
    }
}
