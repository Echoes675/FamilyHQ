using FamilyHQ.Core.Models;
using FamilyHQ.Data.Repositories;
using FamilyHQ.Services.Tests.Fakes;
using FluentAssertions;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Repositories;

public class WebhookRegistrationRepositoryTests
{
    private readonly FakeFamilyHqDbContext _db = new();

    private WebhookRegistrationRepository CreateSut() => new(_db);

    [Fact]
    public async Task UpsertAsync_NoExistingRegistration_InsertsWithChannelToken()
    {
        var mockSet = _db.Setup<WebhookRegistration>();
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

        mockSet.Verify(s => s.Add(It.Is<WebhookRegistration>(w =>
            w.CalendarInfoId == calendarInfoId &&
            w.ChannelId == "chan-1" &&
            w.ResourceId == "res-1" &&
            w.ChannelToken == "token-1")), Times.Once);
        _db.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task UpsertAsync_ExistingRegistration_ReplacesStaleChannelTokenWithRenewedOne()
    {
        // A webhook renewal generates a brand-new channel ID + token and re-registers with Google.
        // The stored registration must reflect the new token, or Google's push notifications on the
        // renewed channel will be rejected as a token mismatch (FHQ-135).
        var calendarInfoId = Guid.NewGuid();
        var existing = new WebhookRegistration
        {
            CalendarInfoId = calendarInfoId,
            ChannelId = "chan-1",
            ResourceId = "res-1",
            ChannelToken = "old-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            RegisteredAt = DateTimeOffset.UtcNow
        };
        var mockSet = _db.Setup<WebhookRegistration>([existing]);
        var sut = CreateSut();

        await sut.UpsertAsync(new WebhookRegistration
        {
            CalendarInfoId = calendarInfoId,
            ChannelId = "chan-2",
            ResourceId = "res-2",
            ChannelToken = "new-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
            RegisteredAt = DateTimeOffset.UtcNow
        });

        existing.ChannelId.Should().Be("chan-2");
        existing.ChannelToken.Should().Be("new-token");
        mockSet.Verify(s => s.Add(It.IsAny<WebhookRegistration>()), Times.Never);
        _db.SaveChangesCount.Should().Be(1);
    }
}
