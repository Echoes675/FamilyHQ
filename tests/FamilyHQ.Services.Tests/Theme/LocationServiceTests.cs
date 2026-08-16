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

    // FHQ-114: ip-api documents exactly three `status: "fail"` messages — "private range",
    // "reserved range" and "invalid query" — and every one of them is permanent for the querying
    // IP, so a retry can only ever return the same answer. Rate limiting is signalled separately as
    // HTTP 429 (+ X-Ttl) and is handled by TransientHttpRetryHandler. The body-level failure
    // therefore stays a clean, immediate failure — but it must say WHY.
    [Fact]
    public async Task GetEffectiveLocationAsync_IpApiStatusFails_ReportsTheDocumentedReason()
    {
        var sut = CreateSut(new HttpClient(new FakeIpApiFailureHandler()) { BaseAddress = new Uri("http://ip-api.com/") });

        var act = () => sut.GetEffectiveLocationAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*private range*");
    }

    [Fact]
    public async Task GetEffectiveLocationAsync_Request_AsksForTheFailureMessageField()
    {
        // API contract pin: ip-api only returns `message` when it is in the requested `fields` list,
        // so omitting it leaves every failure diagnosed as a bare "fail" in Seq.
        var handler = new FakeIpApiHandler();
        var sut = CreateSut(new HttpClient(handler) { BaseAddress = new Uri("http://ip-api.com/") });

        await sut.GetEffectiveLocationAsync();

        handler.LastRequestUri.Should().NotBeNull();
        handler.LastRequestUri!.Query.Should().Contain("message");
    }

    private class FakeIpApiHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUri = request.RequestUri;
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
