using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

public class SyncFailureRepository(FamilyHqDbContext context) : ISyncFailureRepository
{
    public async Task AddAsync(SyncEventFailure failure, CancellationToken ct = default)
    {
        context.SyncEventFailures.Add(failure);
        await context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SyncEventFailure>> GetRecentAsync(string userId, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
            return Array.Empty<SyncEventFailure>();

        return await context.SyncEventFailures
            .AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.FailedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task MarkResolvedAsync(Guid id, CancellationToken ct = default)
    {
        var failure = await context.SyncEventFailures.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (failure is null) return;

        failure.Resolved = true;
        await context.SaveChangesAsync(ct);
    }
}
