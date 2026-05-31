using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class TimeZoneServiceTests
{
    private static (TimeZoneService sut, Mock<IDisplaySettingRepository> display, Mock<ILocationSettingRepository> loc, Mock<ILocationService> ipapi)
        CreateSut(string userId = "u-1")
    {
        var cu = new Mock<ICurrentUserService>(); cu.SetupGet(c => c.UserId).Returns(userId);
        var display = new Mock<IDisplaySettingRepository>();
        var loc = new Mock<ILocationSettingRepository>();
        var ipapi = new Mock<ILocationService>();
        return (new TimeZoneService(cu.Object, display.Object, loc.Object, ipapi.Object), display, loc, ipapi);
    }

    // ── ResolveAutoZoneAsync ────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAutoZone_WithSavedLocation_DerivesFromLatLon_WithoutIpApi()
    {
        var (sut, _, loc, ipapi) = CreateSut();
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LocationSetting { Latitude = 51.5074, Longitude = -0.1278 });

        (await sut.ResolveAutoZoneAsync()).Should().Be("Europe/London");

        ipapi.Verify(i => i.GetEffectiveLocationAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAutoZone_NoLocation_UsesIpApi()
    {
        var (sut, _, loc, ipapi) = CreateSut();
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((LocationSetting?)null);
        ipapi.Setup(i => i.GetEffectiveLocationAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new LocationResult("Berlin", 52.52, 13.405, true, "Europe/Berlin"));

        (await sut.ResolveAutoZoneAsync()).Should().Be("Europe/Berlin");
    }

    [Fact]
    public async Task ResolveAutoZone_NoLocation_IpApiThrows_ReturnsNull()
    {
        var (sut, _, loc, ipapi) = CreateSut();
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((LocationSetting?)null);
        ipapi.Setup(i => i.GetEffectiveLocationAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());

        (await sut.ResolveAutoZoneAsync()).Should().BeNull();
    }

    [Fact]
    public async Task ResolveAutoZone_NoCurrentUser_ReturnsNull()
    {
        var (sut, _, _, _) = CreateSut(userId: "");
        (await sut.ResolveAutoZoneAsync()).Should().BeNull();
    }

    // ── GetSendZoneAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetSendZone_WhenPersisted_ReturnsIt_WithoutResolving()
    {
        var (sut, display, loc, ipapi) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DisplaySetting { UserId = "u-1", IanaTimeZone = "America/New_York" });

        (await sut.GetSendZoneAsync()).Should().Be("America/New_York");

        ipapi.Verify(i => i.GetEffectiveLocationAsync(It.IsAny<CancellationToken>()), Times.Never);
        loc.Verify(l => l.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        display.Verify(d => d.UpsertAsync(It.IsAny<string>(), It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSendZone_WhenUnset_ResolvesPersistsAndReturns()
    {
        var (sut, display, loc, ipapi) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((DisplaySetting?)null);
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((LocationSetting?)null);
        ipapi.Setup(i => i.GetEffectiveLocationAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new LocationResult("Berlin", 52.52, 13.405, true, "Europe/Berlin"));
        DisplaySetting? upserted = null;
        display.Setup(d => d.UpsertAsync("u-1", It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()))
               .Callback<string, DisplaySetting, CancellationToken>((_, s, _) => upserted = s)
               .ReturnsAsync((string _, DisplaySetting s, CancellationToken _) => s);

        (await sut.GetSendZoneAsync()).Should().Be("Europe/Berlin");

        upserted.Should().NotBeNull();
        upserted!.IanaTimeZone.Should().Be("Europe/Berlin");
        upserted.IsTimeZoneAutoDetected.Should().BeTrue();
    }

    [Fact]
    public async Task GetSendZone_WhenUnsetAndUnresolvable_ReturnsNull_WithoutPersisting()
    {
        var (sut, display, loc, ipapi) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((DisplaySetting?)null);
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((LocationSetting?)null);
        ipapi.Setup(i => i.GetEffectiveLocationAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());

        (await sut.GetSendZoneAsync()).Should().BeNull();

        display.Verify(d => d.UpsertAsync(It.IsAny<string>(), It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSendZone_NoCurrentUser_ReturnsNull()
    {
        var (sut, _, _, _) = CreateSut(userId: "");
        (await sut.GetSendZoneAsync()).Should().BeNull();
    }

    // ── SetExplicitZoneAsync ────────────────────────────────────────────────

    [Fact]
    public async Task SetExplicitZone_PersistsZone_NotAutoDetected()
    {
        var (sut, display, _, _) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((DisplaySetting?)null);
        DisplaySetting? upserted = null;
        display.Setup(d => d.UpsertAsync("u-1", It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()))
               .Callback<string, DisplaySetting, CancellationToken>((_, s, _) => upserted = s)
               .ReturnsAsync((string _, DisplaySetting s, CancellationToken _) => s);

        await sut.SetExplicitZoneAsync("America/New_York");

        upserted.Should().NotBeNull();
        upserted!.IanaTimeZone.Should().Be("America/New_York");
        upserted.IsTimeZoneAutoDetected.Should().BeFalse();
    }

    // ── ResetToAutoZoneAsync ────────────────────────────────────────────────

    [Fact]
    public async Task ResetToAutoZone_ReResolvesAndPersists_AsAutoDetected()
    {
        var (sut, display, loc, _) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DisplaySetting { UserId = "u-1", IanaTimeZone = "America/New_York", IsTimeZoneAutoDetected = false });
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LocationSetting { Latitude = 51.5074, Longitude = -0.1278 });
        DisplaySetting? upserted = null;
        display.Setup(d => d.UpsertAsync("u-1", It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()))
               .Callback<string, DisplaySetting, CancellationToken>((_, s, _) => upserted = s)
               .ReturnsAsync((string _, DisplaySetting s, CancellationToken _) => s);

        await sut.ResetToAutoZoneAsync();

        upserted.Should().NotBeNull();
        upserted!.IanaTimeZone.Should().Be("Europe/London");
        upserted.IsTimeZoneAutoDetected.Should().BeTrue();
    }

    // ── RepersistAutoIfNotExplicitAsync ─────────────────────────────────────

    [Fact]
    public async Task RepersistAutoIfNotExplicit_WhenExplicit_IsStickyNoOp()
    {
        var (sut, display, loc, ipapi) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DisplaySetting { UserId = "u-1", IanaTimeZone = "America/New_York", IsTimeZoneAutoDetected = false });

        await sut.RepersistAutoIfNotExplicitAsync();

        display.Verify(d => d.UpsertAsync(It.IsAny<string>(), It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()), Times.Never);
        loc.Verify(l => l.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        ipapi.Verify(i => i.GetEffectiveLocationAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RepersistAutoIfNotExplicit_WhenAutoDetected_ReResolvesAndPersists()
    {
        var (sut, display, loc, _) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DisplaySetting { UserId = "u-1", IanaTimeZone = "Europe/Berlin", IsTimeZoneAutoDetected = true });
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LocationSetting { Latitude = 51.5074, Longitude = -0.1278 });
        DisplaySetting? upserted = null;
        display.Setup(d => d.UpsertAsync("u-1", It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()))
               .Callback<string, DisplaySetting, CancellationToken>((_, s, _) => upserted = s)
               .ReturnsAsync((string _, DisplaySetting s, CancellationToken _) => s);

        await sut.RepersistAutoIfNotExplicitAsync();

        upserted.Should().NotBeNull();
        upserted!.IanaTimeZone.Should().Be("Europe/London");
        upserted.IsTimeZoneAutoDetected.Should().BeTrue();
    }

    [Fact]
    public async Task RepersistAutoIfNotExplicit_WhenUnset_ResolvesAndPersists()
    {
        var (sut, display, loc, _) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((DisplaySetting?)null);
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LocationSetting { Latitude = 51.5074, Longitude = -0.1278 });
        DisplaySetting? upserted = null;
        display.Setup(d => d.UpsertAsync("u-1", It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()))
               .Callback<string, DisplaySetting, CancellationToken>((_, s, _) => upserted = s)
               .ReturnsAsync((string _, DisplaySetting s, CancellationToken _) => s);

        await sut.RepersistAutoIfNotExplicitAsync();

        upserted.Should().NotBeNull();
        upserted!.IanaTimeZone.Should().Be("Europe/London");
        upserted.IsTimeZoneAutoDetected.Should().BeTrue();
    }

    // ── ToZonedWallClock (unchanged) ────────────────────────────────────────

    [Fact]
    public void ToZonedWallClock_holds_local_time_across_dst()
    {
        var (sut, _, _, _) = CreateSut();
        sut.ToZonedWallClock(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero), "Europe/London").Should().Be("2026-07-01T09:00:00");
        sut.ToZonedWallClock(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero), "Europe/London").Should().Be("2026-01-01T09:00:00");
    }
}
