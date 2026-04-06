using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar.GoogleApi;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Calendar;

public class GoogleCalendarClient : IGoogleCalendarClient
{
    private readonly HttpClient _httpClient;
    private readonly GoogleAuthService _authService;
    private readonly ITokenStore _tokenStore;
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly GoogleCalendarOptions _options;
    private readonly ILogger<GoogleCalendarClient> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GoogleCalendarClient(
        HttpClient httpClient,
        GoogleAuthService authService,
        ITokenStore tokenStore,
        IAccessTokenProvider accessTokenProvider,
        IOptions<GoogleCalendarOptions> options,
        ILogger<GoogleCalendarClient> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _tokenStore = tokenStore;
        _accessTokenProvider = accessTokenProvider;
        _options = options.Value;
        _logger = logger;
    }

    private async Task SetAuthorizationHeaderAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_accessTokenProvider.AccessToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _accessTokenProvider.AccessToken);
            return;
        }

        var refreshToken = await _tokenStore.GetRefreshTokenAsync(ct);
        if (string.IsNullOrEmpty(refreshToken))
            throw new InvalidOperationException("No refresh token available. User must authenticate first.");

        var accessToken = await _authService.RefreshAccessTokenAsync(refreshToken, ct);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<IEnumerable<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);
        var endpoint = $"{_options.CalendarApiBaseUrl}/users/me/calendarList";
        var response = await _httpClient.GetAsync(endpoint, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GoogleApiCalendarList>(cancellationToken: ct);
        return result?.Items.Select(item => new CalendarInfo
        {
            GoogleCalendarId = item.Id,
            DisplayName = item.SummaryOverride ?? item.Summary ?? string.Empty,
            Color = item.BackgroundColor
        }) ?? Array.Empty<CalendarInfo>();
    }

    public async Task<(IEnumerable<CalendarEvent> Events, string? NextSyncToken)> GetEventsAsync(
        string googleCalendarId,
        DateTimeOffset? syncWindowStart,
        DateTimeOffset? syncWindowEnd,
        string? syncToken = null,
        CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);

        var events = new List<CalendarEvent>();
        string? nextSyncToken = null;
        string? pageToken = null;

        do
        {
            var query = new List<string> { "singleEvents=true" };

            if (!string.IsNullOrEmpty(syncToken))
            {
                query.Add($"syncToken={Uri.EscapeDataString(syncToken)}");
            }
            else
            {
                if (syncWindowStart.HasValue)
                    query.Add($"timeMin={Uri.EscapeDataString(syncWindowStart.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}");
                if (syncWindowEnd.HasValue)
                    query.Add($"timeMax={Uri.EscapeDataString(syncWindowEnd.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}");
            }

            if (!string.IsNullOrEmpty(pageToken))
                query.Add($"pageToken={Uri.EscapeDataString(pageToken)}");

            var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events?{string.Join("&", query)}";
            var response = await _httpClient.GetAsync(endpoint, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Gone)
                throw new InvalidOperationException("Sync token is no longer valid. Full sync required.");

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<GoogleApiEventList>(cancellationToken: ct);
            if (result != null)
            {
                foreach (var item in result.Items)
                {
                    if (item.Status == "cancelled")
                    {
                        events.Add(new CalendarEvent { GoogleEventId = item.Id, Title = "CANCELLED_TOMBSTONE" });
                        continue;
                    }

                    var startParam = item.Start?.DateTime
                        ?? (item.Start?.Date != null ? DateTimeOffset.Parse(item.Start.Date, CultureInfo.InvariantCulture) : (DateTimeOffset?)null);
                    var endParam = item.End?.DateTime
                        ?? (item.End?.Date != null ? DateTimeOffset.Parse(item.End.Date, CultureInfo.InvariantCulture) : (DateTimeOffset?)null);

                    if (startParam == null || endParam == null) continue;

                    events.Add(new CalendarEvent
                    {
                        GoogleEventId = item.Id,
                        Title = item.Summary ?? "Untitled Event",
                        Start = startParam.Value,
                        End = endParam.Value,
                        IsAllDay = item.Start?.Date != null,
                        Location = item.Location,
                        Description = item.Description
                        // ContentHash is NOT stored on CalendarEvent; retrieved on-demand from Google via GetEventAsync
                    });
                }

                pageToken = result.NextPageToken;
                nextSyncToken = result.NextSyncToken;
            }
        } while (!string.IsNullOrEmpty(pageToken));

        return (events, nextSyncToken);
    }

    public async Task<CalendarEvent> CreateEventAsync(
        string googleCalendarId,
        CalendarEvent calendarEvent,
        string contentHash,
        CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events";
        var body = MapToGoogleEvent(calendarEvent, contentHash);
        var response = await _httpClient.PostAsJsonAsync(endpoint, body, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GoogleApiEvent>(cancellationToken: ct);
        calendarEvent.GoogleEventId = result!.Id;
        return calendarEvent;
    }

    public async Task<CalendarEvent> UpdateEventAsync(
        string googleCalendarId,
        CalendarEvent calendarEvent,
        string contentHash,
        CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(calendarEvent.GoogleEventId)}";
        var body = MapToGoogleEvent(calendarEvent, contentHash);
        var response = await _httpClient.PutAsJsonAsync(endpoint, body, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return calendarEvent;
    }

    public async Task DeleteEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(googleEventId)}";
        var response = await _httpClient.DeleteAsync(endpoint, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Delete event {GoogleEventId} returned 404 — treating as success.", googleEventId);
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    public async Task<string> MoveEventAsync(
        string sourceCalendarId,
        string googleEventId,
        string destinationCalendarId,
        CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(sourceCalendarId)}/events/{Uri.EscapeDataString(googleEventId)}/move?destination={Uri.EscapeDataString(destinationCalendarId)}";
        var response = await _httpClient.PostAsync(endpoint, null, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GoogleApiEvent>(cancellationToken: ct);
        return result!.Id;
    }

    public async Task<GoogleEventDetail?> GetEventAsync(
        string googleCalendarId,
        string googleEventId,
        CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(googleEventId)}";
        var response = await _httpClient.GetAsync(endpoint, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var apiEvent = await response.Content.ReadFromJsonAsync<GoogleApiEvent>(cancellationToken: ct);
        if (apiEvent is null) return null;

        var contentHash = apiEvent.ExtendedProperties?.Private?.ContentHash;
        return new GoogleEventDetail(apiEvent.Id, apiEvent.Organizer?.Email, contentHash);
    }

    private static object MapToGoogleEvent(CalendarEvent evt, string contentHash)
    {
        var extendedProperties = new
        {
            @private = new Dictionary<string, string> { ["content-hash"] = contentHash }
        };

        if (evt.IsAllDay)
        {
            return new
            {
                summary = evt.Title,
                description = evt.Description,
                location = evt.Location,
                start = new { date = evt.Start.ToString("yyyy-MM-dd") },
                end = new { date = evt.End.ToString("yyyy-MM-dd") },
                extendedProperties
            };
        }

        return new
        {
            summary = evt.Title,
            description = evt.Description,
            location = evt.Location,
            start = new { dateTime = evt.Start.ToString("yyyy-MM-ddTHH:mm:ssK") },
            end = new { dateTime = evt.End.ToString("yyyy-MM-ddTHH:mm:ssK") },
            extendedProperties
        };
    }
}
