using FamilyHQ.Data;
using FamilyHQ.Data.PostgreSQL.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace FamilyHQ.Services.Tests.Data;

/// <summary>
/// Verifies the shared-context convention (FHQ-146) fails the model build when no provider supplied a
/// concurrency token, and passes with the production Npgsql customizer. Model construction opens no
/// database connection.
/// </summary>
public class SyncJobConcurrencyTokenConventionTests
{
    [Fact]
    public void ModelBuild_WithoutProviderToken_ThrowsFromConvention()
    {
        var options = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseNpgsql("Host=localhost;Database=irrelevant")
            .Options;
        using var context = new FamilyHqDbContext(options);

        var act = () => _ = context.Model; // finalizes the model, running the convention

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CalendarSyncJob*concurrency token*");
    }

    [Fact]
    public void ModelBuild_WithNpgsqlCustomizer_Succeeds()
    {
        var options = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseNpgsql("Host=localhost;Database=irrelevant")
            .ReplaceService<IModelCustomizer, NpgsqlModelCustomizer>()
            .Options;
        using var context = new FamilyHqDbContext(options);

        var act = () => _ = context.Model;

        act.Should().NotThrow();
    }
}
