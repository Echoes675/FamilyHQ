using FamilyHQ.WebUi.Services;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace FamilyHQ.WebUi.Tests.Services;

// FHQ-63: KioskTimeProvider wraps the system clock and lets lower environments advance the
// displayed "today" so E2E can cross midnight. Production runs with the override disabled,
// where it must behave exactly like the wrapped provider.
public class KioskTimeProviderTests
{
    private static FakeTimeProvider FakeAt(DateTimeOffset utc) => new FakeTimeProvider(utc);

    [Fact]
    public void WhenOverrideDisabled_AdvanceDays_IsIgnored()
    {
        var baseUtc = new DateTimeOffset(2026, 6, 9, 10, 0, 0, TimeSpan.Zero);
        var clock = new KioskTimeProvider(FakeAt(baseUtc), overrideEnabled: false);

        clock.AdvanceDays(5);

        clock.GetUtcNow().Should().Be(baseUtc);
    }

    [Fact]
    public void WhenOverrideEnabled_AdvanceDays_ShiftsNowByWholeDays()
    {
        var baseUtc = new DateTimeOffset(2026, 6, 9, 10, 0, 0, TimeSpan.Zero);
        var clock = new KioskTimeProvider(FakeAt(baseUtc), overrideEnabled: true);

        clock.AdvanceDays(1);

        clock.GetUtcNow().Should().Be(baseUtc.AddDays(1));
    }

    [Fact]
    public void Reset_ClearsTheOffset()
    {
        var baseUtc = new DateTimeOffset(2026, 6, 9, 10, 0, 0, TimeSpan.Zero);
        var clock = new KioskTimeProvider(FakeAt(baseUtc), overrideEnabled: true);

        clock.AdvanceDays(3);
        clock.Reset();

        clock.GetUtcNow().Should().Be(baseUtc);
    }

    [Fact]
    public void LocalTimeZone_DelegatesToWrappedProvider()
    {
        var fake = FakeAt(DateTimeOffset.UnixEpoch);
        var clock = new KioskTimeProvider(fake, overrideEnabled: true);
        clock.LocalTimeZone.Should().Be(fake.LocalTimeZone);
    }
}
