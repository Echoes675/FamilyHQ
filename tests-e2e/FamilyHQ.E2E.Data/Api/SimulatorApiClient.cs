using System.Net.Http.Json;
using FamilyHQ.E2E.Common.Configuration;
using FamilyHQ.E2E.Data.Models;

namespace FamilyHQ.E2E.Data.Api;

public class SimulatorApiClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public SimulatorApiClient()
    {
        var config = ConfigurationLoader.Load();
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(config.SimulatorApiUrl) };
    }

    /// <summary>
    /// Injects a pre-defined user configuration template into the dumb Simulator.
    /// This establishes the isolated data context for the subsequent E2E test scenario.
    /// Retries on transient failures (e.g. 504 Gateway Timeout during concurrent test startup burst).
    /// </summary>
    public async Task ConfigureUserTemplateAsync(object userTemplateConfig)
    {
        const int maxAttempts = 5;
        const int retryDelayMs = 2000;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var response = await _httpClient.PostAsJsonAsync("api/simulator/configure", userTemplateConfig);
            if (response.IsSuccessStatusCode)
                return;

            if (attempt == maxAttempts)
                response.EnsureSuccessStatusCode();

            await Task.Delay(retryDelayMs);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
