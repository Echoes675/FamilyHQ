using FamilyHQ.Services.Theme;
using FluentAssertions;

namespace FamilyHQ.Services.Tests.Theme;

public class LocationServiceTests
{
    private static LocationService CreateSut(HttpClient httpClient) => new(httpClient);

    [Fact]
    public async Task GetEffectiveLocationAsync_ReturnsAutoDetected_FromIpApi()
    {
        var sut = CreateSut(new HttpClient(new FakeIpApiHandler()) { BaseAddress = new Uri("http://ip-api.com/") });

        var result = await sut.GetEffectiveLocationAsync();

        result.IsAutoDetected.Should().BeTrue();
        result.PlaceName.Should().Contain("London");
        result.Latitude.Should().NotBe(0);
    }

    [Fact]
    public async Task GetEffectiveLocationAsync_ThrowsInvalidOperationException_WhenIpApiStatusFails()
    {
        var sut = CreateSut(new HttpClient(new FakeIpApiFailureHandler()) { BaseAddress = new Uri("http://ip-api.com/") });

        var act = () => sut.GetEffectiveLocationAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*fail*");
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

    private class FakeIpApiFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = """{"status":"fail","message":"private range","query":"192.168.1.1"}""";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
