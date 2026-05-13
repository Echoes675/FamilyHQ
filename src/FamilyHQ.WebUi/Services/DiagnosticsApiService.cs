using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Services;

public class DiagnosticsApiService(HttpClient httpClient) : IDiagnosticsApiService
{
    public async Task<ConnectionStatusWithCalendarsDto?> GetConnectionStatusAsync(CancellationToken ct = default)
    {
        // Diagnostics is non-critical signal — degrade gracefully on network / auth failures,
        // matching CalendarApiService.GetConnectionStatusAsync. Caller treats null as "unknown".
        try
        {
            var response = await httpClient.GetAsync("api/diagnostics/connection-status", ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<ConnectionStatusWithCalendarsDto>(cancellationToken: ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<SyncFailureDto>> GetSyncFailuresAsync(int limit = 100, CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/diagnostics/sync-failures?limit={limit}", ct);
            if (!response.IsSuccessStatusCode) return Array.Empty<SyncFailureDto>();

            return await response.Content.ReadFromJsonAsync<IReadOnlyList<SyncFailureDto>>(cancellationToken: ct)
                   ?? Array.Empty<SyncFailureDto>();
        }
        catch (HttpRequestException)
        {
            return Array.Empty<SyncFailureDto>();
        }
        catch (TaskCanceledException)
        {
            return Array.Empty<SyncFailureDto>();
        }
    }
}
