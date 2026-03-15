using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Calendar;

public class GoogleCalendarClient : IGoogleCalendarClient
{
    private readonly HttpClient _httpClient;
    private readonly GoogleAuthService _authService;
    private readonly ITokenStore _tokenStore;
    private readonly GoogleCalendarOptions _options;
    private readonly ILogger<GoogleCalendarClient> _logger;
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GoogleCalendarClient(
        HttpClient httpClient,
        GoogleAuthService authService,
        ITokenStore tokenStore,
        IOptions<GoogleCalendarOptions> options,
        ILogger<GoogleCalendarClient> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _tokenStore = tokenStore;
        _options = options.Value;
        _logger = logger;
    }

    private async Task SetAuthorizationHeaderAsync(CancellationToken ct)
    {
        var refreshToken = await _tokenStore.GetRefreshTokenAsync(ct);
        if (string.IsNullOrEmpty(refreshToken))
        {
            throw new InvalidOperationException("No refresh token available. User must authenticate first.");
        }

        var accessToken = await _authService.RefreshAccessTokenAsync(refreshToken, ct);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<IEnumerable<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);

        var endpoint = $"{_options.CalendarApiBaseUrl}/users/me/calendarList";
        var response = await _httpClient.GetAsync(endpoint, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CalendarListResponse>(cancellationToken: ct);
        
        return result?.Items.Select(item => new CalendarInfo
        {
            GoogleCalendarId = item.Id,
            DisplayName = item.SummaryOverride ?? item.Summary,
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
                if (syncWindowStart.HasValue) query.Add($"timeMin={syncWindowStart.Value.ToString("O")}");
                if (syncWindowEnd.HasValue) query.Add($"timeMax={syncWindowEnd.Value.ToString("O")}");
            }

            if (!string.IsNullOrEmpty(pageToken))
            {
                query.Add($"pageToken={Uri.EscapeDataString(pageToken)}");
            }

            var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events?{string.Join("&", query)}";
            
            var response = await _httpClient.GetAsync(endpoint, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                // Sync token expired. We must clear it and do a full sync.
                throw new InvalidOperationException("Sync token is no longer valid. Full sync required.");
            }
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<EventsResponse>(cancellationToken: ct);
            if (result != null)
            {
                foreach (var item in result.Items)
                {
                    // If the event was deleted, item.Status == "cancelled"
                    if (item.Status == "cancelled")
                    {
                        // We will return a tombstone event so the caller can delete it from the DB
                        events.Add(new CalendarEvent
                        {
                            GoogleEventId = item.Id,
                            Title = "CANCELLED_TOMBSTONE"
                        });
                        continue;
                    }

                    var startParam = item.Start?.DateTime ?? item.Start?.Date;
                    var endParam = item.End?.DateTime ?? item.End?.Date;
                    
                    if (startParam == null || endParam == null) continue; // Skip malformed events

                    var isAllDay = item.Start?.Date != null;

                    // Convert to UTC to ensure PostgreSQL compatibility (timestamp with time zone requires UTC)
                    var startUtc = startParam.Value.ToUniversalTime();
                    var endUtc = endParam.Value.ToUniversalTime();

                    events.Add(new CalendarEvent
                    {
                        GoogleEventId = item.Id,
                        Title = item.Summary ?? "Untitled Event",
                        Start = startUtc,
                        End = endUtc,
                        IsAllDay = isAllDay,
                        Location = item.Location,
                        Description = item.Description
                    });
                }

                pageToken = result.NextPageToken;
                nextSyncToken = result.NextSyncToken;
            }
        } while (!string.IsNullOrEmpty(pageToken));

        return (events, nextSyncToken);
    }

    public async Task<CalendarEvent> CreateEventAsync(string googleCalendarId, CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);

        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events";
        
        var requestBody = MapToGoogleEvent(calendarEvent);
        var response = await _httpClient.PostAsJsonAsync(endpoint, requestBody, _jsonSerializerOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GoogleEventItem>(cancellationToken: ct);
        calendarEvent.GoogleEventId = result!.Id;
        
        return calendarEvent;
    }

    public async Task<CalendarEvent> UpdateEventAsync(string googleCalendarId, CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);

        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(calendarEvent.GoogleEventId)}";
        
        var requestBody = MapToGoogleEvent(calendarEvent);
        var response = await _httpClient.PutAsJsonAsync(endpoint, requestBody, _jsonSerializerOptions, ct);
        response.EnsureSuccessStatusCode();

        return calendarEvent;
    }

    public async Task DeleteEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default)
    {
        await SetAuthorizationHeaderAsync(ct);

        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(googleEventId)}";
        var response = await _httpClient.DeleteAsync(endpoint, ct);
        response.EnsureSuccessStatusCode();
    }

    private static object MapToGoogleEvent(CalendarEvent calendarEvent)
    {
        if (calendarEvent.IsAllDay)
        {
            return new
            {
                summary = calendarEvent.Title,
                description = calendarEvent.Description,
                location = calendarEvent.Location,
                start = new { date = calendarEvent.Start.ToString("yyyy-MM-dd") },
                end = new { date = calendarEvent.End.ToString("yyyy-MM-dd") }
            };
        }
        else
        {
            return new
            {
                summary = calendarEvent.Title,
                description = calendarEvent.Description,
                location = calendarEvent.Location,
                start = new { dateTime = calendarEvent.Start.ToString("yyyy-MM-ddTHH:mm:ssK") },
                end = new { dateTime = calendarEvent.End.ToString("yyyy-MM-ddTHH:mm:ssK") }
            };
        }
    }

}
