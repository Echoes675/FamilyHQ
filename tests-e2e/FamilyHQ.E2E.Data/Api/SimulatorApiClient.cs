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
    /// Sets the correlation ID for all subsequent requests from this client instance.
    /// This ensures API-only tests supply the TestCorrelationId.
    /// </summary>
    public void SetCorrelationId(string correlationId)
    {
        _httpClient.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _httpClient.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
    }

    /// <summary>
    /// Injects a pre-defined user configuration template into the dumb Simulator.
    /// This establishes the isolated data context for the subsequent E2E test scenario.
    /// </summary>
    public async Task ConfigureUserTemplateAsync(object userTemplateConfig)
    {
        var response = await _httpClient.PostAsJsonAsync("api/simulator/configure", userTemplateConfig);
        response.EnsureSuccessStatusCode(); 
    }

    /// <summary>
    /// Adds a new event directly to the Simulator via the back-door endpoint.
    /// Returns the newly created event's ID.
    /// </summary>
    public async Task<string> AddEventAsync(
        string userId, string calendarId, string summary,
        DateTime start, DateTime end, bool isAllDay)
    {
        var body = new
        {
            UserId = userId,
            CalendarId = calendarId,
            Summary = summary,
            Start = start,
            End = end,
            IsAllDay = isAllDay
        };
        var response = await _httpClient.PostAsJsonAsync("api/simulator/backdoor/events", body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Updates the summary of an existing event via the back-door endpoint.
    /// </summary>
    public async Task UpdateEventAsync(string userId, string eventId, string newSummary)
    {
        var body = new { UserId = userId, Summary = newSummary };
        var response = await _httpClient.PutAsJsonAsync(
            $"api/simulator/backdoor/events/{eventId}", body);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Deletes an event via the back-door endpoint.
    /// </summary>
    public async Task DeleteEventAsync(string userId, string eventId)
    {
        var response = await _httpClient.DeleteAsync(
            $"api/simulator/backdoor/events/{eventId}?userId={userId}");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Triggers the Simulator to fire a push notification to the WebApi webhook endpoint,
    /// which causes the WebApi to run SyncAllAsync and notify clients via SignalR.
    /// </summary>
    public async Task TriggerWebhookAsync()
    {
        // "simulate/push" — no leading slash, resolves against BaseAddress correctly
        var response = await _httpClient.PostAsync("simulate/push", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Seeds weather data for a specific location via the Simulator backdoor.
    /// </summary>
    public async Task SetWeatherAsync(object weatherRequest)
    {
        var response = await _httpClient.PostAsJsonAsync("api/simulator/backdoor/weather", weatherRequest);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Clears seeded weather data for a location via the Simulator backdoor.
    /// </summary>
    public async Task ClearWeatherAsync(double latitude, double longitude)
    {
        var response = await _httpClient.DeleteAsync(
            $"api/simulator/backdoor/weather?latitude={latitude}&longitude={longitude}");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Seeds a geocoding result for a place name via the Simulator backdoor.
    /// </summary>
    public async Task SetLocationAsync(string placeName, double latitude, double longitude)
    {
        var body = new { PlaceName = placeName, Latitude = latitude, Longitude = longitude };
        var response = await _httpClient.PostAsJsonAsync("api/simulator/backdoor/location", body);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Clears a seeded geocoding result via the Simulator backdoor.
    /// </summary>
    public async Task ClearLocationAsync(string placeName)
    {
        var response = await _httpClient.DeleteAsync(
            $"api/simulator/backdoor/location?placeName={Uri.EscapeDataString(placeName)}");
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
