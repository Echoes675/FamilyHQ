using System.Globalization;
using System.Net;
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
    private readonly ITimeZoneService _timeZoneService;
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
        ILogger<GoogleCalendarClient> logger,
        ITimeZoneService timeZoneService)
    {
        _httpClient = httpClient;
        _authService = authService;
        _tokenStore = tokenStore;
        _accessTokenProvider = accessTokenProvider;
        _options = options.Value;
        _logger = logger;
        _timeZoneService = timeZoneService;
    }

    private const int MaxLoggedBodyLength = 4096;
    private const string EventsListFields =
        "nextPageToken,nextSyncToken,items(id,iCalUID,summary,description,location,start,end,attendees,organizer,extendedProperties,recurringEventId,originalStartTime,status)";

    private async Task ThrowIfFailedAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        var truncated = body.Length <= MaxLoggedBodyLength ? body : body.Substring(0, MaxLoggedBodyLength);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(
                "Google {Operation} returned {Status}; user re-authentication required. Body: {Body}",
                operation, (int)response.StatusCode, truncated);
            throw new GoogleReauthRequiredException(
                GoogleAuthFailureSource.CalendarApi,
                response.ReasonPhrase,
                truncated);
        }

        _logger.LogWarning(
            "Google {Operation} returned {Status}. Body: {Body}",
            operation, (int)response.StatusCode, truncated);
        throw new GoogleApiException(response.StatusCode, operation, truncated);
    }

    private async Task<string> GetBearerTokenAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_accessTokenProvider.AccessToken))
            return _accessTokenProvider.AccessToken;

        var refreshToken = await _tokenStore.GetRefreshTokenAsync(ct);
        if (string.IsNullOrEmpty(refreshToken))
            throw new InvalidOperationException("No refresh token available. User must authenticate first.");

        return await _authService.RefreshAccessTokenAsync(refreshToken, ct);
    }

    // FHQ-27: build a fresh HttpRequestMessage with Authorization attached per-request.
    // Never mutate _httpClient.DefaultRequestHeaders.Authorization — that is process-shared
    // state on the typed client and leaks across concurrent users.
    private async Task<HttpRequestMessage> BuildAuthorizedRequestAsync(
        HttpMethod method, string requestUri, CancellationToken ct)
    {
        var token = await GetBearerTokenAsync(ct);
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public async Task<IEnumerable<CalendarInfo>> GetCalendarsAsync(CancellationToken ct = default)
    {
        var endpoint = $"{_options.CalendarApiBaseUrl}/users/me/calendarList";
        using var request = await BuildAuthorizedRequestAsync(HttpMethod.Get, endpoint, ct);
        var response = await _httpClient.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, "GetCalendars", ct);

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
        var events = new List<CalendarEvent>();
        string? nextSyncToken = null;
        string? pageToken = null;

        do
        {
            var query = new List<string>
            {
                "singleEvents=true",
                "fields=" + Uri.EscapeDataString(EventsListFields)
            };

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
            using var request = await BuildAuthorizedRequestAsync(HttpMethod.Get, endpoint, ct);
            var response = await _httpClient.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.Gone)
                throw new InvalidOperationException("Sync token is no longer valid. Full sync required.");

            await ThrowIfFailedAsync(response, "GetEvents", ct);

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

                    var originalStart = item.OriginalStartTime?.DateTime
                        ?? (item.OriginalStartTime?.Date != null
                            ? DateTimeOffset.Parse(item.OriginalStartTime.Date, CultureInfo.InvariantCulture)
                            : (DateTimeOffset?)null);

                    events.Add(new CalendarEvent
                    {
                        GoogleEventId = item.Id,
                        Title = item.Summary ?? "Untitled Event",
                        Start = startParam.Value,
                        End = endParam.Value,
                        IsAllDay = item.Start?.Date != null,
                        Location = item.Location,
                        Description = item.Description,
                        ContentHash = item.ExtendedProperties?.Private?.ContentHash,
                        // Series link from pass 1. RecurrenceRule is filled in pass 2 by the
                        // two-pass master fetch in CalendarSyncService.
                        GoogleRecurringEventId = item.RecurringEventId,
                        OriginalStartTime = originalStart
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
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events";
        var ianaZone = await _timeZoneService.GetSendZoneAsync(ct);
        var body = MapToGoogleEvent(calendarEvent, contentHash, ianaZone: ianaZone);
        using var request = await BuildAuthorizedRequestAsync(HttpMethod.Post, endpoint, ct);
        request.Content = JsonContent.Create(body, options: _jsonOptions);
        var response = await _httpClient.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, "CreateEvent", ct);

        var result = await response.Content.ReadFromJsonAsync<GoogleApiEvent>(cancellationToken: ct);
        calendarEvent.GoogleEventId = result!.Id;
        return calendarEvent;
    }

    public async Task<CalendarEvent> CreateRecurringEventAsync(
        string googleCalendarId,
        CalendarEvent calendarEvent,
        string contentHash,
        string rrule,
        CancellationToken ct = default)
    {
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events";
        var ianaZone = await _timeZoneService.GetSendZoneAsync(ct);
        var body = MapToGoogleEvent(calendarEvent, contentHash, rrule, ianaZone);
        using var request = await BuildAuthorizedRequestAsync(HttpMethod.Post, endpoint, ct);
        request.Content = JsonContent.Create(body, options: _jsonOptions);
        var response = await _httpClient.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, "CreateRecurringEvent", ct);

        var result = await response.Content.ReadFromJsonAsync<GoogleApiEvent>(cancellationToken: ct);
        calendarEvent.GoogleEventId = result!.Id;
        return calendarEvent;
    }

    public async Task PatchSeriesRecurrenceAsync(
        string googleCalendarId,
        string seriesId,
        string rrule,
        CancellationToken ct = default)
    {
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(seriesId)}";
        // events.patch with only the recurrence array — every other master field is left untouched.
        var body = new { recurrence = new[] { rrule } };
        using var request = await BuildAuthorizedRequestAsync(HttpMethod.Patch, endpoint, ct);
        request.Content = JsonContent.Create(body, options: _jsonOptions);
        var response = await _httpClient.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, "PatchSeriesRecurrence", ct);
    }

    public async Task ClearSeriesRecurrenceAsync(
        string googleCalendarId,
        string seriesId,
        CancellationToken ct = default)
    {
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(seriesId)}";
        // events.patch with an empty recurrence array — Google collapses the series to a single event.
        var body = new { recurrence = Array.Empty<string>() };
        using var request = await BuildAuthorizedRequestAsync(HttpMethod.Patch, endpoint, ct);
        request.Content = JsonContent.Create(body, options: _jsonOptions);
        var response = await _httpClient.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, "ClearSeriesRecurrence", ct);
    }

    public async Task<CalendarEvent> UpdateEventAsync(
        string googleCalendarId,
        CalendarEvent calendarEvent,
        string contentHash,
        CancellationToken ct = default)
    {
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(calendarEvent.GoogleEventId)}";
        var ianaZone = await _timeZoneService.GetSendZoneAsync(ct);
        var body = MapToGoogleEvent(calendarEvent, contentHash, ianaZone: ianaZone);
        using var request = await BuildAuthorizedRequestAsync(HttpMethod.Put, endpoint, ct);
        request.Content = JsonContent.Create(body, options: _jsonOptions);
        var response = await _httpClient.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, "UpdateEvent", ct);
        return calendarEvent;
    }

    public async Task DeleteEventAsync(string googleCalendarId, string googleEventId, CancellationToken ct = default)
    {
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(googleEventId)}";
        using var request = await BuildAuthorizedRequestAsync(HttpMethod.Delete, endpoint, ct);
        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Delete event {GoogleEventId} returned 404 — treating as success.", googleEventId);
            return;
        }

        await ThrowIfFailedAsync(response, "DeleteEvent", ct);
    }

    public async Task<string> MoveEventAsync(
        string sourceCalendarId,
        string googleEventId,
        string destinationCalendarId,
        CancellationToken ct = default)
    {
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(sourceCalendarId)}/events/{Uri.EscapeDataString(googleEventId)}/move?destination={Uri.EscapeDataString(destinationCalendarId)}";
        using var request = await BuildAuthorizedRequestAsync(HttpMethod.Post, endpoint, ct);
        var response = await _httpClient.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, "MoveEvent", ct);

        var result = await response.Content.ReadFromJsonAsync<GoogleApiEvent>(cancellationToken: ct);
        return result!.Id;
    }

    public async Task<GoogleEventDetail?> GetEventAsync(
        string googleCalendarId,
        string googleEventId,
        CancellationToken ct = default)
    {
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(googleEventId)}";
        using var request = await BuildAuthorizedRequestAsync(HttpMethod.Get, endpoint, ct);
        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await ThrowIfFailedAsync(response, "GetEvent", ct);

        var apiEvent = await response.Content.ReadFromJsonAsync<GoogleApiEvent>(cancellationToken: ct);
        if (apiEvent is null) return null;

        var contentHash = apiEvent.ExtendedProperties?.Private?.ContentHash;
        return new GoogleEventDetail(apiEvent.Id, apiEvent.Organizer?.Email, contentHash);
    }

    public async Task<SeriesMaster?> GetSeriesMasterAsync(
        string googleCalendarId,
        string seriesId,
        CancellationToken ct = default)
    {
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(seriesId)}";
        using var request = await BuildAuthorizedRequestAsync(HttpMethod.Get, endpoint, ct);
        var response = await _httpClient.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await ThrowIfFailedAsync(response, "GetSeriesMaster", ct);

        var apiEvent = await response.Content.ReadFromJsonAsync<GoogleApiEvent>(cancellationToken: ct);

        // recurrence may contain RRULE, EXDATE and RDATE lines; FamilyHQ stores only the RRULE.
        var rrule = apiEvent?.Recurrence?.FirstOrDefault(line => line.StartsWith("RRULE:", StringComparison.Ordinal));
        if (rrule is null) return null;

        var start = ParseEventStart(apiEvent!.Start);
        if (start is null) return null;

        return new SeriesMaster(rrule, start.Value);
    }

    // Resolves an event's start instant from either a timed (dateTime) or all-day (date) field.
    private static DateTimeOffset? ParseEventStart(GoogleApiEventDateTime? start) =>
        start?.DateTime
        ?? (start?.Date != null ? DateTimeOffset.Parse(start.Date, CultureInfo.InvariantCulture) : (DateTimeOffset?)null);

    public async Task<WatchChannelResponse> WatchEventsAsync(
        string googleCalendarId,
        string channelId,
        string webhookUrl,
        CancellationToken ct = default)
    {
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/watch";
        var body = new { id = channelId, type = "web_hook", address = webhookUrl };
        using var request = await BuildAuthorizedRequestAsync(HttpMethod.Post, endpoint, ct);
        request.Content = JsonContent.Create(body, options: _jsonOptions);
        var response = await _httpClient.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, "WatchEvents", ct);

        var result = await response.Content.ReadFromJsonAsync<GoogleApiWatchResponse>(cancellationToken: ct);
        return new WatchChannelResponse(result!.Id, result.ResourceId, result.Expiration);
    }

    public async Task StopChannelAsync(string channelId, string resourceId, CancellationToken ct = default)
    {
        var endpoint = $"{_options.CalendarApiBaseUrl}/channels/stop";
        var body = new { id = channelId, resourceId };
        using var request = await BuildAuthorizedRequestAsync(HttpMethod.Post, endpoint, ct);
        request.Content = JsonContent.Create(body, options: _jsonOptions);
        var response = await _httpClient.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, "StopChannel", ct);
    }

    private object MapToGoogleEvent(CalendarEvent evt, string contentHash, string? rrule = null, string? ianaZone = null)
    {
        var extendedProperties = new
        {
            @private = new Dictionary<string, string> { ["content-hash"] = contentHash }
        };

        // Google expects the recurrence array only when the event is a series master.
        var recurrence = rrule is null ? null : new[] { rrule };

        if (evt.IsAllDay)
        {
            // Google requires end.date to be the day AFTER the last day of the event (exclusive).
            // Local End may be next-day midnight (already exclusive), an inclusive end-of-day tick,
            // a same-instant-as-Start (post-sync corruption), or a mid-day time (IsAllDay toggled
            // without resetting times). Normalise all of these to a strict next-day boundary using
            // each instant's wall-clock date in its own offset, matching how Start is serialised.
            var startWallDate = evt.Start.DateTime.Date;
            var endWallDate   = evt.End.DateTime.Date;
            var exclusiveEndDate = evt.End.TimeOfDay == TimeSpan.Zero && endWallDate > startWallDate
                ? endWallDate
                : endWallDate.AddDays(1);

            return new
            {
                summary = evt.Title,
                description = evt.Description,
                location = evt.Location,
                start = new { date = evt.Start.ToString("yyyy-MM-dd") },
                end = new { date = exclusiveEndDate.ToString("yyyy-MM-dd") },
                recurrence,
                extendedProperties
            };
        }

        // When the user's IANA zone is known, send the wall-clock time in that zone so recurring
        // series don't drift across DST transitions (FHQ-43). The timeZone field tells Google
        // how to interpret the dateTime and how to expand future occurrences.
        if (!string.IsNullOrWhiteSpace(ianaZone))
        {
            return new
            {
                summary = evt.Title,
                description = evt.Description,
                location = evt.Location,
                start = new { dateTime = _timeZoneService.ToZonedWallClock(evt.Start, ianaZone), timeZone = ianaZone },
                end = new { dateTime = _timeZoneService.ToZonedWallClock(evt.End, ianaZone), timeZone = ianaZone },
                recurrence,
                extendedProperties
            };
        }

        // UTC fallback — preserves FHQ-42 behaviour when no zone is resolved.
        // Google REQUIRES a timeZone on start/end for a recurring event (one with a recurrence
        // array) — without it the events.insert is rejected 400 "Missing time zone definition for
        // start time." (FHQ-42). A single event is accepted with just the offset, but sending the
        // timeZone unconditionally is harmless and keeps create/update of recurring series working.
        // The dateTime is normalised to UTC so its offset (+00:00) is consistent with timeZone=UTC.
        return new
        {
            summary = evt.Title,
            description = evt.Description,
            location = evt.Location,
            start = new { dateTime = evt.Start.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssK"), timeZone = "UTC" },
            end = new { dateTime = evt.End.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssK"), timeZone = "UTC" },
            recurrence,
            extendedProperties
        };
    }
}
