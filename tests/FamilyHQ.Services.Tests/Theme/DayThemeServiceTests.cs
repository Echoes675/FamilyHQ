using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Theme;
using FluentAssertions;
using Moq;

namespace FamilyHQ.Services.Tests.Theme;

public class DayThemeServiceTests
{
    private static DayThemeService CreateSut(
        IDayThemeRepository dayThemeRepo,
        ILocationService locationService,
        ISunCalculatorService sunCalculator)
        => new(dayThemeRepo, locationService, sunCalculator);

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

        sunCalcMock.Verify(x => x.CalculateBoundariesAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<DateOnly>()), Times.Never);
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
            .ReturnsAsync(new LocationResult("Test", 55.0, -3.0, false));
        var sunCalcMock = new Mock<ISunCalculatorService>();
        sunCalcMock.Setup(x => x.CalculateBoundariesAsync(55.0, -3.0, today))
            .ReturnsAsync(new DayThemeBoundaries(
                new TimeOnly(5, 0), new TimeOnly(6, 30), new TimeOnly(20, 0), new TimeOnly(21, 30)));

        await CreateSut(repoMock.Object, locationMock.Object, sunCalcMock.Object).EnsureTodayAsync();

        sunCalcMock.Verify(x => x.CalculateBoundariesAsync(55.0, -3.0, today), Times.Once);
    }

    [Fact]
    public async Task GetTodayAsync_ReturnsDaytimePeriod_WhenNowBetweenSunriseAndEvening()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var now = DateTime.Now;
        var repoMock = new Mock<IDayThemeRepository>();
        repoMock.Setup(x => x.GetByDateAsync(today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayTheme
            {
                Date = today,
                MorningStart = TimeOnly.FromDateTime(now.AddHours(-3)),
                DaytimeStart = TimeOnly.FromDateTime(now.AddHours(-1)),
                EveningStart = TimeOnly.FromDateTime(now.AddHours(1)),
                NightStart = TimeOnly.FromDateTime(now.AddHours(3))
            });

        var result = await CreateSut(repoMock.Object, new Mock<ILocationService>().Object, new Mock<ISunCalculatorService>().Object).GetTodayAsync();

        result.CurrentPeriod.Should().Be("Daytime");
    }
}
