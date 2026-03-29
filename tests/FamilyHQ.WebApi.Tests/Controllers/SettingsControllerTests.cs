using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class SettingsControllerTests
{
    private readonly Mock<ILocationSettingRepository> _locationRepoMock = new();
    private readonly Mock<IGeocodingService> _geocodingMock = new();
    private readonly Mock<IDayThemeService> _dayThemeServiceMock = new();
    private readonly Mock<IDayThemeScheduler> _schedulerMock = new();
    private readonly Mock<IHubContext<FamilyHQ.WebApi.Hubs.CalendarHub>> _hubMock = new();

    private SettingsController CreateSut() =>
        new(_locationRepoMock.Object, _geocodingMock.Object, _dayThemeServiceMock.Object,
            _schedulerMock.Object, _hubMock.Object);

    [Fact]
    public async Task GetLocation_Returns404_WhenNotSet()
    {
        _locationRepoMock.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationSetting?)null);

        var result = await CreateSut().GetLocation(CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetLocation_ReturnsOk_WhenSet()
    {
        _locationRepoMock.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationSetting { PlaceName = "Edinburgh, Scotland", Latitude = 55.9, Longitude = -3.2 });

        var result = await CreateSut().GetLocation(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<LocationSettingDto>()
            .Which.PlaceName.Should().Be("Edinburgh, Scotland");
    }

    [Fact]
    public async Task SaveLocation_Geocodes_SavesPersists_AndTriggers()
    {
        _geocodingMock.Setup(x => x.GeocodeAsync("Edinburgh, Scotland", It.IsAny<CancellationToken>()))
            .ReturnsAsync((55.9533, -3.1883));
        _locationRepoMock.Setup(x => x.UpsertAsync(It.IsAny<LocationSetting>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationSetting ls, CancellationToken _) => ls);
        _dayThemeServiceMock.Setup(x => x.RecalculateForTodayAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _dayThemeServiceMock.Setup(x => x.GetTodayAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DayThemeDto(DateOnly.FromDateTime(DateTime.Today),
                new TimeOnly(5, 0), new TimeOnly(6, 30), new TimeOnly(20, 0), new TimeOnly(21, 30), "Daytime"));
        _schedulerMock.Setup(x => x.TriggerRecalculationAsync()).Returns(Task.CompletedTask);

        var clientsMock = new Mock<IHubClients>();
        var clientMock = new Mock<IClientProxy>();
        clientsMock.Setup(x => x.All).Returns(clientMock.Object);
        _hubMock.Setup(x => x.Clients).Returns(clientsMock.Object);

        var result = await CreateSut().SaveLocation(new SaveLocationRequest("Edinburgh, Scotland"), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _schedulerMock.Verify(x => x.TriggerRecalculationAsync(), Times.Once);
    }
}
