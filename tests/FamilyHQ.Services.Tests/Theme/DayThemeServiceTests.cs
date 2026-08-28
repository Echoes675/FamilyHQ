using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Theme;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FamilyHQ.Services.Tests.Theme;

public class DayThemeServiceTests
{
    private const string Kiosk = "kiosk-kitchen";
    private const string OtherKiosk = "kiosk-hall";

    /// <summary>
    /// A clock pinned to midday UTC and the date it implies. <see cref="FakeTimeProvider"/> leaves
    /// <c>LocalTimeZone</c> at UTC, so the service's <c>GetLocalNow()</c> date and this UTC instant
    /// are the same date by construction — midday is simply a value far from either boundary, not a
    /// guard against a divergence that exists here. What the pin buys is the single source: the
    /// test's expectation and the service's date key are derived from one instant and cannot drift.
    /// </summary>
    private static (FakeTimeProvider Clock, DateOnly Today) PinnedToday()
        => (new FakeTimeProvider(new DateTimeOffset(2024, 6, 21, 12, 0, 0, TimeSpan.Zero)),
            new DateOnly(2024, 6, 21));

    private static Mock<ILocationSettingRepository> LocationFor(
        string userId, double lat = 53.35, double lon = -6.26)
    {
        var mock = new Mock<ILocationSettingRepository>();
        mock.Setup(x => x.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationSetting { UserId = userId, Latitude = lat, Longitude = lon });
        return mock;
    }

    private static Mock<ITimeZoneLookup> ZoneLookup(string? zone)
    {
        var mock = new Mock<ITimeZoneLookup>();
        mock.Setup(x => x.GetTimeZone(It.IsAny<double>(), It.IsAny<double>())).Returns(zone);
        return mock;
    }

    private static Mock<ISunCalculatorService> SunCalc(
        TimeOnly morning, TimeOnly daytime, TimeOnly evening, TimeOnly night)
    {
        var mock = new Mock<ISunCalculatorService>();
        mock.Setup(x => x.CalculateBoundariesAsync(
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<DateOnly>(), It.IsAny<string?>()))
            .ReturnsAsync(new DayThemeBoundaries(morning, daytime, evening, night));
        return mock;
    }

    private static DayThemeService CreateSut(
        IDayThemeRepository dayThemeRepo,
        ILocationSettingRepository locationRepo,
        ISunCalculatorService sunCalculator,
        ITimeZoneLookup? timeZoneLookup = null,
        TimeProvider? timeProvider = null)
        => new(dayThemeRepo, locationRepo, sunCalculator,
               timeZoneLookup ?? new Mock<ITimeZoneLookup>().Object,
               timeProvider ?? TimeProvider.System);

    // ---------------------------------------------------------------------------------------
    // FHQ-177: the kiosk owns its theme
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task EnsureTodayAsync_UsesTheKiosksOwnSavedLocation()
    {
        // The reported bug: boundaries were computed from a server-side IP lookup, which returns the
        // hosting datacentre — Berlin for a household in Derry — so every transition fired an hour
        // early. The coordinates must come from THIS kiosk's saved location and nowhere else.
        var (fakeTime, today) = PinnedToday();
        var repoMock = new Mock<IDayThemeRepository>();
        var locationMock = LocationFor(Kiosk, lat: 54.9966, lon: -7.3086);
        var sunCalcMock = SunCalc(new TimeOnly(5, 45), new TimeOnly(6, 25), new TimeOnly(19, 39), new TimeOnly(21, 19));

        await CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object,
                        ZoneLookup("Europe/London").Object, fakeTime)
            .EnsureTodayAsync(Kiosk);

