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
    /// FHQ-43: reads the stored event matching userId + calendarId + summary from the Simulator
    /// backdoor, returning the IANA <c>StartTimeZone</c> the app anchored the (recurring) timed
    /// event to. Returns null when no matching event exists yet, so callers can poll for the
    /// asynchronous create to land.
    /// </summary>
    public async Task<string?> GetEventStartTimeZoneAsync(string userId, string calendarId, string summary)
    {
        var url = $"api/simulator/backdoor/events" +
                  $"?userId={Uri.EscapeDataString(userId)}" +
                  $"&calendarId={Uri.EscapeDataString(calendarId)}" +
                  $"&summary={Uri.EscapeDataString(summary)}";
        var response = await _httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BackdoorEventResponse>();
        return result?.StartTimeZone;
    }

    private sealed class BackdoorEventResponse
    {
        public string? Id { get; set; }
        public string? CalendarId { get; set; }
        public string? Summary { get; set; }
        public string? StartTimeZone { get; set; }
        public string? RecurrenceRule { get; set; }
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

    /// <summary>
    /// Retrieves all webhook channel registrations from the Simulator backdoor.
    /// </summary>
    public async Task<List<WebhookRegistrationDto>> GetWebhookRegistrationsAsync()
    {
        var response = await _httpClient.GetAsync("api/simulator/backdoor/webhooks");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<WebhookRegistrationDto>>() ?? [];
    }

    /// <summary>
    /// Instructs the Simulator to inject a sync failure mode for the given user.
    /// Valid mode names: "RefreshTokenInvalidGrant", "CalendarApi401", "CalendarApi403".
    /// </summary>
    public async Task SetSyncFailureModeAsync(string userId, string mode)
    {
        var body = new { UserId = userId, Mode = mode };
        var response = await _httpClient.PostAsJsonAsync(
            "api/simulator/backdoor/sync-failure-mode", body);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Clears any injected sync failure mode for the given user.
    /// </summary>
    public async Task ClearSyncFailureModeAsync(string userId)
    {
        var response = await _httpClient.DeleteAsync(
            $"api/simulator/backdoor/sync-failure-mode?userId={Uri.EscapeDataString(userId)}");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Clears all injected sync failure modes across all users.
    /// Intended for scenario teardown.
    /// </summary>
    public async Task ClearAllSyncFailureModesAsync()
    {
        var response = await _httpClient.DeleteAsync("api/simulator/backdoor/sync-failure-mode");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Adds a poisoned event whose Summary length exceeds the WebApi's
    /// CalendarEvent.Title column limit. On the next sync, EF Core will throw
    /// a DbUpdateException for this single event, exercising the per-event
    /// resilience path. Returns the new event's ID.
    /// </summary>
    public async Task<string> AddPoisonEventAsync(string userId, string calendarId)
    {
        var body = new { UserId = userId, CalendarId = calendarId };
        var response = await _httpClient.PostAsJsonAsync(
            "api/simulator/backdoor/events/poison", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync()).Trim('"');
    }

    /// <summary>
    /// Returns the number of outbound writes (PUT/POST) the Simulator has received for
    /// the given event ID since the last reset. Used by WebhookEchoGuard E2E assertions.
    /// </summary>
    public async Task<int> GetOutboundWriteCountAsync(string eventId)
    {
        var response = await _httpClient.GetAsync(
            $"api/simulator/backdoor/write-counts/{Uri.EscapeDataString(eventId)}");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WriteCountResponse>();
        return result?.WriteCount ?? 0;
    }

    /// <summary>
    /// Returns the total outbound writes (PUT/POST) the Simulator has received for a single user
    /// since the last reset. Used by recurring-events echo-guard assertions where the written
    /// event's ID is server-generated (native series creation); scoping to the scenario's isolated
    /// user keeps the count safe under the parallel E2E runner (a global total would be contaminated).
    /// </summary>
    public async Task<int> GetUserOutboundWriteCountAsync(string userId)
    {
        var response = await _httpClient.GetAsync(
            $"api/simulator/backdoor/write-counts/user/{Uri.EscapeDataString(userId)}/total");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WriteCountResponse>();
        return result?.WriteCount ?? 0;
    }

    /// <summary>
    /// Resets the outbound write count for a single (isolated test) user. Call in AfterScenario hooks;
    /// scoping to the scenario's own user avoids the parallel-runner ClearAll race (FHQ-31).
    /// </summary>
    public async Task ResetUserOutboundWriteCountsAsync(string userId)
    {
        var response = await _httpClient.DeleteAsync(
            $"api/simulator/backdoor/write-counts/user/{Uri.EscapeDataString(userId)}");
        response.EnsureSuccessStatusCode();
    }

    private sealed class WriteCountResponse
    {
        public int WriteCount { get; set; }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
