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
    private readonly ICurrentUserService _currentUser;
    private readonly IAccessTokenCache _accessTokenCache;
    private readonly GoogleCalendarOptions _options;
    private readonly ILogger<GoogleCalendarClient> _logger;
    private readonly ITimeZoneService _timeZoneService;
    private readonly IPiiRedactor _piiRedactor;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GoogleCalendarClient(
        HttpClient httpClient,
        GoogleAuthService authService,
        ITokenStore tokenStore,
        ICurrentUserService currentUser,
        IAccessTokenCache accessTokenCache,
        IOptions<GoogleCalendarOptions> options,
        ILogger<GoogleCalendarClient> logger,
        ITimeZoneService timeZoneService,
        IPiiRedactor piiRedactor)
    {
        _httpClient = httpClient;
        _authService = authService;
        _tokenStore = tokenStore;
        _currentUser = currentUser;
        _accessTokenCache = accessTokenCache;
        _options = options.Value;
        _logger = logger;
        _timeZoneService = timeZoneService;
        _piiRedactor = piiRedactor;
    }

    public const int MaxSyncPages = 20;
    private const string EventsListFields =
        "nextPageToken,nextSyncToken,items(id,iCalUID,summary,description,location,start,end,attendees,organizer,extendedProperties,recurringEventId,originalStartTime,status)";

    private async Task ThrowIfFailedAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        // FHQ-88: the raw Google error body is parsed for its reason code and then discarded —
        // it must never be logged or retained on an exception, where a generic handler or logger
        // up the stack could leak it.
        var body = await response.Content.ReadAsStringAsync(ct);
        var reason = ParseGoogleErrorReason(body);

        // 401 is always an auth failure. A 403 is auth too — UNLESS it carries a rate/quota reason,
        // in which case it is a transient throttle (FHQ-83) and must retry, not prompt reconnect.
        if (response.StatusCode == HttpStatusCode.Unauthorized
            || (response.StatusCode == HttpStatusCode.Forbidden && !IsRateLimitReason(reason)))
        {
            // FHQ-82: the access-token cache must not keep serving a token past a revoked grant.
            // Evict on ANY observed reauth (not just the background sync's MarkNeedsReauthAsync
            // path) so a stale token is dropped immediately, including foreground event writes.
            if (_currentUser.UserId is { } reauthUserId)
                _accessTokenCache.Evict(reauthUserId);

            _logger.LogWarning(
                "Google {Operation} returned {Status}; user re-authentication required (reason: {Reason}).",
                operation, (int)response.StatusCode, reason ?? response.ReasonPhrase);
            // FHQ-85: attach the user so catch sites (DomainExceptionHandler, webhook
            // registration) can persist NeedsReauth without re-resolving the current user.
            throw new GoogleReauthRequiredException(
                GoogleAuthFailureSource.CalendarApi,
                response.ReasonPhrase,
                userId: _currentUser.UserId);
        }

        // FHQ-61: allowlist the one benign, permanent rejection — a read-only/subscribed calendar that
        // can't have push notifications. Surface it as a typed, non-error signal so callers skip the
        // calendar quietly. Every other reason still raises GoogleApiException.
        if (reason == "pushNotSupportedForRequestedResource")
        {
            _logger.LogInformation(
                "Google {Operation} returned {Status} ({Reason}); resource does not support push notifications.",
                operation, (int)response.StatusCode, reason);
            throw new WebhookNotSupportedException(operation, reason);
        }

        _logger.LogWarning(
            "Google {Operation} returned {Status} (reason: {Reason}).",
            operation, (int)response.StatusCode, reason ?? "unknown");
        throw new GoogleApiException(response.StatusCode, operation, NormaliseRetryAfter(response.Headers.RetryAfter));
    }

    /// <summary>
    /// Reduces an HTTP <c>Retry-After</c> header (delta-seconds or HTTP-date form) to a positive
    /// <see cref="TimeSpan"/>. Returns null when the header is absent, in the past, or unparseable.
    /// </summary>
    private static TimeSpan? NormaliseRetryAfter(RetryConditionHeaderValue? header)
    {
        if (header is null) return null;
        var delta = header.Delta ?? (header.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
        return delta is { } d && d > TimeSpan.Zero ? d : null;
    }

    // FHQ-83: Google surfaces throttling as a 403 with one of these reasons. They are transient
    // (the request was rejected, not processed) and must retry, not trigger a reauth prompt.
    private static readonly HashSet<string> RateLimitReasons = new(StringComparer.Ordinal)
    {
        "rateLimitExceeded", "userRateLimitExceeded", "quotaExceeded", "dailyLimitExceeded", "rateLimitExceededUnreg"
    };

    private static bool IsRateLimitReason(string? reason) => reason is not null && RateLimitReasons.Contains(reason);

    private static string? ParseGoogleErrorReason(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0
                && errors[0].TryGetProperty("reason", out var reasonEl))
            {
                return reasonEl.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON / unexpected shape → no reason; falls through to GoogleApiException.
        }
        return null;
    }

    private async Task<string> GetBearerTokenAsync(CancellationToken ct)
    {
        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("No user id available for Google token acquisition.");

        return await _accessTokenCache.GetOrRefreshAsync(userId, async c =>
        {
            var refreshToken = await _tokenStore.GetRefreshTokenAsync(c);
            if (string.IsNullOrEmpty(refreshToken))
                throw new InvalidOperationException("No refresh token available. User must authenticate first.");
            try
            {
                return await _authService.RefreshAccessTokenAsync(refreshToken, c);
            }
            catch (GoogleReauthRequiredException ex) when (ex.UserId is null)
            {
                // FHQ-85: GoogleAuthService only sees the raw refresh-token string; this seam
                // knows which user it belongs to. Re-throw with the user attached so catch
                // sites can persist NeedsReauth.
                throw new GoogleReauthRequiredException(
                    ex.FailureSource, ex.ErrorDescription, userId: userId);
            }
        }, ct);
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
        var pageCount = 0;

        do
        {
            var query = new List<string>
            {
                "singleEvents=true",
                "maxResults=250",
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
                throw new SyncTokenExpiredException();

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

            pageCount++;
            if (pageCount >= MaxSyncPages && !string.IsNullOrEmpty(pageToken))
            {
                // FHQ-166: a Google PRIMARY calendar's id IS the account's email address, so it
                // never goes to Seq verbatim. This client is the one place with no FamilyHQ-side
                // calendar row to name instead, so it logs the redactor's stable token.
                _logger.LogWarning(
                    "GetEventsAsync reached the {MaxPages}-page cap for calendar {CalendarIdToken}. Returning {EventCount} events collected so far.",
                    MaxSyncPages, _piiRedactor.Redact(googleCalendarId), events.Count);
                break;
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

    public async Task<CalendarEvent> PatchEventFieldsAsync(
        string googleCalendarId,
        CalendarEvent calendarEvent,
        string contentHash,
        CancellationToken ct = default)
    {
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/{Uri.EscapeDataString(calendarEvent.GoogleEventId)}";
        var ianaZone = await _timeZoneService.GetSendZoneAsync(ct);
        // MapToGoogleEvent emits no `recurrence` key when given no rrule (WhenWritingNull), and PATCH
        // merges — so the master's existing RRULE, attendees and reminders survive the write.
        var body = MapToGoogleEvent(calendarEvent, contentHash, ianaZone: ianaZone, clearCounterpartWhenFields: true);
        using var request = await BuildAuthorizedRequestAsync(HttpMethod.Patch, endpoint, ct);
        request.Content = JsonContent.Create(body, options: _jsonOptions);
        var response = await _httpClient.SendAsync(request, ct);
        await ThrowIfFailedAsync(response, "PatchEventFields", ct);
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

        // start.timeZone carries the zone the recurrence is anchored to; the split-count enumeration
        // needs it to hold the series' wall clock across a DST transition (FHQ-161).
        return new SeriesMaster(rrule, start.Value, apiEvent.Start?.TimeZone);
    }

    // Resolves an event's start instant from either a timed (dateTime) or all-day (date) field.
    private static DateTimeOffset? ParseEventStart(GoogleApiEventDateTime? start) =>
        start?.DateTime
        ?? (start?.Date != null ? DateTimeOffset.Parse(start.Date, CultureInfo.InvariantCulture) : (DateTimeOffset?)null);

    public async Task<WatchChannelResponse> WatchEventsAsync(
        string googleCalendarId,
        string channelId,
        string webhookUrl,
        string channelToken,
        CancellationToken ct = default)
    {
        var endpoint = $"{_options.CalendarApiBaseUrl}/calendars/{Uri.EscapeDataString(googleCalendarId)}/events/watch";
        var body = new { id = channelId, type = "web_hook", address = webhookUrl, token = channelToken };
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

    private object MapToGoogleEvent(
        CalendarEvent evt, string contentHash, string? rrule = null, string? ianaZone = null,
        bool clearCounterpartWhenFields = false)
    {
        var extendedProperties = new
        {
            @private = new Dictionary<string, string> { ["content-hash"] = contentHash }
        };

        // Google expects the recurrence array only when the event is a series master.
        var recurrence = rrule is null ? null : new[] { rrule };

        string? startDate = null, startDateTime = null, startZone = null;
        string? endDate = null, endDateTime = null, endZone = null;

        if (evt.IsAllDay)
        {
            // Google requires end.date to be the day AFTER the last day of the event (exclusive).
            // Local End may be next-day midnight (already exclusive), an inclusive end-of-day tick,
            // a same-instant-as-Start (post-sync corruption), or a mid-day time (IsAllDay toggled
            // without resetting times). Normalise all of these to a strict next-day boundary using
            // each instant's wall-clock date in its own offset, matching how Start is serialised.
            var startWallDate = evt.Start.DateTime.Date;
            var endWallDate = evt.End.DateTime.Date;
            var exclusiveEndDate = evt.End.TimeOfDay == TimeSpan.Zero && endWallDate > startWallDate
                ? endWallDate
                : endWallDate.AddDays(1);

            startDate = evt.Start.ToString("yyyy-MM-dd");
            endDate = exclusiveEndDate.ToString("yyyy-MM-dd");
        }
        else if (!string.IsNullOrWhiteSpace(ianaZone))
        {
            // When the user's IANA zone is known, send the wall-clock time in that zone so recurring
            // series don't drift across DST transitions (FHQ-43). The timeZone field tells Google
            // how to interpret the dateTime and how to expand future occurrences.
            startDateTime = _timeZoneService.ToZonedWallClock(evt.Start, ianaZone);
            startZone = ianaZone;
            endDateTime = _timeZoneService.ToZonedWallClock(evt.End, ianaZone);
            endZone = ianaZone;
        }
        else
        {
            // UTC fallback — preserves FHQ-42 behaviour when no zone is resolved. Google REQUIRES a
            // timeZone on start/end for a recurring event, so send timeZone=UTC with the UTC instant.
            startDateTime = evt.Start.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssK");
            startZone = "UTC";
            endDateTime = evt.End.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssK");
            endZone = "UTC";
        }

        return new
        {
            summary = evt.Title,
            description = evt.Description,
            location = evt.Location ?? "",
            start = BuildWhen(startDate, startDateTime, startZone, clearCounterpartWhenFields),
            end = BuildWhen(endDate, endDateTime, endZone, clearCounterpartWhenFields),
            recurrence,
            extendedProperties
        };
    }

    // On events.patch (merge), the unused sub-field must be sent as an explicit JSON null to clear a
    // stale value when an event flips all-day <-> timed; otherwise Google merges the new date onto the
    // stale dateTime (or vice-versa) and rejects it 400 "Invalid start time" (FHQ-151). The client-wide
    // WhenWritingNull would drop an omitted null, so the patch path uses GoogleEventWhenPayload, whose
    // properties are always emitted. On events.insert (create) there is no stale field to clear, so keep
    // the pruned anonymous shape (WhenWritingNull omits the null sub-fields).
    private static object BuildWhen(string? date, string? dateTime, string? timeZone, bool clearCounterpart)
        => clearCounterpart
            ? new GoogleEventWhenPayload { Date = date, DateTime = dateTime, TimeZone = timeZone }
            : new { date, dateTime, timeZone };

    private sealed class GoogleEventWhenPayload
    {
        [JsonPropertyName("date")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Date { get; init; }

        [JsonPropertyName("dateTime")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? DateTime { get; init; }

        [JsonPropertyName("timeZone")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? TimeZone { get; init; }
    }
}
