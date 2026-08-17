using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// FHQ-161 — the injectable seam that resolves an IANA id into the zone the recurrence engine
/// enumerates in, and the transition semantics that seam guarantees.
/// </summary>
public class NodaTimeRecurrenceTimeZoneFactoryTests
{
    private static NodaTimeRecurrenceTimeZoneFactory Sut() => new();

    [Fact]
    public void TryCreate_KnownIanaId_ReturnsAZoneCarryingThatId()
    {
        var zone = Sut().TryCreate("Europe/London");

        zone.Should().NotBeNull();
        zone!.Id.Should().Be("Europe/London");
    }

    [Fact]
    public void TryCreate_UnknownId_ReturnsNullRatherThanThrowing()
    {
        // Callers decide what an unknown zone means; the lookup itself must never throw.
        Sut().TryCreate("Not/AZone").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreate_NullOrBlankId_ReturnsNull(string? id)
    {
        Sut().TryCreate(id).Should().BeNull();
    }

    [Fact]
    public void ToWallClock_ReadsAnInstantInTheZonesLocalTime()
    {
        var zone = Sut().TryCreate("Europe/London")!;

        // 13 Oct 2026 18:00Z is 19:00 BST.
        zone.ToWallClock(new DateTimeOffset(2026, 10, 13, 18, 0, 0, TimeSpan.Zero))
            .Should().Be(new DateTime(2026, 10, 13, 19, 0, 0));
    }

    [Fact]
    public void ToInstant_UnambiguousWallClock_ResolvesToTheZoneOffsetInForce()
    {
        var zone = Sut().TryCreate("Europe/London")!;

        // Before the 25 Oct 2026 transition London is BST (+01:00).
        zone.ToInstant(new DateTime(2026, 10, 13, 19, 0, 0))
            .Should().Be(new DateTimeOffset(2026, 10, 13, 18, 0, 0, TimeSpan.Zero));

        // After it, GMT (+00:00).
        zone.ToInstant(new DateTime(2026, 10, 27, 19, 0, 0))
            .Should().Be(new DateTimeOffset(2026, 10, 27, 19, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ToInstant_AmbiguousWallClock_ResolvesToTheEarlierInstant()
    {
        // 01:30 on 25 Oct 2026 occurs twice (02:00 BST → 01:00 GMT): 00:30Z then 01:30Z.
        var zone = Sut().TryCreate("Europe/London")!;

        zone.ToInstant(new DateTime(2026, 10, 25, 1, 30, 0))
            .Should().Be(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ToInstant_SkippedWallClock_ShiftsForwardByTheGap()
    {
        // 01:30 on 29 Mar 2026 does not exist (01:00 → 02:00), so it shifts to 02:30 BST = 01:30Z.
        // Shifting forward rather than throwing is what keeps a rule from losing an occurrence.
        var zone = Sut().TryCreate("Europe/London")!;

        zone.ToInstant(new DateTime(2026, 3, 29, 1, 30, 0))
            .Should().Be(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ToInstant_IgnoresTheKindOfTheSuppliedWallClock()
    {
        // The engine enumerates unspecified-kind wall clocks; a Utc-kind value must not be treated
        // as an instant and re-converted.
        var zone = Sut().TryCreate("Europe/London")!;

        zone.ToInstant(new DateTime(2026, 10, 13, 19, 0, 0, DateTimeKind.Utc))
            .Should().Be(zone.ToInstant(new DateTime(2026, 10, 13, 19, 0, 0)));
    }
}
