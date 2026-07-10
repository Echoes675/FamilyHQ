using FamilyHQ.Data;
using FamilyHQ.Data.PostgreSQL.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FamilyHQ.Services.Tests.Repositories;

/// <summary>
/// Guards the invariant that <c>Database.Migrate()</c> depends on: the runtime model must match the
/// migrations snapshot, or EF throws <c>PendingModelChangesWarning</c> on startup and the app never boots.
/// FHQ-71 hit exactly that — the <c>xmin</c> token was applied at runtime but absent from the snapshot.
/// Builds the model with no database connection (model construction never opens one).
/// </summary>
public class MigrationsSnapshotTests
{
    [Fact]
    public void RuntimeModel_HasNoPendingChanges_AgainstMigrationsSnapshot()
    {
        using var context = new FamilyHqDbContext(RuntimeEquivalentOptions());

        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot;
        snapshot.Should().NotBeNull("the migrations assembly must expose a model snapshot");

        var differ = context.GetService<IMigrationsModelDiffer>();
        var initializer = context.GetService<IModelRuntimeInitializer>();

        var snapshotModel = initializer.Initialize(
            ((IMutableModel)snapshot!.Model).FinalizeModel(), designTime: true);
        var runtimeModel = context.GetService<IDesignTimeModel>().Model;

        var differences = differ.GetDifferences(
            snapshotModel.GetRelationalModel(),
            runtimeModel.GetRelationalModel());

        differences.Should().BeEmpty(
            "the model has changes not captured in a migration — Database.Migrate() would throw " +
            "PendingModelChangesWarning at startup. Add a migration.");
    }

    /// <summary>Mirrors the options built in <c>AddPostgreSqlDataAccess</c> (and DesignTimeDbContextFactory).</summary>
    private static DbContextOptions<FamilyHqDbContext> RuntimeEquivalentOptions() =>
        new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=irrelevant",
                x => x.MigrationsAssembly(typeof(NpgsqlModelCustomizer).Assembly.FullName))
            .ReplaceService<IModelCustomizer, NpgsqlModelCustomizer>()
            .Options;
}
