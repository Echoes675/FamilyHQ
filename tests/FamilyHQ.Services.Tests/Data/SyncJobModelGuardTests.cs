using FamilyHQ.Core.Models;
using FamilyHQ.Data.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FamilyHQ.Services.Tests.Data;

/// <summary>
/// Pure unit tests for the provider-agnostic concurrency-token guard (FHQ-146). Uses a provider-free
/// <see cref="ModelBuilder"/> — no database, no provider, no InMemory — so it only exercises the
/// metadata check itself.
/// </summary>
public class SyncJobModelGuardTests
{
    private static IReadOnlyModel BuildModel(bool withToken)
    {
        var builder = new ModelBuilder();
        var entity = builder.Entity<CalendarSyncJob>();
        if (withToken)
            entity.Property<uint>("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
        return builder.Model;
    }

    [Fact]
    public void EnsureCalendarSyncJobConcurrencyToken_WhenTokenMissing_Throws()
    {
        var act = () => SyncJobModelGuard.EnsureCalendarSyncJobConcurrencyToken(BuildModel(withToken: false));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CalendarSyncJob*concurrency token*");
    }

    [Fact]
    public void EnsureCalendarSyncJobConcurrencyToken_WhenTokenPresent_DoesNotThrow()
    {
        var act = () => SyncJobModelGuard.EnsureCalendarSyncJobConcurrencyToken(BuildModel(withToken: true));

        act.Should().NotThrow();
    }
}
