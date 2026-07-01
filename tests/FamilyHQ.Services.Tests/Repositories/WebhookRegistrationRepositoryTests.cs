using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using FamilyHQ.Data.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FamilyHQ.Services.Tests.Repositories;

public class WebhookRegistrationRepositoryTests : IDisposable
{
    private readonly FamilyHqDbContext _db;

    public WebhookRegistrationRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new FamilyHqDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private WebhookRegistrationRepository CreateSut() => new(_db);

    [Fact]
    public async Task UpsertAsync_NoExistingRegistration_InsertsWithChannelToken()
    {
        var sut = CreateSut();
        var calendarInfoId = Guid.NewGuid();

        await sut.UpsertAsync(new WebhookRegistration
        {
            CalendarInfoId = calendarInfoId,
            ChannelId = "chan-1",
            ResourceId = "res-1",
            ChannelToken = "token-1",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            RegisteredAt = DateTimeOffset.UtcNow
        });

        var stored = await _db.WebhookRegistrations.SingleAsync(w => w.CalendarInfoId == calendarInfoId);
        stored.ChannelToken.Should().Be("token-1");
    }

    [Fact]
    public async Task UpsertAsync_ExistingRegistration_ReplacesStaleChannelTokenWithRenewedOne()
    {
        // A webhook renewal generates a brand-new channel ID + token and re-registers with Google.
        // The stored registration must reflect the new token, or Google's push notifications on the
        // renewed channel will be rejected as a token mismatch (FHQ-135).
        var sut = CreateSut();
        var calendarInfoId = Guid.NewGuid();

        await sut.UpsertAsync(new WebhookRegistration
        {
            CalendarInfoId = calendarInfoId,
            ChannelId = "chan-1",
            ResourceId = "res-1",
            ChannelToken = "old-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            RegisteredAt = DateTimeOffset.UtcNow
        });

        await sut.UpsertAsync(new WebhookRegistration
        {
            CalendarInfoId = calendarInfoId,
            ChannelId = "chan-2",
            ResourceId = "res-2",
            ChannelToken = "new-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
            RegisteredAt = DateTimeOffset.UtcNow
        });

        var stored = await _db.WebhookRegistrations.SingleAsync(w => w.CalendarInfoId == calendarInfoId);
        stored.ChannelId.Should().Be("chan-2");
        stored.ChannelToken.Should().Be("new-token");
    }
}
