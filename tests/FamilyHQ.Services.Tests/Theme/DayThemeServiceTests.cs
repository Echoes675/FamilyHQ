using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Theme;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FamilyHQ.Services.Tests.Theme;

public class DayThemeServiceTests
{
    private static DayThemeService CreateSut(
        IDayThemeRepository dayThemeRepo,
        ILocationService locationService,
        ISunCalculatorService sunCalculator,
        ITimeZoneLookup? timeZoneLookup = null,
        TimeProvider? timeProvider = null)
        => new(dayThemeRepo, locationService, sunCalculator,
               timeZoneLookup ?? new Mock<ITimeZoneLookup>().Object,
               timeProvider ?? TimeProvider.System);

    [Fact]
    public async Task EnsureTodayAsync_DoesNotRecalculate_WhenRecordExists()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var repoMock = new Mock<IDayThemeRepository>();
        repoMock.Setup(x => x.GetByDateAsync(today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayTheme { Date = today });
        var locationMock = new Mock<ILocationService>();
        var sunCalcMock = new Mock<ISunCalculatorService>();

        await CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object).EnsureTodayAsync();

        sunCalcMock.Verify(x => x.CalculateBoundariesAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<DateOnly>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task EnsureTodayAsync_Calculates_WhenNoRecordExists()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var repoMock = new Mock<IDayThemeRepository>();
        repoMock.Setup(x => x.GetByDateAsync(today, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayTheme?)null);
        repoMock.Setup(x => x.UpsertAsync(It.IsAny<DayTheme>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayTheme dt, CancellationToken _) => dt);
        var locationMock = new Mock<ILocationService>();
        locationMock.Setup(x => x.GetEffectiveLocationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationResult("Test", 55.0, -3.0, false, IanaTimeZone: null));
        var sunCalcMock = new Mock<ISunCalculatorService>();
        sunCalcMock.Setup(x => x.CalculateBoundariesAsync(55.0, -3.0, today, It.IsAny<string?>()))
            .ReturnsAsync(new DayThemeBoundaries(
                new TimeOnly(5, 0), new TimeOnly(6, 30), new TimeOnly(20, 0), new TimeOnly(21, 30)));

        await CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object).EnsureTodayAsync();

