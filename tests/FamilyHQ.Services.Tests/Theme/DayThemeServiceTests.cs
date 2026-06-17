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
        var today = DateOnly.FromDateTime(DateTime.Today);
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

        await CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object, tzLookupMock.Object).EnsureTodayAsync();

        tzLookupMock.Verify(t => t.GetTimeZone(53.3498, -6.2603), Times.Once);
        sunCalcMock.Verify(x => x.CalculateBoundariesAsync(53.3498, -6.2603, today, "Europe/Dublin"), Times.Once);
        repoMock.Verify(r => r.UpsertAsync(
            It.Is<DayTheme>(dt => dt.IanaTimeZone == "Europe/Dublin"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTodayAsync_DerivesPeriod_UsingLocalTimeInConfiguredZone()
    {
        // Fix the clock at 04:50 UTC = 05:50 Europe/Dublin (BST, UTC+1).
        // MorningStart = 05:30 local. At 05:50 local the period should be Morning.
        // Without timezone fix it would compare against 04:50 UTC → before MorningStart → Night.
        var fixedUtc = new DateTimeOffset(2024, 6, 21, 4, 50, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(fixedUtc);

        var today = DateOnly.FromDateTime(DateTime.Today);
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
