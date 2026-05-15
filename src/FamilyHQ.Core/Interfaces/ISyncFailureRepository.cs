using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ISyncFailureRepository
{
    Task AddAsync(SyncEventFailure failure, CancellationToken ct = default);

    Task<IReadOnlyList<SyncEventFailure>> GetRecentAsync(string userId, int limit, CancellationToken ct = default);

    Task MarkResolvedAsync(Guid id, CancellationToken ct = default);
}