        sunCalcMock.Verify(x => x.CalculateBoundariesAsync(55.0, -3.0, today, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task RecalculateForTodayAsync_AlwaysRecalculates_EvenWhenRecordExists()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var repoMock = new Mock<IDayThemeRepository>();
        // Record EXISTS — but RecalculateForToday should still call CalculateBoundariesAsync
        repoMock.Setup(x => x.GetByDateAsync(today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayTheme { Date = today });
        repoMock.Setup(x => x.UpsertAsync(It.IsAny<DayTheme>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayTheme dt, CancellationToken _) => dt);
        var locationMock = new Mock<ILocationService>();
        locationMock.Setup(x => x.GetEffectiveLocationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationResult("Test", 55.0, -3.0, false, IanaTimeZone: null));
        var sunCalcMock = new Mock<ISunCalculatorService>();
        // Adapt the setup call to match the actual ISunCalculatorService method signature
        sunCalcMock.Setup(x => x.CalculateBoundariesAsync(55.0, -3.0, today, It.IsAny<string?>()))
            .ReturnsAsync(new DayThemeBoundaries(
                new TimeOnly(5, 0), new TimeOnly(6, 30), new TimeOnly(20, 0), new TimeOnly(21, 30)));

        await CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object).RecalculateForTodayAsync();

        sunCalcMock.Verify(x => x.CalculateBoundariesAsync(55.0, -3.0, today, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task CalculateAndPersistAsync_CallsTimeZoneLookup_WithLocationCoordinates()
    {
        // Clock pinned at midday UTC so the Europe/Dublin local date matches the UTC date — this test
        // is about the coordinate plumbing, not the date key (FHQ-134 covers that separately).
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 21, 12, 0, 0, TimeSpan.Zero));
        var today = new DateOnly(2024, 6, 21);
        var repoMock = new Mock<IDayThemeRepository>();
        repoMock.Setup(x => x.GetByDateAsync(today, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayTheme?)null);
        repoMock.Setup(x => x.UpsertAsync(It.IsAny<DayTheme>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayTheme dt, CancellationToken _) => dt);
        var locationMock = new Mock<ILocationService>();
        locationMock.Setup(x => x.GetEffectiveLocationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationResult("Dublin", 53.3498, -6.2603, false, null));
        var tzLookupMock = new Mock<ITimeZoneLookup>();
        tzLookupMock.Setup(t => t.GetTimeZone(53.3498, -6.2603)).Returns("Europe/Dublin");
        var sunCalcMock = new Mock<ISunCalculatorService>();
        sunCalcMock.Setup(x => x.CalculateBoundariesAsync(53.3498, -6.2603, today, "Europe/Dublin"))
            .ReturnsAsync(new DayThemeBoundaries(
                new TimeOnly(5, 30), new TimeOnly(6, 0), new TimeOnly(20, 0), new TimeOnly(21, 0)));

        await CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object, tzLookupMock.Object, fakeTime)
            .EnsureTodayAsync();

        tzLookupMock.Verify(t => t.GetTimeZone(53.3498, -6.2603), Times.Once);
        sunCalcMock.Verify(x => x.CalculateBoundariesAsync(53.3498, -6.2603, today, "Europe/Dublin"), Times.Once);
        repoMock.Verify(r => r.UpsertAsync(
            It.Is<DayTheme>(dt => dt.IanaTimeZone == "Europe/Dublin"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── FHQ-134: the date key must be the family's LOCAL date, not the server's UTC date ──────────
    //
    // On the UTC Docker host GetLocalNow() is UTC, so between 23:00 and 00:00 UTC a UTC+1 family has
    // already crossed local midnight while the service still looks up yesterday's row. The zone comes
    // from the most recent stored DayTheme row: a cheap indexed read that needs no HTTP context (the
    // scheduler has none) and no network (ip-api is banned from hot paths).

    [Fact]
    public async Task EnsureTodayAsync_AfterLocalMidnightInUtcPlusZone_CreatesTheNewLocalDaysRecord()
    {
        // 23:30 UTC on 2024-06-20 = 00:30 on 2024-06-21 in Europe/Dublin (BST, UTC+1).
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 20, 23, 30, 0, TimeSpan.Zero));
        var localToday = new DateOnly(2024, 6, 21);
        var repoMock = CreateRepoMock(new DayTheme { Date = new DateOnly(2024, 6, 20), IanaTimeZone = "Europe/Dublin" });
        repoMock.Setup(x => x.GetByDateAsync(localToday, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayTheme?)null);
        var (locationMock, tzLookupMock, sunCalcMock) = CreateDublinDependencies();

        await CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object, tzLookupMock.Object, fakeTime)
            .EnsureTodayAsync();

        repoMock.Verify(r => r.UpsertAsync(
            It.Is<DayTheme>(dt => dt.Date == localToday), It.IsAny<CancellationToken>()), Times.Once);
        sunCalcMock.Verify(x => x.CalculateBoundariesAsync(
            It.IsAny<double>(), It.IsAny<double>(), localToday, "Europe/Dublin"), Times.Once);
    }

    [Fact]
    public async Task EnsureTodayAsync_BeforeLocalMidnightInUtcMinusZone_CreatesThePreviousDaysRecord()
    {
        // 02:00 UTC on 2024-06-21 = 22:00 on 2024-06-20 in America/New_York (EDT, UTC-4). The UTC date
        // has already rolled over; the family's has not. Guards against a one-directional fix.
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 21, 2, 0, 0, TimeSpan.Zero));
        var localToday = new DateOnly(2024, 6, 20);
        var repoMock = CreateRepoMock(new DayTheme { Date = new DateOnly(2024, 6, 19), IanaTimeZone = "America/New_York" });
        repoMock.Setup(x => x.GetByDateAsync(localToday, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayTheme?)null);
        var (locationMock, tzLookupMock, sunCalcMock) = CreateNewYorkDependencies();

        await CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object, tzLookupMock.Object, fakeTime)
            .EnsureTodayAsync();

        repoMock.Verify(r => r.UpsertAsync(
            It.Is<DayTheme>(dt => dt.Date == localToday), It.IsAny<CancellationToken>()), Times.Once);
        repoMock.Verify(r => r.UpsertAsync(
            It.Is<DayTheme>(dt => dt.Date == new DateOnly(2024, 6, 21)), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureTodayAsync_AfterLocalMidnightInUtcPlusZone_DoesNotReturnEarlyOnYesterdaysRecord()
    {
        // The reported symptom: yesterday's row exists, EnsureTodayAsync finds it under the UTC date
        // key and returns early, so today's row is never created and the family keeps yesterday's
        // sunrise/sunset boundaries for up to an hour.
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 20, 23, 30, 0, TimeSpan.Zero));
        var yesterday = new DayTheme { Date = new DateOnly(2024, 6, 20), IanaTimeZone = "Europe/Dublin" };
        var repoMock = CreateRepoMock(yesterday);
        repoMock.Setup(x => x.GetByDateAsync(new DateOnly(2024, 6, 20), It.IsAny<CancellationToken>()))
            .ReturnsAsync(yesterday);
        repoMock.Setup(x => x.GetByDateAsync(new DateOnly(2024, 6, 21), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayTheme?)null);
        var (locationMock, tzLookupMock, sunCalcMock) = CreateDublinDependencies();

        await CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object, tzLookupMock.Object, fakeTime)
            .EnsureTodayAsync();

