using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace FamilyHQ.Data.Infrastructure;

/// <summary>
/// Runs at model finalization — after the provider's <c>IModelCustomizer</c> has had its chance to add
/// a token — and enforces <see cref="SyncJobModelGuard"/> for whatever provider is active. Wired into
/// the shared <c>FamilyHqDbContext</c> so no provider can opt out (FHQ-146).
/// </summary>
public sealed class RequireSyncJobConcurrencyTokenConvention : IModelFinalizedConvention
{
    public IModel ProcessModelFinalized(IModel model)
    {
        SyncJobModelGuard.EnsureCalendarSyncJobConcurrencyToken(model);
        return model;
    }
}
