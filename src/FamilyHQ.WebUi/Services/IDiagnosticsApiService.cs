using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Services;

public interface IDiagnosticsApiService
{
    Task<DiagnosticsLoadResult<ConnectionStatusWithCalendarsDto>> GetConnectionStatusAsync(CancellationToken ct = default);
    Task<DiagnosticsLoadResult<IReadOnlyList<SyncFailureDto>>> GetSyncFailuresAsync(int limit = 100, CancellationToken ct = default);
    Task<DiagnosticsLoadResult<IReadOnlyList<FailedSyncRunDto>>> GetFailedSyncRunsAsync(int limit = 100, CancellationToken ct = default);
}
