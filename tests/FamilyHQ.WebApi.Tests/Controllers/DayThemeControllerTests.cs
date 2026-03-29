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
    [Fact]
    public async Task GetToday_ReturnsOk_WithDayThemeDto()
    {
        // Arrange
        var (sut, serviceMock) = CreateSut();

        var date = new DateOnly(2026, 6, 15);
        var dto = new DayThemeDto(date,
            new TimeOnly(5, 30), new TimeOnly(6, 45), new TimeOnly(20, 15), new TimeOnly(21, 30),
            "Daytime");
        serviceMock.Setup(x => x.GetTodayAsync(It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        // Act
        var result = await sut.GetToday(CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(dto);
    }

    private static (DayThemeController sut, Mock<IDayThemeService> serviceMock) CreateSut()
    {
        var serviceMock = new Mock<IDayThemeService>();
        var loggerMock = new Mock<ILogger<DayThemeController>>();

        var sut = new DayThemeController(
            serviceMock.Object,
            loggerMock.Object);

        return (sut, serviceMock);
    }
}
