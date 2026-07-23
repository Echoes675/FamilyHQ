using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FamilyHQ.Data.Infrastructure;

/// <summary>
/// Provider-agnostic guard (FHQ-71 / FHQ-146 / FHQ-119). Certain entities are only safe under their
/// read-modify-save policies because they carry an optimistic-concurrency token, so a lost race
/// surfaces as <c>DbUpdateConcurrencyException</c>: <see cref="CalendarSyncJob"/> (sync-job claim) and
/// <see cref="UserToken"/> (token refresh / re-consent). A provider that forgets a token (Postgres:
/// <c>xmin</c>; SQL Server: <c>rowversion</c>) would silently degrade to a lost-update bug. This guard,
/// invoked from a shared model convention, makes that fail the model build for any provider.
/// </summary>
public static class ConcurrencyTokenModelGuard
{
    private static readonly Type[] RequiredTokenEntities =
    {
        typeof(CalendarSyncJob),
        typeof(UserToken),
    };

    public static void EnsureConcurrencyTokens(IReadOnlyModel model)
    {
        foreach (var clrType in RequiredTokenEntities)
        {
            var entity = model.FindEntityType(clrType);
            if (entity is null)
                continue; // not mapped in this model — nothing to protect.

            var hasToken = entity.GetProperties().Any(p => p.IsConcurrencyToken);
            if (!hasToken)
                throw new InvalidOperationException(
                    $"{clrType.Name} requires an optimistic-concurrency token for safe read-modify-save " +
                    "(FHQ-71 / FHQ-119). The active database provider configured none. Add one in the " +
                    "provider's model customizer (Postgres: the 'xmin' system column; SQL Server: a " +
                    "'rowversion' property via IsRowVersion()).");
        }
    }
}