        sunCalcMock.Verify(x => x.CalculateBoundariesAsync(
            It.IsAny<double>(), It.IsAny<double>(), new DateOnly(2024, 6, 21), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task GetTodayAsync_AfterLocalMidnightInUtcPlusZone_ReturnsTheNewLocalDaysRecord()
    {
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 20, 23, 30, 0, TimeSpan.Zero));
        var localToday = new DateOnly(2024, 6, 21);
        var todaysRecord = new DayTheme
        {
            Date = localToday,
            MorningStart = new TimeOnly(4, 15),
            DaytimeStart = new TimeOnly(5, 45),
            EveningStart = new TimeOnly(20, 45),
            NightStart = new TimeOnly(22, 15),
            IanaTimeZone = "Europe/Dublin"
        };
        var repoMock = CreateRepoMock(todaysRecord);
        repoMock.Setup(x => x.GetByDateAsync(localToday, It.IsAny<CancellationToken>()))
            .ReturnsAsync(todaysRecord);

        var result = await CreateSut(
            repoMock.Object,
            new Mock<ILocationService>().Object,
            new Mock<ISunCalculatorService>().Object,
            timeProvider: fakeTime).GetTodayAsync();

        result.Date.Should().Be(localToday, "23:30 UTC is already 00:30 the next day in Europe/Dublin");
        result.CurrentPeriod.Should().Be("Night", "00:30 local is before MorningStart");
    }

