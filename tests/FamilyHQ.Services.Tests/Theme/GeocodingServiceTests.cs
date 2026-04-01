using FamilyHQ.Services.Theme;
using FluentAssertions;

namespace FamilyHQ.Services.Tests.Theme;

public class GeocodingServiceTests
{
    private static GeocodingService CreateSut(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://nominatim.openstreetmap.org") };
        return new(httpClient);
    }

    [Fact]
    public async Task GeocodeAsync_ReturnsCoordinates_ForKnownPlaceName()
    {
        var sut = CreateSut(new FakeNominatimHandler());

        var (lat, lon) = await sut.GeocodeAsync("Edinburgh, Scotland");

        lat.Should().BeApproximately(55.9533, 1.0);
        lon.Should().BeApproximately(-3.1883, 1.0);
    }

    [Fact]
    public async Task GeocodeAsync_ThrowsInvalidOperationException_WhenNoResultsFound()
    {
        var sut = CreateSut(new EmptyResultsHandler());

        var act = () => sut.GeocodeAsync("ZZZ-nonexistent-place-XYZ");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No geocoding results found*");
    }

    private class FakeNominatimHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = """[{"lat":"55.9533","lon":"-3.1883","display_name":"Edinburgh, Scotland"}]""";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private class EmptyResultsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = "[]";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
