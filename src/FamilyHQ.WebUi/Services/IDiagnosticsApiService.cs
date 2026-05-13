using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Services;

public interface IDiagnosticsApiService
{
    Task<ConnectionStatusWithCalendarsDto?> GetConnectionStatusAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SyncFailureDto>> GetSyncFailuresAsync(int limit = 100, CancellationToken ct = default);
}
