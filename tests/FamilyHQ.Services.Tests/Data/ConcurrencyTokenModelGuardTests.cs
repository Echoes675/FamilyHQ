using FamilyHQ.Core.Models;
using FamilyHQ.Data.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace FamilyHQ.Services.Tests.Data;

/// <summary>
/// Pure unit tests for the provider-agnostic concurrency-token guard (FHQ-119, generalizing FHQ-146).
/// Uses a provider-free <see cref="ModelBuilder"/> — no database, no provider, no InMemory.
/// </summary>
public class ConcurrencyTokenModelGuardTests
{
    private static IReadOnlyModel BuildModel(bool syncJobToken, bool userTokenToken)
    {
        var builder = new ModelBuilder();

        var job = builder.Entity<CalendarSyncJob>();
        if (syncJobToken)
            job.Property<uint>("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();

        var token = builder.Entity<UserToken>();
        if (userTokenToken)
            token.Property<uint>("xmin").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();

        return builder.Model;
    }

    [Fact]
    public void EnsureConcurrencyTokens_WhenUserTokenMissing_Throws()
    {
        var act = () => ConcurrencyTokenModelGuard.EnsureConcurrencyTokens(
            BuildModel(syncJobToken: true, userTokenToken: false));

        act.Should().Throw<InvalidOperationException>().WithMessage("*UserToken*concurrency token*");
    }

    [Fact]
    public void EnsureConcurrencyTokens_WhenCalendarSyncJobMissing_Throws()
    {
        var act = () => ConcurrencyTokenModelGuard.EnsureConcurrencyTokens(
            BuildModel(syncJobToken: false, userTokenToken: true));

        act.Should().Throw<InvalidOperationException>().WithMessage("*CalendarSyncJob*concurrency token*");
    }

    [Fact]
    public void EnsureConcurrencyTokens_WhenAllTokensPresent_DoesNotThrow()
    {
        var act = () => ConcurrencyTokenModelGuard.EnsureConcurrencyTokens(
            BuildModel(syncJobToken: true, userTokenToken: true));

        act.Should().NotThrow();
    }
}