        sunCalcMock.Verify(x => x.CalculateBoundariesAsync(54.9966, -7.3086, today, "Europe/London"), Times.Once);
        repoMock.Verify(x => x.UpsertAsync(It.Is<DayTheme>(d => d.UserId == Kiosk), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TwoKiosksInDifferentPlaces_EachGetTheirOwnBoundaries()
    {
        // The scoping requirement. With a single global row the second kiosk to be calculated
        // overwrote the first, and both then displayed one location's sunset.
        var (fakeTime, today) = PinnedToday();
        var repoMock = new Mock<IDayThemeRepository>();
        var persisted = new List<DayTheme>();
        repoMock.Setup(x => x.UpsertAsync(It.IsAny<DayTheme>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayTheme d, CancellationToken _) => { persisted.Add(d); return d; });

        var locationMock = new Mock<ILocationSettingRepository>();
        locationMock.Setup(x => x.GetAsync(Kiosk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationSetting { UserId = Kiosk, Latitude = 53.35, Longitude = -6.26 });
        locationMock.Setup(x => x.GetAsync(OtherKiosk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationSetting { UserId = OtherKiosk, Latitude = -33.87, Longitude = 151.21 });

        var zoneMock = new Mock<ITimeZoneLookup>();
        zoneMock.Setup(x => x.GetTimeZone(53.35, -6.26)).Returns("Europe/Dublin");
        zoneMock.Setup(x => x.GetTimeZone(-33.87, 151.21)).Returns("Australia/Sydney");

        var sunCalcMock = new Mock<ISunCalculatorService>();
        sunCalcMock.Setup(x => x.CalculateBoundariesAsync(53.35, -6.26, It.IsAny<DateOnly>(), "Europe/Dublin"))
            .ReturnsAsync(new DayThemeBoundaries(new TimeOnly(4, 0), new TimeOnly(5, 0), new TimeOnly(21, 0), new TimeOnly(22, 30)));
        sunCalcMock.Setup(x => x.CalculateBoundariesAsync(-33.87, 151.21, It.IsAny<DateOnly>(), "Australia/Sydney"))
            .ReturnsAsync(new DayThemeBoundaries(new TimeOnly(6, 30), new TimeOnly(7, 0), new TimeOnly(16, 0), new TimeOnly(17, 30)));

        var sut = CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object, zoneMock.Object, fakeTime);
        await sut.EnsureTodayAsync(Kiosk);
        await sut.EnsureTodayAsync(OtherKiosk);

        persisted.Should().HaveCount(2);
        persisted.Single(p => p.UserId == Kiosk).NightStart.Should().Be(new TimeOnly(22, 30));
        persisted.Single(p => p.UserId == OtherKiosk).NightStart.Should().Be(new TimeOnly(17, 30));
        persisted.Single(p => p.UserId == OtherKiosk).IanaTimeZone.Should().Be("Australia/Sydney");
    }

    [Fact]
    public async Task EnsureTodayAsync_DoesNothing_WhenTheKioskHasNoSavedLocation()
    {
        // Deliberately no fallback. Server-side IP geolocation reports the datacentre, so guessing
        // is choosing a known-wrong answer; no row means the kiosk keeps its default theme.
        var (fakeTime, _) = PinnedToday();
        var repoMock = new Mock<IDayThemeRepository>();
        var locationMock = new Mock<ILocationSettingRepository>();
        locationMock.Setup(x => x.GetAsync(Kiosk, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationSetting?)null);
        var sunCalcMock = new Mock<ISunCalculatorService>();

        await CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object, timeProvider: fakeTime)
            .EnsureTodayAsync(Kiosk);

        sunCalcMock.VerifyNoOtherCalls();
        repoMock.Verify(x => x.UpsertAsync(It.IsAny<DayTheme>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetTodayAsync_ReturnsNull_WhenTheKioskHasNoSavedLocation()
    {
        var locationMock = new Mock<ILocationSettingRepository>();
        locationMock.Setup(x => x.GetAsync(Kiosk, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationSetting?)null);

        var result = await CreateSut(new Mock<IDayThemeRepository>().Object, locationMock.Object,
                                     new Mock<ISunCalculatorService>().Object)
            .GetTodayAsync(Kiosk);

        result.Should().BeNull("no location is a normal state, not a fault the kiosk should surface");
    }

    [Fact]
    public async Task GetTodayAsync_ReturnsNull_WhenNoRowExistsYet()
    {
        var (fakeTime, _) = PinnedToday();
        var repoMock = new Mock<IDayThemeRepository>();
        repoMock.Setup(x => x.GetByDateAsync(Kiosk, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayTheme?)null);

        var result = await CreateSut(repoMock.Object, LocationFor(Kiosk).Object,
                                     new Mock<ISunCalculatorService>().Object,
                                     ZoneLookup("Europe/Dublin").Object, fakeTime)
            .GetTodayAsync(Kiosk);

        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------------------
    // Recalculation and idempotence
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task EnsureTodayAsync_DoesNotRecalculate_WhenRecordExists()
    {
        // FHQ-158: the date key comes from the injected clock, not a second read of the host's
        // calendar. Two independent reads of "today" disagree for the instant either side of local
        // midnight — the divergence class that produced FHQ-134 and tracker issue 11.
        var (fakeTime, today) = PinnedToday();
        var repoMock = new Mock<IDayThemeRepository>();
        repoMock.Setup(x => x.GetByDateAsync(Kiosk, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayTheme { UserId = Kiosk, Date = today });
        var sunCalcMock = new Mock<ISunCalculatorService>();

        await CreateSut(repoMock.Object, LocationFor(Kiosk).Object, sunCalcMock.Object,
                        ZoneLookup("Europe/Dublin").Object, fakeTime)
            .EnsureTodayAsync(Kiosk);

        sunCalcMock.Verify(x => x.CalculateBoundariesAsync(
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<DateOnly>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task RecalculateForTodayAsync_AlwaysRecalculates_EvenWhenRecordExists()
    {
        var (fakeTime, today) = PinnedToday();
        var repoMock = new Mock<IDayThemeRepository>();
        repoMock.Setup(x => x.GetByDateAsync(Kiosk, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayTheme { UserId = Kiosk, Date = today });
        var sunCalcMock = SunCalc(new TimeOnly(4, 0), new TimeOnly(5, 0), new TimeOnly(21, 0), new TimeOnly(22, 30));

        await CreateSut(repoMock.Object, LocationFor(Kiosk).Object, sunCalcMock.Object,
                        ZoneLookup("Europe/Dublin").Object, fakeTime)
            .RecalculateForTodayAsync(Kiosk);

        sunCalcMock.Verify(x => x.CalculateBoundariesAsync(
            It.IsAny<double>(), It.IsAny<double>(), today, "Europe/Dublin"), Times.Once);
        repoMock.Verify(x => x.UpsertAsync(It.IsAny<DayTheme>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecalculateForTodayAsync_DoesNothing_WhenTheLocationWasJustCleared()
    {
        // DeleteLocation calls this. There is nothing to compute from, and the previous row must not
        // be refreshed with stale coordinates.
        var repoMock = new Mock<IDayThemeRepository>();
        var locationMock = new Mock<ILocationSettingRepository>();
        locationMock.Setup(x => x.GetAsync(Kiosk, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationSetting?)null);

        await CreateSut(repoMock.Object, locationMock.Object, new Mock<ISunCalculatorService>().Object)
            .RecalculateForTodayAsync(Kiosk);

        repoMock.Verify(x => x.UpsertAsync(It.IsAny<DayTheme>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------------------------------
    // Local-date derivation across midnight (FHQ-134 / FHQ-158), now driven by the kiosk's zone
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task EnsureTodayAsync_AfterLocalMidnightInUtcPlusZone_CreatesTheNewLocalDaysRecord()
    {
        // 23:30 UTC on the 21st is 09:30 on the 22nd in Sydney. The row must be keyed by the KIOSK's
        // local date, not the server's.
        var clock = new FakeTimeProvider(new DateTimeOffset(2024, 6, 21, 23, 30, 0, TimeSpan.Zero));
        var expected = new DateOnly(2024, 6, 22);
        var repoMock = new Mock<IDayThemeRepository>();
        var sunCalcMock = SunCalc(new TimeOnly(6, 30), new TimeOnly(7, 0), new TimeOnly(16, 0), new TimeOnly(17, 30));

        await CreateSut(repoMock.Object, LocationFor(Kiosk).Object, sunCalcMock.Object,
                        ZoneLookup("Australia/Sydney").Object, clock)
            .EnsureTodayAsync(Kiosk);

        repoMock.Verify(x => x.GetByDateAsync(Kiosk, expected, It.IsAny<CancellationToken>()), Times.Once);
        repoMock.Verify(x => x.UpsertAsync(It.Is<DayTheme>(d => d.Date == expected), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureTodayAsync_BeforeLocalMidnightInUtcMinusZone_CreatesThePreviousDaysRecord()
    {
        // 02:00 UTC on the 21st is still 22:00 on the 20th in New York.
        var clock = new FakeTimeProvider(new DateTimeOffset(2024, 6, 21, 2, 0, 0, TimeSpan.Zero));
        var expected = new DateOnly(2024, 6, 20);
        var repoMock = new Mock<IDayThemeRepository>();
        var sunCalcMock = SunCalc(new TimeOnly(5, 0), new TimeOnly(5, 30), new TimeOnly(19, 30), new TimeOnly(21, 0));

        await CreateSut(repoMock.Object, LocationFor(Kiosk).Object, sunCalcMock.Object,
                        ZoneLookup("America/New_York").Object, clock)
            .EnsureTodayAsync(Kiosk);

        repoMock.Verify(x => x.GetByDateAsync(Kiosk, expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureTodayAsync_WhenTheZoneIsUnusable_FallsBackToTheServerDate()
    {
        var (fakeTime, today) = PinnedToday();
        var repoMock = new Mock<IDayThemeRepository>();
        var sunCalcMock = SunCalc(new TimeOnly(4, 0), new TimeOnly(5, 0), new TimeOnly(21, 0), new TimeOnly(22, 30));

        await CreateSut(repoMock.Object, LocationFor(Kiosk).Object, sunCalcMock.Object,
                        ZoneLookup("Not/AZone").Object, fakeTime)
            .EnsureTodayAsync(Kiosk);

        repoMock.Verify(x => x.GetByDateAsync(Kiosk, today, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------------------------------
    // Period derivation
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task GetTodayAsync_DerivesPeriod_UsingLocalTimeInTheKiosksZone()
    {
        // 19:10 UTC on 27 Aug is 20:10 in Dublin (BST, UTC+1) — before a 20:46 night boundary, so
        // Evening. This is the exact reported scenario, and it is asserted under a NON-ZERO offset
        // on purpose: at UTC+0 the assertion passes whether or not the zone is honoured, which is
        // precisely why the production bug survived the original test suite.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 27, 19, 10, 0, TimeSpan.Zero));
        var today = new DateOnly(2026, 8, 27);
        var repoMock = new Mock<IDayThemeRepository>();
        repoMock.Setup(x => x.GetByDateAsync(Kiosk, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayTheme
            {
                UserId = Kiosk,
                Date = today,
                MorningStart = new TimeOnly(5, 51),
                DaytimeStart = new TimeOnly(6, 25),
                EveningStart = new TimeOnly(19, 12),
                NightStart = new TimeOnly(20, 46),
                IanaTimeZone = "Europe/Dublin"
            });

        var result = await CreateSut(repoMock.Object, LocationFor(Kiosk).Object,
                                     new Mock<ISunCalculatorService>().Object,
                                     ZoneLookup("Europe/Dublin").Object, clock)
            .GetTodayAsync(Kiosk);

        result.Should().NotBeNull();
        result!.CurrentPeriod.Should().Be("Evening",
            "20:10 local is before the 20:46 night boundary — reading the clock as UTC would say Night");
        result.NightStart.Should().Be(new TimeOnly(20, 46));
        result.IanaTimeZone.Should().Be("Europe/Dublin");
    }
}
