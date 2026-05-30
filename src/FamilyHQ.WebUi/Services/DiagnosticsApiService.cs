using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Services;

public class DiagnosticsApiService(HttpClient httpClient) : IDiagnosticsApiService
{
    public async Task<DiagnosticsLoadResult<ConnectionStatusWithCalendarsDto>> GetConnectionStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.GetAsync("api/diagnostics/connection-status", ct);
            if (!response.IsSuccessStatusCode)
                return DiagnosticsLoadResult<ConnectionStatusWithCalendarsDto>.Failed();

            var data = await response.Content.ReadFromJsonAsync<ConnectionStatusWithCalendarsDto>(cancellationToken: ct);
            return data is null
                ? DiagnosticsLoadResult<ConnectionStatusWithCalendarsDto>.Failed()
                : DiagnosticsLoadResult<ConnectionStatusWithCalendarsDto>.Ok(data);
        }
        catch (HttpRequestException)
        {
            return DiagnosticsLoadResult<ConnectionStatusWithCalendarsDto>.Failed();
        }
        catch (TaskCanceledException)
        {
            return DiagnosticsLoadResult<ConnectionStatusWithCalendarsDto>.Failed();
        }
    }

    public async Task<DiagnosticsLoadResult<IReadOnlyList<SyncFailureDto>>> GetSyncFailuresAsync(int limit = 100, CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/diagnostics/sync-failures?limit={limit}", ct);
            if (!response.IsSuccessStatusCode)
                return DiagnosticsLoadResult<IReadOnlyList<SyncFailureDto>>.Failed();

            var data = await response.Content.ReadFromJsonAsync<IReadOnlyList<SyncFailureDto>>(cancellationToken: ct);
            return DiagnosticsLoadResult<IReadOnlyList<SyncFailureDto>>.Ok(data ?? Array.Empty<SyncFailureDto>());
        }
        catch (HttpRequestException)
        {
            return DiagnosticsLoadResult<IReadOnlyList<SyncFailureDto>>.Failed();
        }
        catch (TaskCanceledException)
        {
            return DiagnosticsLoadResult<IReadOnlyList<SyncFailureDto>>.Failed();
        }
    }

    public async Task<DiagnosticsLoadResult<IReadOnlyList<FailedSyncRunDto>>> GetFailedSyncRunsAsync(int limit = 100, CancellationToken ct = default)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/diagnostics/failed-sync-runs?limit={limit}", ct);
            if (!response.IsSuccessStatusCode)
                return DiagnosticsLoadResult<IReadOnlyList<FailedSyncRunDto>>.Failed();

            var data = await response.Content.ReadFromJsonAsync<IReadOnlyList<FailedSyncRunDto>>(cancellationToken: ct);
            return DiagnosticsLoadResult<IReadOnlyList<FailedSyncRunDto>>.Ok(data ?? Array.Empty<FailedSyncRunDto>());
        }
        catch (HttpRequestException)
        {
            return DiagnosticsLoadResult<IReadOnlyList<FailedSyncRunDto>>.Failed();
        }
        catch (TaskCanceledException)
        {
            return DiagnosticsLoadResult<IReadOnlyList<FailedSyncRunDto>>.Failed();
        }
    }
}
