namespace FamilyHQ.Services.Tests.Weather;

using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Weather;
using FluentAssertions;
using Moq;

public class WeatherServiceTests
{
    private static WeatherService CreateSut(
        Mock<IWeatherDataPointRepository> dataRepo,
        Mock<ILocationSettingRepository> locationRepo,
        Mock<ITimeZoneLookup> tzLookup,
        string userId = "user-1")
    {
        var weatherSettingRepoMock = new Mock<IWeatherSettingRepository>();
        weatherSettingRepoMock
            .Setup(x => x.GetOrCreateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WeatherSetting { TemperatureUnit = TemperatureUnit.Celsius });

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.UserId).Returns(userId);

        return new WeatherService(
            dataRepo.Object,
            weatherSettingRepoMock.Object,
            locationRepo.Object,
            currentUserMock.Object,
            tzLookup.Object);
    }

    [Fact]
    public async Task GetDailyForecastAsync_ThreadsIanaZoneFromLocationToRepository()
    {
        var location = new LocationSetting { Id = 1, UserId = "user-1", Latitude = 53.35, Longitude = -6.26 };

        var locationRepo = new Mock<ILocationSettingRepository>();
        locationRepo.Setup(x => x.GetAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        var tzLookup = new Mock<ITimeZoneLookup>();
        tzLookup.Setup(x => x.GetTimeZone(53.35, -6.26)).Returns("Europe/Dublin");

        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetDailyAsync(1, It.IsAny<int>(), "Europe/Dublin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut(dataRepo, locationRepo, tzLookup);

        await sut.GetDailyForecastAsync(days: 7);

        dataRepo.Verify(
            x => x.GetDailyAsync(1, It.IsAny<int>(), "Europe/Dublin", It.IsAny<CancellationToken>()),
            Times.Once,
            "timezone from location lat/long must be passed to GetDailyAsync");
    }

    [Fact]
    public async Task GetHourlyAsync_ThreadsIanaZoneFromLocationToRepository()
    {
        var location = new LocationSetting { Id = 1, UserId = "user-1", Latitude = 53.35, Longitude = -6.26 };
        var date = new DateOnly(2026, 6, 18);

        var locationRepo = new Mock<ILocationSettingRepository>();
        locationRepo.Setup(x => x.GetAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(location);

        var tzLookup = new Mock<ITimeZoneLookup>();
        tzLookup.Setup(x => x.GetTimeZone(53.35, -6.26)).Returns("Europe/Dublin");

        var dataRepo = new Mock<IWeatherDataPointRepository>();
        dataRepo.Setup(x => x.GetHourlyAsync(1, date, "Europe/Dublin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut(dataRepo, locationRepo, tzLookup);

        await sut.GetHourlyAsync(date);

        dataRepo.Verify(
            x => x.GetHourlyAsync(1, date, "Europe/Dublin", It.IsAny<CancellationToken>()),
            Times.Once,
            "timezone from location lat/long must be passed to repository");
    }

    [Fact]
    public async Task GetHourlyAsync_NullLocation_ReturnsEmpty()
    {
        var locationRepo = new Mock<ILocationSettingRepository>();
        locationRepo.Setup(x => x.GetAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationSetting?)null);

        var dataRepo = new Mock<IWeatherDataPointRepository>();
        var tzLookup = new Mock<ITimeZoneLookup>();
        var sut = CreateSut(dataRepo, locationRepo, tzLookup);

        var result = await sut.GetHourlyAsync(new DateOnly(2026, 6, 18));

        result.Should().BeEmpty();
        dataRepo.Verify(x => x.GetHourlyAsync(
            It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
