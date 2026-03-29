using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class DayThemeControllerTests
{
    private readonly Mock<IDayThemeService> _serviceMock = new();

    [Fact]
    public async Task GetToday_ReturnsOk_WithDayThemeDto()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var dto = new DayThemeDto(today,
            new TimeOnly(5, 30), new TimeOnly(6, 45), new TimeOnly(20, 15), new TimeOnly(21, 30),
            "Daytime");
        _serviceMock.Setup(x => x.GetTodayAsync(It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var controller = new DayThemeController(_serviceMock.Object);
        var result = await controller.GetToday(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(dto);
    }
}
