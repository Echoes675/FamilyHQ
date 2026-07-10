using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FamilyHQ.Data.PostgreSQL.Infrastructure;

/// <summary>
/// Applies Npgsql-specific model configuration that cannot live in the provider-agnostic
/// <c>FamilyHQ.Data</c> project (which does not reference Npgsql). Runs after the shared
/// model configuration so entity types are already built.
/// </summary>
public sealed class NpgsqlModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        // FHQ-71: optimistic concurrency for the sync-job claim. A uint / OnAddOrUpdate / concurrency-token
        // property mapped to the `xmin` column is recognised by Npgsql's model-finalizing convention as the
        // Postgres system column, so it adds no schema/migration. (The removed-in-EFCore-10
        // `UseXminAsConcurrencyToken()` helper expanded to exactly this.) `ClaimNextAsync` relies on the
        // resulting DbUpdateConcurrencyException to detect a job claimed by a competing worker.
        // FHQ-119 should apply the same three lines to UserToken.
        modelBuilder.Entity<CalendarSyncJob>()
            .Property<uint>("xmin")
            .HasColumnName("xmin")
            .IsConcurrencyToken()
            .ValueGeneratedOnAddOrUpdate();
    }
}
