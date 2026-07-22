using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using FamilyHQ.Data.PostgreSQL.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FamilyHQ.Services.Tests.Repositories;

/// <summary>
/// Verifies the FHQ-71 optimistic-concurrency wiring at the model level. Builds the Npgsql model
/// with no database connection (model construction never opens one) and asserts the <c>xmin</c>
/// concurrency token is configured — the guard against someone removing the customizer line.
/// </summary>
public class NpgsqlModelCustomizerTests
{
    private static IModel BuildNpgsqlModel()
    {
        var options = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseNpgsql("Host=localhost;Database=irrelevant")
            .ReplaceService<IModelCustomizer, NpgsqlModelCustomizer>()
            .Options;
        using var context = new FamilyHqDbContext(options);
        return context.Model;
    }

    [Fact]
    public void CalendarSyncJob_HasXminOptimisticConcurrencyToken()
    {
        var xmin = BuildNpgsqlModel()
            .FindEntityType(typeof(CalendarSyncJob))!
            .FindProperty("xmin");

        xmin.Should().NotBeNull();
        xmin!.ClrType.Should().Be(typeof(uint));
        xmin.IsConcurrencyToken.Should().BeTrue();
        xmin.ValueGenerated.Should().Be(ValueGenerated.OnAddOrUpdate);
    }

    [Fact]
    public void UserToken_HasXminOptimisticConcurrencyToken()
    {
        var xmin = BuildNpgsqlModel()
            .FindEntityType(typeof(UserToken))!
            .FindProperty("xmin");

        xmin.Should().NotBeNull();
        xmin!.ClrType.Should().Be(typeof(uint));
        xmin.IsConcurrencyToken.Should().BeTrue();
        xmin.ValueGenerated.Should().Be(ValueGenerated.OnAddOrUpdate);
    }
}