    [Fact]
    public async Task RecalculateForTodayAsync_AfterLocalMidnightInUtcPlusZone_RecalculatesTheNewLocalDay()
    {
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 20, 23, 30, 0, TimeSpan.Zero));
        var repoMock = CreateRepoMock(new DayTheme { Date = new DateOnly(2024, 6, 20), IanaTimeZone = "Europe/Dublin" });
        var (locationMock, tzLookupMock, sunCalcMock) = CreateDublinDependencies();

        await CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object, tzLookupMock.Object, fakeTime)
            .RecalculateForTodayAsync();

        repoMock.Verify(r => r.UpsertAsync(
            It.Is<DayTheme>(dt => dt.Date == new DateOnly(2024, 6, 21)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureTodayAsync_OnFirstEverBoot_PersistsUnderTheResolvedZonesLocalDate()
    {
        // Bootstrap: the table is empty, so there is no stored zone to derive the key from and the
        // probe falls back to the server date. CalculateAndPersistAsync resolves the zone from the
        // location anyway, so it re-derives the date from THAT zone before computing and storing —
        // the first-ever row lands on the correct local date rather than a wrong-dated one that
        // GetTodayAsync would then fail to find.
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 20, 23, 30, 0, TimeSpan.Zero));
        var repoMock = CreateRepoMock(mostRecent: null);
        repoMock.Setup(x => x.GetByDateAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayTheme?)null);
        var (locationMock, tzLookupMock, sunCalcMock) = CreateDublinDependencies();

        await CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object, tzLookupMock.Object, fakeTime)
            .EnsureTodayAsync();

        repoMock.Verify(r => r.UpsertAsync(
            It.Is<DayTheme>(dt => dt.Date == new DateOnly(2024, 6, 21) && dt.IanaTimeZone == "Europe/Dublin"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureTodayAsync_WhenStoredZoneIsUnusable_FallsBackToTheServerDate()
    {
        // A row with no zone (or an unknown id) must not throw — fall back to the previous behaviour.
        // FakeTimeProvider's LocalTimeZone is UTC, so the server date here is the UTC date.
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 20, 23, 30, 0, TimeSpan.Zero));
        var repoMock = CreateRepoMock(new DayTheme { Date = new DateOnly(2024, 6, 19), IanaTimeZone = "Mars/Olympus_Mons" });
        repoMock.Setup(x => x.GetByDateAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayTheme?)null);
        var locationMock = new Mock<ILocationService>();
        locationMock.Setup(x => x.GetEffectiveLocationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationResult("Test", 55.0, -3.0, false, null));
        var sunCalcMock = new Mock<ISunCalculatorService>();
        sunCalcMock.Setup(x => x.CalculateBoundariesAsync(
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<DateOnly>(), It.IsAny<string?>()))
            .ReturnsAsync(new DayThemeBoundaries(
                new TimeOnly(5, 0), new TimeOnly(6, 30), new TimeOnly(20, 0), new TimeOnly(21, 30)));

        await CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object, timeProvider: fakeTime)
            .EnsureTodayAsync();

        repoMock.Verify(r => r.UpsertAsync(
            It.Is<DayTheme>(dt => dt.Date == new DateOnly(2024, 6, 20)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecalculateForTodayAsync_WhenTheZoneChangeMovesTheLocalDate_RefreshesBothDaysZone()
    {
        // Westward move (Europe/Dublin -> America/New_York) made inside the divergence window: at
        // 00:30 UTC the old zone says 2024-06-21 but the new zone says 2024-06-20. The new day's row
        // is written for 06-20, but 06-21's row is the one the NEXT date-key derivation reads its zone
        // from — leaving it on the old zone would pin the whole app to the abandoned zone until that
        // zone's next midnight. Both rows are refreshed so the derivation converges immediately.
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2024, 6, 21, 0, 30, 0, TimeSpan.Zero));
        var repoMock = CreateRepoMock(new DayTheme { Date = new DateOnly(2024, 6, 21), IanaTimeZone = "Europe/Dublin" });
        var (locationMock, tzLookupMock, sunCalcMock) = CreateNewYorkDependencies();

        await CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object, tzLookupMock.Object, fakeTime)
            .RecalculateForTodayAsync();

        repoMock.Verify(r => r.UpsertAsync(
            It.Is<DayTheme>(dt => dt.Date == new DateOnly(2024, 6, 20) && dt.IanaTimeZone == "America/New_York"),
            It.IsAny<CancellationToken>()), Times.Once);
        repoMock.Verify(r => r.UpsertAsync(
            It.Is<DayTheme>(dt => dt.Date == new DateOnly(2024, 6, 21) && dt.IanaTimeZone == "America/New_York"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IDayThemeRepository> CreateRepoMock(DayTheme? mostRecent)
    {
        var repoMock = new Mock<IDayThemeRepository>();
        repoMock.Setup(x => x.GetMostRecentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(mostRecent);
        repoMock.Setup(x => x.UpsertAsync(It.IsAny<DayTheme>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayTheme dt, CancellationToken _) => dt);
        return repoMock;
    }

    private static (Mock<ILocationService>, Mock<ITimeZoneLookup>, Mock<ISunCalculatorService>) CreateDublinDependencies()
        => CreateZonedDependencies("Dublin", 53.3498, -6.2603, "Europe/Dublin");

    private static (Mock<ILocationService>, Mock<ITimeZoneLookup>, Mock<ISunCalculatorService>) CreateNewYorkDependencies()
        => CreateZonedDependencies("New York", 40.7128, -74.0060, "America/New_York");

    private static (Mock<ILocationService>, Mock<ITimeZoneLookup>, Mock<ISunCalculatorService>) CreateZonedDependencies(
        string placeName, double latitude, double longitude, string ianaTimeZone)
    {
        var locationMock = new Mock<ILocationService>();
        locationMock.Setup(x => x.GetEffectiveLocationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationResult(placeName, latitude, longitude, false, null));
        var tzLookupMock = new Mock<ITimeZoneLookup>();
        tzLookupMock.Setup(t => t.GetTimeZone(latitude, longitude)).Returns(ianaTimeZone);
        var sunCalcMock = new Mock<ISunCalculatorService>();
        sunCalcMock.Setup(x => x.CalculateBoundariesAsync(
                latitude, longitude, It.IsAny<DateOnly>(), ianaTimeZone))
            .ReturnsAsync(new DayThemeBoundaries(
                new TimeOnly(5, 30), new TimeOnly(6, 0), new TimeOnly(20, 0), new TimeOnly(21, 30)));
        return (locationMock, tzLookupMock, sunCalcMock);
    }

    [Fact]
    public async Task GetTodayAsync_DerivesPeriod_UsingLocalTimeInConfiguredZone()
    {
        // Fix the clock at 04:50 UTC = 05:50 Europe/Dublin (BST, UTC+1).
        // MorningStart = 05:30 local. At 05:50 local the period should be Morning.
        // Without timezone fix it would compare against 04:50 UTC → before MorningStart → Night.
        var fixedUtc = new DateTimeOffset(2024, 6, 21, 4, 50, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(fixedUtc);

        var today = DateOnly.FromDateTime(fakeTime.GetLocalNow().DateTime);
        var repoMock = new Mock<IDayThemeRepository>();
        repoMock.Setup(x => x.GetByDateAsync(today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayTheme
            {
                Date = today,
                MorningStart = new TimeOnly(5, 30),
                DaytimeStart = new TimeOnly(6, 0),
                EveningStart = new TimeOnly(20, 0),
                NightStart = new TimeOnly(21, 30),
                IanaTimeZone = "Europe/Dublin"
            });

        var result = await CreateSut(
            repoMock.Object,
            new Mock<ILocationService>().Object,
            new Mock<ISunCalculatorService>().Object,
            timeProvider: fakeTime).GetTodayAsync();

        result.CurrentPeriod.Should().Be("Morning",
            "04:50 UTC = 05:50 BST, which is after MorningStart (05:30) but before DaytimeStart (06:00)");
    }
}
