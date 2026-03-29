using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Theme;
using FluentAssertions;
using Moq;

namespace FamilyHQ.Services.Tests.Theme;

public class LocationServiceTests
{
    private static LocationService CreateSut(ILocationSettingRepository repo, HttpClient httpClient)
        => new(repo, httpClient);

    [Fact]
    public async Task GetEffectiveLocationAsync_ReturnsSavedSetting_WhenPresent()
    {
        var saved = new LocationSetting
        {
            PlaceName = "Edinburgh, Scotland",
            Latitude = 55.9533,
            Longitude = -3.1883,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var repoMock = new Mock<ILocationSettingRepository>();
        repoMock.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(saved);

        var sut = CreateSut(repoMock.Object, new HttpClient(new NoOpHandler()));

        var result = await sut.GetEffectiveLocationAsync();

        result.PlaceName.Should().Be("Edinburgh, Scotland");
        result.Latitude.Should().Be(55.9533);
        result.IsAutoDetected.Should().BeFalse();
    }

    [Fact]
    public async Task GetEffectiveLocationAsync_ReturnsAutoDetected_WhenNoSetting()
    {
        var repoMock = new Mock<ILocationSettingRepository>();
        repoMock.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((LocationSetting?)null);

        var sut = CreateSut(repoMock.Object, new HttpClient(new FakeIpApiHandler()));

        var result = await sut.GetEffectiveLocationAsync();

        result.IsAutoDetected.Should().BeTrue();
        result.Latitude.Should().NotBe(0);
    }

    private class NoOpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    private class FakeIpApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = """{"status":"success","city":"London","regionName":"England","country":"United Kingdom","lat":51.5074,"lon":-0.1278}""";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
