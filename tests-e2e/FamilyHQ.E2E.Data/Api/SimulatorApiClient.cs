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
    /// </summary>
    public async Task ConfigureUserTemplateAsync(object userTemplateConfig)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/simulator/configure", userTemplateConfig);
        response.EnsureSuccessStatusCode(); 
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
