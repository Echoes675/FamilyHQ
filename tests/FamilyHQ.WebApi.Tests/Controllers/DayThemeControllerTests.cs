using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class DayThemeControllerTests
{
    private const string KioskUserId = "kiosk-kitchen";

    [Fact]
    public async Task GetToday_ReturnsOk_WithDayThemeDto()
    {
        var (sut, serviceMock, _) = CreateSut();

        var date = new DateOnly(2026, 6, 15);
        var dto = new DayThemeDto(date,
            new TimeOnly(5, 30), new TimeOnly(6, 45), new TimeOnly(20, 15), new TimeOnly(21, 30),
            null,
            "Daytime");
        serviceMock.Setup(x => x.GetTodayAsync(KioskUserId, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var result = await sut.GetToday(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(dto);
    }

    [Fact]
    public async Task GetToday_AsksForTheCallersOwnTheme_NotAnyOtherKiosks()
    {
        // FHQ-177: the whole point of scoping. If the controller ever resolved the row by anything
        // other than the authenticated caller, a second kiosk would be served the first one's
        // sunrise/sunset — which is the class of bug this ticket exists to fix.
        var (sut, serviceMock, _) = CreateSut();
        serviceMock
            .Setup(x => x.GetTodayAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayThemeDto?)null);

        await sut.GetToday(CancellationToken.None);

        serviceMock.Verify(x => x.GetTodayAsync(KioskUserId, It.IsAny<CancellationToken>()), Times.Once);
        serviceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetToday_ReturnsNoContent_WhenTheKioskHasNoSavedLocation()
    {
        // Not an error: a kiosk with no location has no theme, and the client falls back to its
        // default rather than showing a failure on a wall display.
        var (sut, serviceMock, _) = CreateSut();
        serviceMock
            .Setup(x => x.GetTodayAsync(KioskUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DayThemeDto?)null);

        var result = await sut.GetToday(CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    private static (DayThemeController sut, Mock<IDayThemeService> serviceMock, Mock<ICurrentUserService> userMock) CreateSut()
    {
        var serviceMock = new Mock<IDayThemeService>();
        var userMock = new Mock<ICurrentUserService>();
        userMock.SetupGet(x => x.UserId).Returns(KioskUserId);
        var loggerMock = new Mock<ILogger<DayThemeController>>();

        var sut = new DayThemeController(
            serviceMock.Object,
            userMock.Object,
            loggerMock.Object);

        return (sut, serviceMock, userMock);
    }
}
