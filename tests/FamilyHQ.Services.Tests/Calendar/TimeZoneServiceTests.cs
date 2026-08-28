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
    private static (TimeZoneService sut,
                    Mock<IDisplaySettingRepository> display,
                    Mock<ILocationSettingRepository> loc,
                    Mock<ITimeZoneLookup> tzLookup)
        CreateSut(string userId = "u-1")
    {
        var cu = new Mock<ICurrentUserService>(); cu.SetupGet(c => c.UserId).Returns(userId);
        var display = new Mock<IDisplaySettingRepository>();
        var loc = new Mock<ILocationSettingRepository>();
        var tzLookup = new Mock<ITimeZoneLookup>();
        // FHQ-178: ILocationService is deliberately absent. The ip-api seam is gone from this class,
        // not merely unused — it geolocates the hosting VPS, and its answer used to reach Google.
        return (new TimeZoneService(cu.Object, display.Object, loc.Object, tzLookup.Object),
                display, loc, tzLookup);
    }

    // ── ResolveAutoZoneAsync ────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAutoZone_WithSavedLocation_DerivesFromLatLon_WithoutIpApi()
    {
        var (sut, _, loc, tzLookup) = CreateSut();
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LocationSetting { Latitude = 51.5074, Longitude = -0.1278 });
        tzLookup.Setup(t => t.GetTimeZone(51.5074, -0.1278)).Returns("Europe/London");

        (await sut.ResolveAutoZoneAsync()).Should().Be("Europe/London");
        tzLookup.Verify(t => t.GetTimeZone(51.5074, -0.1278), Times.Once);
    }

    [Fact]
    public async Task ResolveAutoZone_NoLocation_FallsBackToTheKiosksOwnZone_NotAnIpLookup()
    {
        // The kiosk sits in the family's house; the server sits in a datacentre. Only one of those
        // knows where the family is.
        var (sut, display, loc, _) = CreateSut();
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((LocationSetting?)null);
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DisplaySetting { UserId = "u-1", IanaTimeZone = "Europe/Dublin", IsTimeZoneAutoDetected = true });

        (await sut.ResolveAutoZoneAsync()).Should().Be("Europe/Dublin");
    }

    [Fact]
    public async Task ResolveAutoZone_NoLocationAndNoKioskReport_ReturnsNull()
    {
        // Null, not a guess. It flows to GetSendZoneAsync -> familyZone on Google event creation, so
        // inventing a zone here stamps a fabricated one onto the family's calendar. Null lets the
        // caller fall back to UTC, which asserts nothing about where they live.
        var (sut, display, loc, _) = CreateSut();
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((LocationSetting?)null);
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((DisplaySetting?)null);

        (await sut.ResolveAutoZoneAsync()).Should().BeNull();
    }

    [Fact]
    public async Task ResolveAutoZone_DoesNotReuseAnExplicitZoneAsIfItWereDetected()
    {
        // An explicit zone is the user's answer, not a detection result. Returning it here would
        // launder it into the auto path and let a later re-resolve overwrite it with something else.
        var (sut, display, loc, _) = CreateSut();
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((LocationSetting?)null);
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DisplaySetting { UserId = "u-1", IanaTimeZone = "Europe/Dublin", IsTimeZoneAutoDetected = false });

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
        var (sut, display, loc, _) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DisplaySetting { UserId = "u-1", IanaTimeZone = "America/New_York" });

        (await sut.GetSendZoneAsync()).Should().Be("America/New_York");
        loc.Verify(l => l.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        display.Verify(d => d.UpsertAsync(It.IsAny<string>(), It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSendZone_WhenUnset_ReturnsNull_WithoutResolvingOrPersisting()
    {
        // READ-ONLY: the outbound path must never resolve (no ip-api / saved-location lookup) or persist.
        var (sut, display, loc, _) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((DisplaySetting?)null);

        (await sut.GetSendZoneAsync()).Should().BeNull();
        loc.Verify(l => l.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        display.Verify(d => d.UpsertAsync(It.IsAny<string>(), It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── SetKioskZoneAsync (FHQ-178) ─────────────────────────────────────────

    [Fact]
    public async Task SetKioskZone_WhenUnset_PersistsAsAutoDetected()
    {
        var (sut, display, _, _) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((DisplaySetting?)null);
        DisplaySetting? upserted = null;
        display.Setup(d => d.UpsertAsync("u-1", It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()))
               .Callback<string, DisplaySetting, CancellationToken>((_, s, _) => upserted = s)
               .ReturnsAsync((string _, DisplaySetting s, CancellationToken _) => s);

        await sut.SetKioskZoneAsync("Europe/Dublin");

        upserted.Should().NotBeNull();
        upserted!.IanaTimeZone.Should().Be("Europe/Dublin");
        upserted.IsTimeZoneAutoDetected.Should().BeTrue();
    }

    [Fact]
    public async Task SetKioskZone_WhenTheKiosksOsZoneChanged_UpdatesTheAutoDetectedZone()
    {
        // The user's question that shaped this design: how does the system learn the kiosk's OS
        // timezone changed? It reports on every load, and an auto-detected zone follows it. No
        // polling, and no extra state to hold the previous answer.
        var (sut, display, _, _) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DisplaySetting { UserId = "u-1", IanaTimeZone = "Europe/London", IsTimeZoneAutoDetected = true });
        DisplaySetting? upserted = null;
        display.Setup(d => d.UpsertAsync("u-1", It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()))
               .Callback<string, DisplaySetting, CancellationToken>((_, s, _) => upserted = s)
               .ReturnsAsync((string _, DisplaySetting s, CancellationToken _) => s);

        await sut.SetKioskZoneAsync("Europe/Dublin");

        upserted!.IanaTimeZone.Should().Be("Europe/Dublin");
        upserted.IsTimeZoneAutoDetected.Should().BeTrue();
    }

    [Fact]
    public async Task SetKioskZone_WhenTheZoneIsExplicit_IsIgnored()
    {
        // The other half of the same question: with a manually-set zone there is nothing to detect.
        // The kiosk's OS is not evidence against a choice the family made.
        var (sut, display, _, _) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DisplaySetting { UserId = "u-1", IanaTimeZone = "America/New_York", IsTimeZoneAutoDetected = false });

        await sut.SetKioskZoneAsync("Europe/Dublin");

        display.Verify(d => d.UpsertAsync(It.IsAny<string>(), It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetKioskZone_WhenUnchanged_DoesNotWrite()
    {
        // This runs on every kiosk load. An unconditional upsert would touch UpdatedAt each time.
        var (sut, display, _, _) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DisplaySetting { UserId = "u-1", IanaTimeZone = "Europe/Dublin", IsTimeZoneAutoDetected = true });

        await sut.SetKioskZoneAsync("Europe/Dublin");

        display.Verify(d => d.UpsertAsync(It.IsAny<string>(), It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetKioskZone_WhenZoneNullOrInvalid_IsNoOp()
    {
        var (sut, display, _, _) = CreateSut();

        await sut.SetKioskZoneAsync(null);
        await sut.SetKioskZoneAsync("Not/AZone");

        display.Verify(d => d.UpsertAsync(It.IsAny<string>(), It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()), Times.Never);
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
        var (sut, display, loc, tzLookup) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DisplaySetting { UserId = "u-1", IanaTimeZone = "America/New_York", IsTimeZoneAutoDetected = false });
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LocationSetting { Latitude = 51.5074, Longitude = -0.1278 });
        tzLookup.Setup(t => t.GetTimeZone(51.5074, -0.1278)).Returns("Europe/London");
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
        var (sut, display, loc, _) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DisplaySetting { UserId = "u-1", IanaTimeZone = "America/New_York", IsTimeZoneAutoDetected = false });

        await sut.RepersistAutoIfNotExplicitAsync();

        display.Verify(d => d.UpsertAsync(It.IsAny<string>(), It.IsAny<DisplaySetting>(), It.IsAny<CancellationToken>()), Times.Never);
        loc.Verify(l => l.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RepersistAutoIfNotExplicit_WhenAutoDetected_ReResolvesAndPersists()
    {
        var (sut, display, loc, tzLookup) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new DisplaySetting { UserId = "u-1", IanaTimeZone = "Europe/Berlin", IsTimeZoneAutoDetected = true });
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LocationSetting { Latitude = 51.5074, Longitude = -0.1278 });
        tzLookup.Setup(t => t.GetTimeZone(51.5074, -0.1278)).Returns("Europe/London");
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
        var (sut, display, loc, tzLookup) = CreateSut();
        display.Setup(d => d.GetAsync("u-1", It.IsAny<CancellationToken>())).ReturnsAsync((DisplaySetting?)null);
        loc.Setup(l => l.GetAsync("u-1", It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LocationSetting { Latitude = 51.5074, Longitude = -0.1278 });
        tzLookup.Setup(t => t.GetTimeZone(51.5074, -0.1278)).Returns("Europe/London");
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
