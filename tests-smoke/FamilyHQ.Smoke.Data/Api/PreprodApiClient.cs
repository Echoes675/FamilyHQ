using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FamilyHQ.Smoke.Common.Configuration;
using FamilyHQ.Smoke.Common.Helpers;
using FamilyHQ.Smoke.Data.Models;

namespace FamilyHQ.Smoke.Data.Api;

/// <summary>
/// Preprod's own API, read as a signed-in kiosk would read it.
/// <para>
/// <b>Read-only, and not by accident.</b> There is no method here that writes a setting, registers a
/// webhook, or triggers a sync. FHQ-141 principle 1 says the suite checks the environment and never
/// repairs it, and principle 2 forbids compensating for a failure — both are enforced by this class
/// simply not offering the verbs. Events are written through the kiosk UI (which is the thing under
/// test) or through Google; never through a convenience call from here.
/// </para>
/// <para>
/// Every request carries the scenario's correlation id as <c>X-Correlation-Id</c>, so a failure has one
/// value to grep for in Seq that spans the browser's requests and the suite's own.
/// </para>
/// </summary>
public sealed class PreprodApiClient : IDisposable
{
    private const string CorrelationIdHeaderName = "X-Correlation-Id";

    private readonly HttpClient _httpClient;

    public PreprodApiClient(SmokeConfiguration configuration, string sessionJwt, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _httpClient = SmokeHttpClientFactory.Create(configuration, configuration.BaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionJwt);
        _httpClient.DefaultRequestHeaders.Add(CorrelationIdHeaderName, correlationId);

        SecretGuard.Register(sessionJwt);
    }

    /// <summary>
    /// Preprod's saved location, or <c>null</c> when none is saved — a 404 there is the documented
    /// "nothing saved" answer (FHQ-179), not an error.
    /// </summary>
    public async Task<PreprodLocation?> GetLocationAsync(CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync("api/settings/location", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadRequiredAsync<PreprodLocation>(response, "api/settings/location", ct);
    }

    public async Task<PreprodTimeZone> GetTimeZoneAsync(CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync("api/settings/timezone", ct);
        return await ReadRequiredAsync<PreprodTimeZone>(response, "api/settings/timezone", ct);
    }

    public async Task<IReadOnlyList<PreprodCalendar>> GetCalendarsAsync(CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync("api/calendars", ct);
        return await ReadRequiredAsync<List<PreprodCalendar>>(response, "api/calendars", ct);
    }

    public async Task<IReadOnlyList<PreprodWebhookRegistration>> GetWebhookRegistrationsAsync(
        CancellationToken ct = default)
    {
        const string path = "api/diagnostics/webhook-registrations";
        using var response = await _httpClient.GetAsync(path, ct);
        return await ReadRequiredAsync<List<PreprodWebhookRegistration>>(response, path, ct);
    }

    /// <summary>
    /// Every distinct event preprod serves for <paramref name="months"/>, de-duplicated by Google event
    /// id. The month view repeats a multi-day event under each date it spans, and a recurring series
    /// arrives as one entry per instance with its own compound id — so "distinct by Google event id" is
    /// the occurrence set, which is exactly what the recurrence scenarios compare against Google.
    /// </summary>
    public async Task<IReadOnlyList<PreprodEvent>> GetEventsAsync(
        IReadOnlyList<DateOnly> months, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(months);

        var byGoogleId = new Dictionary<string, PreprodEvent>(StringComparer.Ordinal);

        foreach (var month in months)
        {
            var path = $"api/calendars/events?year={month.Year}&month={month.Month}";
            using var response = await _httpClient.GetAsync(path, ct);
            var monthView = await ReadRequiredAsync<PreprodMonthView>(response, path, ct);

            foreach (var occurrence in monthView.Days.Values.SelectMany(day => day))
            {
                byGoogleId.TryAdd(occurrence.GoogleEventId, occurrence);
            }
        }

        return [.. byGoogleId.Values];
    }

    public void Dispose() => _httpClient.Dispose();

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response, string path, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"preprod answered HTTP {(int)response.StatusCode} to GET {path}: "
                + SecretGuard.Scrub(body));
        }

        return JsonSerializer.Deserialize<T>(body, SmokeJson.Options)
               ?? throw new HttpRequestException(
                   $"preprod answered HTTP {(int)response.StatusCode} to GET {path} with a body that "
                   + $"did not deserialise to {typeof(T).Name}.");
    }
}
