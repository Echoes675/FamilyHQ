using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// FHQ-131: the header clock read DateTime.Now directly, so with a simulated/offset kiosk
// time (KioskTimeProvider) it disagreed with the rest of the UI. The displayed string is a
// pure function of the injected TimeProvider, extracted so it can be unit-tested with
// FakeTimeProvider (no bUnit in this project) — the NowLinePosition precedent.
public class HeaderClockDisplayTests
{
    // FakeTimeProvider defaults LocalTimeZone to UTC, so local now equals the seeded instant
    // regardless of the machine the tests run on.
    private static FakeTimeProvider FakeAt(DateTimeOffset utc) => new(utc);

    [Fact]
    public void CurrentTime_AtSimulatedNineAm_ReturnsZeroPaddedTwentyFourHourString()
    {
        var clock = FakeAt(new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.Zero));

        HeaderClockDisplay.CurrentTime(clock).Should().Be("09:00");
    }

    [Fact]
    public void CurrentTime_InTheAfternoon_UsesTwentyFourHourFormat()
    {
        var clock = FakeAt(new DateTimeOffset(2026, 8, 13, 14, 30, 0, TimeSpan.Zero));

        HeaderClockDisplay.CurrentTime(clock).Should().Be("14:30");
    }

    [Fact]
    public void CurrentTime_AfterClockAdvances_ReflectsTheSimulatedTime()
    {
        var clock = FakeAt(new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.Zero));
        HeaderClockDisplay.CurrentTime(clock).Should().Be("09:00");

        clock.Advance(TimeSpan.FromMinutes(1));

        HeaderClockDisplay.CurrentTime(clock).Should().Be("09:01");
    }
}
