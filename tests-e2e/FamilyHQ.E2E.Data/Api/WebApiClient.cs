using FamilyHQ.E2E.Common.Configuration;

namespace FamilyHQ.E2E.Data.Api;

public class WebApiClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public WebApiClient()
    {
        var config = ConfigurationLoader.Load();
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(config.ApiBaseUrl) };
    }

    /// <summary>
    /// Triggers an immediate weather data refresh on the WebApi.
    /// </summary>
    public async Task TriggerWeatherRefreshAsync()
    {
        var response = await _httpClient.PostAsync("api/weather/refresh", null);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
