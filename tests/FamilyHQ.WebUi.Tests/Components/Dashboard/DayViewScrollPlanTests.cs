using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// FHQ-132: the Day view scroll-to-now used DateTime.Today / DateTime.Now, so a simulated kiosk
// time scrolled the grid to the wrong position. The decision is a pure function of the injected
// TimeProvider (kiosk-adjusted local now, matching NowLinePosition), unit-tested here with
// FakeTimeProvider; the JS scroll itself is covered by E2E.
public class DayViewScrollPlanTests
{
    // FakeTimeProvider defaults LocalTimeZone to UTC, so local now equals the seeded instant
    // regardless of the machine the tests run on.
    private static FakeTimeProvider FakeAt(DateTimeOffset utc) => new(utc);

    [Fact]
    public void Decide_ViewingKioskTodayNotYetScrolled_ScrollsToCurrentMinutes()
    {
        var clock = FakeAt(new DateTimeOffset(2026, 8, 13, 14, 30, 0, TimeSpan.Zero));

        var plan = DayViewScrollPlan.Decide(new DateTime(2026, 8, 13), null, clock);

        plan.ShouldScroll.Should().BeTrue();
        plan.TargetMinutesOfDay.Should().Be(870.0);
        plan.LastScrolledDate.Should().Be(new DateTime(2026, 8, 13));
    }

    [Fact]
    public void Decide_AfterKioskDayRollover_ScrollsAgainOnTheNewDay()
    {
        var clock = FakeAt(new DateTimeOffset(2026, 8, 13, 23, 50, 0, TimeSpan.Zero));
        clock.Advance(TimeSpan.FromMinutes(20)); // kiosk time is now 2026-08-14 00:10

        var plan = DayViewScrollPlan.Decide(new DateTime(2026, 8, 14), new DateTime(2026, 8, 13), clock);

        plan.ShouldScroll.Should().BeTrue();
        plan.TargetMinutesOfDay.Should().Be(10.0);
        plan.LastScrolledDate.Should().Be(new DateTime(2026, 8, 14));
    }

    [Fact]
    public void Decide_AlreadyScrolledToday_DoesNotScrollAgainAndKeepsGate()
    {
        var clock = FakeAt(new DateTimeOffset(2026, 8, 13, 14, 30, 0, TimeSpan.Zero));
        var today = new DateTime(2026, 8, 13);

        var plan = DayViewScrollPlan.Decide(today, today, clock);

        plan.ShouldScroll.Should().BeFalse();
        plan.LastScrolledDate.Should().Be(today);
    }

    [Fact]
    public void Decide_ViewingDifferentDay_ResetsGateWithoutScrolling()
    {
        var clock = FakeAt(new DateTimeOffset(2026, 8, 13, 14, 30, 0, TimeSpan.Zero));

        var plan = DayViewScrollPlan.Decide(new DateTime(2026, 8, 14), new DateTime(2026, 8, 13), clock);

        plan.ShouldScroll.Should().BeFalse();
        plan.LastScrolledDate.Should().BeNull();
    }

    [Fact]
    public void Decide_KioskLocalDateDiffersFromUtcDate_ComparesAgainstKioskLocalDate()
    {
        // 23:30 UTC on the 13th is already 01:30 on the 14th at kiosk offset +02:00, so the
        // 14th is kiosk-today and the scroll target is 90 minutes into the new day.
        var clock = FakeAt(new DateTimeOffset(2026, 8, 13, 23, 30, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("kiosk+02", TimeSpan.FromHours(2), "kiosk+02", "kiosk+02"));

        var plan = DayViewScrollPlan.Decide(new DateTime(2026, 8, 14), null, clock);

        plan.ShouldScroll.Should().BeTrue();
        plan.TargetMinutesOfDay.Should().Be(90.0);
        plan.LastScrolledDate.Should().Be(new DateTime(2026, 8, 14));
    }
}
