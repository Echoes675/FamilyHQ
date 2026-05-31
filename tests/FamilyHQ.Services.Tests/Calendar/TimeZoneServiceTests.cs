using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
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
        var cache = new MemoryCache(new MemoryCacheOptions());
        return (new TimeZoneService(cu.Object, display.Object, loc.Object, ipapi.Object, cache), display, loc, ipapi);
    }

    [Fact]
    public async Task Explicit_setting_wins()
    {
        var (sut, display, _, _) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DisplaySetting { UserId = "u-1", IanaTimeZone = "America/New_York" });
        (await sut.GetEffectiveIanaZoneAsync()).Should().Be("America/New_York");
    }

    [Fact]
    public async Task No_explicit_with_custom_location_derives_from_latlon()
    {
        var (sut, display, loc, _) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((DisplaySetting?)null);
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LocationSetting { Latitude = 51.5074, Longitude = -0.1278 });
        (await sut.GetEffectiveIanaZoneAsync()).Should().Be("Europe/London");
    }

    [Fact]
    public async Task No_explicit_no_location_uses_ipapi()
    {
        var (sut, display, loc, ipapi) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((DisplaySetting?)null);
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((LocationSetting?)null);
        ipapi.Setup(i => i.GetEffectiveLocationAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new LocationResult("Berlin", 52.52, 13.405, true, "Europe/Berlin"));
        (await sut.GetEffectiveIanaZoneAsync()).Should().Be("Europe/Berlin");
    }

    [Fact]
    public async Task Nothing_resolvable_returns_null()
    {
        var (sut, display, loc, ipapi) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((DisplaySetting?)null);
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((LocationSetting?)null);
        ipapi.Setup(i => i.GetEffectiveLocationAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        (await sut.GetEffectiveIanaZoneAsync()).Should().BeNull();
    }

    [Fact]
    public void ToZonedWallClock_holds_local_time_across_dst()
    {
        var (sut, _, _, _) = CreateSut();
        sut.ToZonedWallClock(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero), "Europe/London").Should().Be("2026-07-01T09:00:00");
        sut.ToZonedWallClock(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero), "Europe/London").Should().Be("2026-01-01T09:00:00");
    }

    [Fact]
    public async Task Invalid_explicit_falls_through_to_location()
    {
        var (sut, display, loc, _) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DisplaySetting { UserId = "u-1", IanaTimeZone = "Not/AZone" });
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LocationSetting { Latitude = 51.5074, Longitude = -0.1278 });
        (await sut.GetEffectiveIanaZoneAsync()).Should().Be("Europe/London");
    }

    [Fact]
    public async Task Returns_null_when_no_current_user()
    {
        var (sut, _, _, _) = CreateSut(userId: "");
        (await sut.GetEffectiveIanaZoneAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Ipapi_result_is_cached_for_subsequent_calls()
    {
        var (sut, display, loc, ipapi) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((DisplaySetting?)null);
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((LocationSetting?)null);
        ipapi.Setup(i => i.GetEffectiveLocationAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new LocationResult("Berlin", 52.52, 13.405, true, "Europe/Berlin"));

        await sut.GetEffectiveIanaZoneAsync();
        await sut.GetEffectiveIanaZoneAsync();

        ipapi.Verify(i => i.GetEffectiveLocationAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
