using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// FHQ-127: the Day view "now" line froze because its position was read from DateTime.Now at
// render time only. Position and visibility are pure functions of the injected TimeProvider
// so they can be unit-tested with FakeTimeProvider (no bUnit in this project); the per-minute
// re-render loop mirrors HeaderClock and is covered by E2E.
public class NowLinePositionTests
{
    // FakeTimeProvider defaults LocalTimeZone to UTC, so local now equals the seeded instant
    // regardless of the machine the tests run on.
    private static FakeTimeProvider FakeAt(DateTimeOffset utc) => new(utc);

    [Fact]
    public void TopPercent_AtMidnight_IsZero()
    {
        var clock = FakeAt(new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero));

        NowLinePosition.TopPercent(clock).Should().Be(0.0);
    }

    [Fact]
    public void TopPercent_AtNoon_IsFiftyPercent()
    {
        var clock = FakeAt(new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero));

        NowLinePosition.TopPercent(clock).Should().Be(50.0);
    }

    [Fact]
    public void TopPercent_AfterTimeAdvances_ReflectsUpdatedTime()
    {
        var clock = FakeAt(new DateTimeOffset(2026, 8, 13, 6, 0, 0, TimeSpan.Zero));
        NowLinePosition.TopPercent(clock).Should().Be(25.0);

        clock.Advance(TimeSpan.FromMinutes(90));

        NowLinePosition.TopPercent(clock).Should().Be(31.25);
    }

    [Fact]
    public void IsVisible_WhenSelectedDateIsToday_ReturnsTrue()
    {
        var clock = FakeAt(new DateTimeOffset(2026, 8, 13, 10, 30, 0, TimeSpan.Zero));

        NowLinePosition.IsVisible(new DateTime(2026, 8, 13), clock).Should().BeTrue();
    }

    [Fact]
    public void IsVisible_WhenSelectedDateIsNotToday_ReturnsFalse()
    {
        var clock = FakeAt(new DateTimeOffset(2026, 8, 13, 10, 30, 0, TimeSpan.Zero));

        NowLinePosition.IsVisible(new DateTime(2026, 8, 12), clock).Should().BeFalse();
    }

    [Fact]
    public void IsVisible_AfterMidnightRollover_HidesLineOnThePreviousDay()
    {
        var clock = FakeAt(new DateTimeOffset(2026, 8, 13, 23, 59, 0, TimeSpan.Zero));
        var selected = new DateTime(2026, 8, 13);
        NowLinePosition.IsVisible(selected, clock).Should().BeTrue();

        clock.Advance(TimeSpan.FromMinutes(2));

        NowLinePosition.IsVisible(selected, clock).Should().BeFalse();
    }
}
