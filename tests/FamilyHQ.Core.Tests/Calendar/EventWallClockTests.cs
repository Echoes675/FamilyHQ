using FamilyHQ.Core.Calendar;
using FluentAssertions;

namespace FamilyHQ.Core.Tests.Calendar;

// The one place "which day is this event on" is answered. Both branches are pinned here because each
// is wrong in a way the other hides: converting an all-day instant through a zone reads a day early
// at any negative offset, and NOT converting a timed one reads the UTC day rather than the day the
// family is living in. A suite that only ever used non-negative offsets passed the first defect for
// as long as it existed.
public class EventWallClockTests
{
    // Real zones looked up by IANA id, not fixed offsets: only a real zone changes offset partway
    // through a day, which the transition fixture below needs. This resolves even though CI runs
    // globalization-invariant, as the reminders suites already rely on.
    private static readonly TimeZoneInfo Dublin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");
    private static readonly TimeZoneInfo Athens = TimeZoneInfo.FindSystemTimeZoneById("Europe/Athens");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeZoneInfo Honolulu = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Honolulu");

    // Midnight UTC on 15 June 2026 — exactly what GoogleAllDayDate.Parse produces for "2026-06-15",
    // and what the database hands back for it.
    private static readonly DateTimeOffset AllDayJune15 = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> EveryZone() => new() { "UTC", "Europe/Dublin", "Europe/Athens", "America/New_York", "Pacific/Honolulu" };

    [Theory]
    [MemberData(nameof(EveryZone))]
    public void Of_ForAnAllDayEvent_ReadsTheStoredUtcDate_WhateverTheZone(string zoneId)
    {
        // The two negative-offset cases are the ones nothing in this repo exercised before, and they
        // are where the defect lived: New York (-4 in June) and Honolulu (-10, no DST at all) both
        // read 14 June out of this instant if it is converted through the zone.
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);

        var wallClock = EventWallClock.Of(AllDayJune15, isAllDay: true, zone);

