using FamilyHQ.Core.Interfaces;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Time.Tests;

/// <summary>
/// The DST semantics of the one shared recurrence adapter, asserted directly rather than through a
/// recurrence rule. The engine's own tests prove that an occurrence holds its wall clock across a
/// transition; these prove what the adapter underneath them does with a reading that is ambiguous,
/// one that does not exist, and one that is carried out and back again.
/// <para>
/// <b>Why two zones.</b> <c>Europe/London</c> and <c>America/New_York</c> change their clocks on
/// different dates and between different offset pairs (+00:00/+01:00 against -05:00/-04:00), so no
/// single host offset and no single transition date can satisfy both. A one-zone version of these
/// tests would pass on a machine that happened to sit in that zone. Both are long-standing tzdb
/// zones whose current rules have been unchanged for well over a decade, and
/// <see cref="BothZonesResolveAgainstTheBundledTzdb"/> fails loudly if either stops resolving —
/// a zone the bundled database cannot find has cost this repository a red build before.
/// </para>
/// </summary>
public class NodaTimeRecurrenceTimeZoneDstSemanticsTests
{
    private const string London = "Europe/London";
    private const string NewYork = "America/New_York";

    private static IRecurrenceTimeZone Zone(string id) =>
        new NodaTimeRecurrenceTimeZoneFactory().TryCreate(id)
        ?? throw new InvalidOperationException($"the bundled tzdb must carry {id}");

    [Theory]
    [InlineData(London)]
    [InlineData(NewYork)]
    public void BothZonesResolveAgainstTheBundledTzdb(string id) =>
        // Every case below indexes into the tz database by one of these two ids. If a future NodaTime
        // drops or renames one, that must surface here as a named failure rather than as arithmetic
        // that quietly stops meaning anything.
        Zone(id).Id.Should().Be(id);

    // ── An ambiguous reading: the wall clock that happens twice ───────────────

    [Theory]
    // London's clocks go back at 02:00 BST on 25 Oct 2026, so 01:30 occurs as BST (00:30Z) and then
    // again as GMT (01:30Z). The lenient rule takes the earlier.
    [InlineData(London, 2026, 10, 25, 0, 30)]
    // New York's go back at 02:00 EDT on 1 Nov 2026: 01:30 occurs as EDT (05:30Z) then EST (06:30Z).
    [InlineData(NewYork, 2026, 11, 1, 5, 30)]
    public void ToInstant_AnAmbiguousWallClock_TakesTheEarlierOfTheTwoInstants(
        string id, int year, int month, int day, int expectedUtcHour, int expectedUtcMinute)
    {
        var instant = Zone(id).ToInstant(new DateTime(year, month, day, 1, 30, 0));

        instant.Should().Be(new DateTimeOffset(
            year, month, day, expectedUtcHour, expectedUtcMinute, 0, TimeSpan.Zero));
    }

    // ── A skipped reading: the wall clock that never happens ──────────────────

    [Theory]
    // London's clocks go forward at 01:00 GMT on 29 Mar 2026, so 01:30 does not exist; shifted
    // forward by the one-hour gap it becomes 02:30 BST = 01:30Z.
    [InlineData(London, 2026, 3, 29, 1, 30, 1, 30)]
    // New York's go forward at 02:00 EST on 8 Mar 2026, so 02:30 does not exist; shifted forward it
    // becomes 03:30 EDT = 07:30Z.
    [InlineData(NewYork, 2026, 3, 8, 2, 30, 7, 30)]
    public void ToInstant_ASkippedWallClock_ShiftsForwardByTheLengthOfTheGap(
        string id, int year, int month, int day,
        int localHour, int localMinute, int expectedUtcHour, int expectedUtcMinute)
    {
        var zone = Zone(id);

        var instant = zone.ToInstant(new DateTime(year, month, day, localHour, localMinute, 0));

        // Shifting forward rather than throwing is what stops a rule step losing an occurrence, and
        // the shift is the gap's own length — not an arbitrary nudge.
        instant.Should().Be(new DateTimeOffset(
            year, month, day, expectedUtcHour, expectedUtcMinute, 0, TimeSpan.Zero));
        zone.ToWallClock(instant).Should().Be(
            new DateTime(year, month, day, localHour, localMinute, 0).AddHours(1));
    }

    // ── Round trips across a transition ───────────────────────────────────────

    [Theory]
    // Either side of London's autumn transition, and of its spring one.
    [InlineData(London, 2026, 10, 24)]
    [InlineData(London, 2026, 10, 26)]
    [InlineData(London, 2026, 3, 28)]
    [InlineData(London, 2026, 3, 30)]
    // Either side of New York's, which fall on different dates.
    [InlineData(NewYork, 2026, 10, 31)]
    [InlineData(NewYork, 2026, 11, 2)]
    [InlineData(NewYork, 2026, 3, 7)]
    [InlineData(NewYork, 2026, 3, 9)]
    public void AnInstantEitherSideOfATransition_SurvivesAWallClockRoundTrip(
        string id, int year, int month, int day)
    {
        var zone = Zone(id);
        var instant = new DateTimeOffset(year, month, day, 18, 0, 0, TimeSpan.Zero);

        // The offset in force differs between the two days of each pair, so an adapter that ignored
        // the transition would return the same wall clock for both and fail one of them.
        zone.ToInstant(zone.ToWallClock(instant)).Should().Be(instant);
    }

    [Theory]
    [InlineData(London, 2026, 10, 24, 19)]
    [InlineData(London, 2026, 10, 26, 18)]
    [InlineData(NewYork, 2026, 10, 31, 14)]
    [InlineData(NewYork, 2026, 11, 2, 13)]
    public void AnUnambiguousWallClockEitherSideOfATransition_SurvivesAnInstantRoundTrip(
        string id, int year, int month, int day, int localHour)
    {
        var zone = Zone(id);
        var wallClock = new DateTime(year, month, day, localHour, 0, 0);

        zone.ToWallClock(zone.ToInstant(wallClock)).Should().Be(wallClock);
    }

    [Theory]
    // The later, GMT reading of London's repeated 01:30 on 25 Oct 2026.
    [InlineData(London, 2026, 10, 25, 1, 30, 0, 30)]
    // The later, EST reading of New York's repeated 01:30 on 1 Nov 2026.
    [InlineData(NewYork, 2026, 11, 1, 6, 30, 5, 30)]
    public void TheLaterOfTwoAmbiguousInstants_DoesNotSurviveTheRoundTrip(
        string id, int year, int month, int day,
        int utcHour, int utcMinute, int expectedUtcHour, int expectedUtcMinute)
    {
        // Stated rather than left to be discovered: the round trip is NOT total. Both readings of the
        // repeated hour share one wall clock, and the lenient rule maps that wall clock to the
        // earlier instant — so an instant inside the repeated hour comes back an hour early. Callers
        // that must preserve an exact instant across a fall-back have to carry the offset, not the
        // wall clock.
        var zone = Zone(id);
        var later = new DateTimeOffset(year, month, day, utcHour, utcMinute, 0, TimeSpan.Zero);

        zone.ToInstant(zone.ToWallClock(later)).Should().Be(new DateTimeOffset(
            year, month, day, expectedUtcHour, expectedUtcMinute, 0, TimeSpan.Zero));
    }
}
