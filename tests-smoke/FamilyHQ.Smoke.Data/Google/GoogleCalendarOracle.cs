using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyHQ.Smoke.Common.Configuration;
using FamilyHQ.Smoke.Common.Helpers;
using FamilyHQ.Smoke.Data.Api;
using FamilyHQ.Smoke.Data.Models;

namespace FamilyHQ.Smoke.Data.Google;

/// <summary>
/// Google Calendar, spoken to directly with the smoke account's own access token.
/// <para>
/// This is the <b>oracle</b>: FHQ-141 principle 6 says recurrence is checked against Google's own
/// expansion and never against a calculation of ours, and the same reasoning applies to every other
/// assertion about a write. Asking FamilyHQ what it wrote would only prove FamilyHQ agrees with itself;
/// the whole point of a smoke suite is to ask the system of record.
/// </para>
/// <para>
/// It is also the phone stand-in. The scenarios that matter most involve an event FamilyHQ did not
/// create, because a change that is correct for FamilyHQ's own events can be wrong for synced ones — so
/// this class writes as well as reads.
/// </para>
/// <para>
/// The access token is registered with <see cref="SecretGuard"/> on construction. Every error message
/// this class raises passes the response body through <see cref="SecretGuard.Scrub"/> first: Google does
/// not echo credentials, but an intermediary is free to, and a diagnostic message that leaks the oracle
/// token would be a worse failure than the one it was describing.
/// </para>
/// </summary>
public sealed class GoogleCalendarOracle : IDisposable
{
    private readonly HttpClient _httpClient;

    public GoogleCalendarOracle(SmokeConfiguration configuration, string accessToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _httpClient = SmokeHttpClientFactory.Create(configuration, configuration.GoogleCalendarApiBaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        SecretGuard.Register(accessToken);
    }

    /// <summary>Every calendar on the smoke account, as Google lists them.</summary>
    public async Task<IReadOnlyList<GoogleCalendarListEntry>> ListCalendarsAsync(CancellationToken ct = default)
    {
        var entries = new List<GoogleCalendarListEntry>();
        string? pageToken = null;

        do
        {
            var path = "users/me/calendarList?maxResults=250"
                       + (pageToken is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");
            var page = await GetAsync<GoogleCalendarList>(path, ct);
            entries.AddRange(page.Items ?? []);
            pageToken = page.NextPageToken;
        }
        while (pageToken is not null);

        return entries;
    }

    /// <summary>
    /// Every event on <paramref name="calendarId"/> in the window that carries
    /// <paramref name="correlationMarker"/> in its description — this scenario's events and nobody
    /// else's.
    /// <para>
    /// Filtering happens here rather than through Google's <c>q</c> parameter deliberately: <c>q</c> is a
    /// full-text index, and an index can lag a write it has not caught up with yet. A window listing plus
    /// an exact substring match on what Google returned cannot be stale in that way.
    /// </para>
    /// </summary>
    /// <param name="expandInstances">
    /// True to have Google expand a series into its instances; false to see the series master and its
    /// <c>recurrence</c> array. The recurrence scenarios need both views of the same series.
    /// </param>
    public async Task<IReadOnlyList<GoogleEvent>> FindEventsAsync(
        string calendarId,
        string correlationMarker,
        DateTimeOffset from,
        DateTimeOffset to,
        bool expandInstances = false,
        CancellationToken ct = default)
    {
        var events = new List<GoogleEvent>();
        string? pageToken = null;

        do
        {
            var path = $"calendars/{Uri.EscapeDataString(calendarId)}/events"
                       + $"?timeMin={Rfc3339(from)}&timeMax={Rfc3339(to)}"
                       + $"&singleEvents={(expandInstances ? "true" : "false")}"
                       + "&maxResults=2500"
                       + (pageToken is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");

            var page = await GetAsync<GoogleEventList>(path, ct);
            events.AddRange((page.Items ?? [])
                .Where(item => item.Description is not null
                               && item.Description.Contains(correlationMarker, StringComparison.Ordinal)));
            pageToken = page.NextPageToken;
        }
        while (pageToken is not null);

        return events;
    }

    /// <summary>One event, exactly as Google holds it right now.</summary>
    public Task<GoogleEvent> GetEventAsync(string calendarId, string eventId, CancellationToken ct = default) =>
        GetAsync<GoogleEvent>(
            $"calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}", ct);

    /// <summary>
    /// Google's own expansion of a series — <c>events.instances</c>. This is the oracle for every
    /// recurrence assertion: whatever this returns is, by definition, the correct occurrence set.
    /// </summary>
    public async Task<IReadOnlyList<GoogleEvent>> ListInstancesAsync(
        string calendarId,
        string seriesMasterId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        var instances = new List<GoogleEvent>();
        string? pageToken = null;

        do
        {
            var path = $"calendars/{Uri.EscapeDataString(calendarId)}"
                       + $"/events/{Uri.EscapeDataString(seriesMasterId)}/instances"
                       + $"?timeMin={Rfc3339(from)}&timeMax={Rfc3339(to)}&maxResults=2500"
                       + (pageToken is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");

            var page = await GetAsync<GoogleEventList>(path, ct);
            instances.AddRange(page.Items ?? []);
            pageToken = page.NextPageToken;
        }
        while (pageToken is not null);

        return instances;
    }

    /// <summary>Creates an event on <paramref name="calendarId"/>, the way a phone would.</summary>
    public async Task<GoogleEvent> InsertEventAsync(
        string calendarId, GoogleEventDraft draft, CancellationToken ct = default)
    {
        var path = $"calendars/{Uri.EscapeDataString(calendarId)}/events";
        using var response = await _httpClient.PostAsJsonAsync(path, draft, SmokeJson.Options, ct);
        return await ReadRequiredAsync<GoogleEvent>(response, "POST", path, ct);
    }

    /// <summary>Deletes an event from <paramref name="calendarId"/>. A 404 or 410 means it was already gone.</summary>
    public async Task DeleteEventAsync(string calendarId, string eventId, CancellationToken ct = default)
    {
        var path = $"calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";
        using var response = await _httpClient.DeleteAsync(path, ct);

        if (response.IsSuccessStatusCode
            || response.StatusCode == System.Net.HttpStatusCode.NotFound
            || response.StatusCode == System.Net.HttpStatusCode.Gone)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"Google answered HTTP {(int)response.StatusCode} to DELETE {path}: {SecretGuard.Scrub(body)}");
    }

    public void Dispose() => _httpClient.Dispose();

    private static string Rfc3339(DateTimeOffset instant) =>
        Uri.EscapeDataString(instant.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(path, ct);
        return await ReadRequiredAsync<T>(response, "GET", path, ct);
    }

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response, string method, string path, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Google answered HTTP {(int)response.StatusCode} to {method} {path}: "
                + SecretGuard.Scrub(body));
        }

        return JsonSerializer.Deserialize<T>(body, SmokeJson.Options)
               ?? throw new HttpRequestException(
                   $"Google answered HTTP {(int)response.StatusCode} to {method} {path} with a body that "
                   + $"did not deserialise to {typeof(T).Name}.");
    }
}