        wallClock.Date.Should().Be(new DateOnly(2026, 6, 15));
        wallClock.Time.Should().BeNull("a null time is the only all-day signal anything downstream reads.");
    }

    [Fact]
    public void Of_ForAnAllDayEventInANegativeZone_DoesNotReadTheDayBefore()
    {
        // Spelled out separately from the theory above because this is the defect itself rather than a
        // property: 2026-06-15T00:00Z converted to New York is 2026-06-14T20:00-04:00.
        TimeZoneInfo.ConvertTime(AllDayJune15, NewYork).Date.Should().Be(new DateTime(2026, 6, 14),
            "if this ever stops being true the fixture has lost its point, not the rule.");

        EventWallClock.Of(AllDayJune15, isAllDay: true, NewYork).Date.Should().Be(new DateOnly(2026, 6, 15));
    }

    [Fact]
    public void Of_ForATimedEventJustAfterLocalMidnight_ReadsTheHouseholdDayNotTheUtcOne()
    {
        // 00:30 in Dublin during BST is 23:30Z the day before — the grid's own defect, which files
        // this event in yesterday's cell and labels it "12:30 AM".
        var instant = new DateTimeOffset(2026, 6, 14, 23, 30, 0, TimeSpan.Zero);

        var wallClock = EventWallClock.Of(instant, isAllDay: false, Dublin);

        wallClock.Date.Should().Be(new DateOnly(2026, 6, 15));
        wallClock.Time.Should().Be(new TimeOnly(0, 30));
    }

    [Fact]
    public void Of_ForATimedEventLateInTheEveningInANegativeZone_ReadsTheEarlierHouseholdDay()
    {
        // The mirror image: 03:30Z on 16 June is 23:30 on the 15th in New York.
        var instant = new DateTimeOffset(2026, 6, 16, 3, 30, 0, TimeSpan.Zero);

        var wallClock = EventWallClock.Of(instant, isAllDay: false, NewYork);

        wallClock.Date.Should().Be(new DateOnly(2026, 6, 15));
        wallClock.Time.Should().Be(new TimeOnly(23, 30));
    }

    [Fact]
    public void Of_ForTwoInstantsAcrossADaylightSavingTransition_UsesTheOffsetInForceAtEachOne()
    {
        // 29 March 2026 is the day Irish clocks go forward: Dublin is UTC+0 until 01:00Z and UTC+1
        // afterwards, so the offset is a property of the INSTANT, not of the day. No fixed-offset
        // stand-in satisfies both of these at once, which is why a real zone is used.
        var beforeTheClocksGoForward = EventWallClock.Of(
            new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero), isAllDay: false, Dublin);
        var afterTheClocksGoForward = EventWallClock.Of(
            new DateTimeOffset(2026, 3, 29, 23, 30, 0, TimeSpan.Zero), isAllDay: false, Dublin);

        beforeTheClocksGoForward.Should().Be(new EventWallClock(new DateOnly(2026, 3, 29), new TimeOnly(0, 30)));
        afterTheClocksGoForward.Should().Be(new EventWallClock(new DateOnly(2026, 3, 30), new TimeOnly(0, 30)));
    }

    [Fact]
    public void Of_ForAnAllDayEventOnADaylightSavingTransitionDay_IsStillTheStoredDate()
    {
        // The transition cannot reach an all-day event at all, because no conversion happens. Pinned
        // so a future edit that "unifies" the two branches fails here rather than in production.
        var transitionDay = new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.Zero);

        EventWallClock.Of(transitionDay, isAllDay: true, Dublin).Date.Should().Be(new DateOnly(2026, 3, 29));
    }

    [Fact]
    public void Of_ForTheLastTickOfAGoogleExclusiveAllDayEnd_NamesTheInclusiveLastDay()
    {
        // How the month endpoint reads a multi-day span's last cell. Google's all-day end is the
        // EXCLUSIVE next-day midnight, so the last day it covers is one tick before it.
        var exclusiveEnd = new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero);

        EventWallClock.Of(exclusiveEnd.AddTicks(-1), isAllDay: true, NewYork).Date
            .Should().Be(new DateOnly(2026, 6, 17));
    }

    [Fact]
    public void Of_ForTheLegacyInclusiveEndOfDayShape_NamesTheSameLastDay()
    {
        // The second, unrelated legacy shape GoogleAllDayDate.IsInclusiveEndOfDay describes
        // (23:59:59.9999999 UTC). It must land on the same last day as the exclusive form above, or
        // existing rows change which cells they occupy — and nothing stored is being migrated.
        var legacyInclusiveEnd = new DateTimeOffset(2026, 6, 17, 23, 59, 59, TimeSpan.Zero).AddTicks(9_999_999);

        EventWallClock.Of(legacyInclusiveEnd.AddTicks(-1), isAllDay: true, NewYork).Date
            .Should().Be(new DateOnly(2026, 6, 17));
    }

    [Fact]
    public void CompareTo_WithinOneDay_PutsAnAllDayEventBeforeEveryTimedOne()
    {
        // All-day first is what Google's own day views do, and it is a deliberate change from the
        // order a midnight-UTC instant produced incidentally: in Dublin that sorted an all-day event
        // between a 00:30 and a 02:00 event.
        var allDay = new EventWallClock(new DateOnly(2026, 6, 15), null);
        var earlyTimed = new EventWallClock(new DateOnly(2026, 6, 15), new TimeOnly(0, 30));

        allDay.CompareTo(earlyTimed).Should().BeNegative();
        earlyTimed.CompareTo(allDay).Should().BePositive();
    }

    [Fact]
    public void CompareTo_SortsByDateFirstAndThenByTime()
    {
        var unordered = new[]
        {
            new EventWallClock(new DateOnly(2026, 6, 16), new TimeOnly(9, 0)),
            new EventWallClock(new DateOnly(2026, 6, 15), new TimeOnly(14, 0)),
            new EventWallClock(new DateOnly(2026, 6, 16), null),
            new EventWallClock(new DateOnly(2026, 6, 15), new TimeOnly(2, 0))
        };

        unordered.OrderBy(w => w).Should().ContainInOrder(
            new EventWallClock(new DateOnly(2026, 6, 15), new TimeOnly(2, 0)),
            new EventWallClock(new DateOnly(2026, 6, 15), new TimeOnly(14, 0)),
            new EventWallClock(new DateOnly(2026, 6, 16), null),
            new EventWallClock(new DateOnly(2026, 6, 16), new TimeOnly(9, 0)));
    }
}
