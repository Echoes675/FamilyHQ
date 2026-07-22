using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FamilyHQ.Data.Infrastructure;

/// <summary>
/// Provider-agnostic guard (FHQ-71 / FHQ-146). The sync-job claim policy in
/// <c>CalendarSyncJobRepository.ClaimNextAsync</c> is only correct because <see cref="CalendarSyncJob"/>
/// carries an optimistic-concurrency token, so a lost claim race surfaces as
/// <c>DbUpdateConcurrencyException</c>. A provider that forgets its token (Postgres: <c>xmin</c>;
/// SQL Server: <c>rowversion</c>) would silently degrade the claim to a double-claim bug. This guard
/// makes that impossible to ship: it is invoked from the shared model convention, so any provider's
/// model build fails loudly when the token is absent.
/// </summary>
public static class SyncJobModelGuard
{
    public static void EnsureCalendarSyncJobConcurrencyToken(IReadOnlyModel model)
    {
        var entity = model.FindEntityType(typeof(CalendarSyncJob));
        if (entity is null)
            return; // CalendarSyncJob is not mapped in this model — nothing to protect.

        var hasToken = entity.GetProperties().Any(p => p.IsConcurrencyToken);
        if (!hasToken)
            throw new InvalidOperationException(
                "CalendarSyncJob requires an optimistic-concurrency token for safe sync-job claiming " +
                "(FHQ-71 / FHQ-146). The active database provider configured none. Add one in the " +
                "provider's model customizer (Postgres: the 'xmin' system column; SQL Server: a " +
                "'rowversion' property via IsRowVersion()).");
    }
}
