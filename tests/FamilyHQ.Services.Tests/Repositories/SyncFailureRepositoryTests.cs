using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using FamilyHQ.Data.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FamilyHQ.Services.Tests.Repositories;

public class SyncFailureRepositoryTests : IDisposable
{
    private readonly FamilyHqDbContext _dbContext;

    public SyncFailureRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FamilyHqDbContext(options);
    }

    public void Dispose() => _dbContext.Dispose();

    private SyncFailureRepository CreateSut() => new(_dbContext);

    private static SyncEventFailure NewFailure(string userId, string googleEventId, DateTimeOffset failedAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        CalendarInfoId = Guid.NewGuid(),
        GoogleEventId = googleEventId,
        EventTitle = "Test",
        FailureReason = "boom",
        ExceptionType = "System.InvalidOperationException",
        FailedAt = failedAt,
        Resolved = false
    };

    [Fact]
    public async Task AddAsync_ThenGetRecentAsync_ReturnsTheFailureForThatUser()
    {
        // Arrange
        var sut = CreateSut();
        var failure = NewFailure("u-1", "evt-1", DateTimeOffset.UtcNow);

        // Act
        await sut.AddAsync(failure);
        var result = await sut.GetRecentAsync("u-1", limit: 10);

        // Assert
        result.Should().ContainSingle()
            .Which.GoogleEventId.Should().Be("evt-1");
    }

    [Fact]
    public async Task GetRecentAsync_OrdersByFailedAtDescending()
    {
        // Arrange
        var sut = CreateSut();
        var older  = NewFailure("u-2", "evt-old", new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var newer  = NewFailure("u-2", "evt-new", new DateTimeOffset(2026, 5, 5, 0, 0, 0, TimeSpan.Zero));
        var middle = NewFailure("u-2", "evt-mid", new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero));

        await sut.AddAsync(older);
        await sut.AddAsync(newer);
        await sut.AddAsync(middle);

        // Act
        var result = await sut.GetRecentAsync("u-2", limit: 10);

        // Assert
        result.Select(f => f.GoogleEventId).Should().ContainInOrder("evt-new", "evt-mid", "evt-old");
    }

    [Fact]
    public async Task GetRecentAsync_HonoursLimit()
    {
        // Arrange
        var sut = CreateSut();
        for (int i = 0; i < 5; i++)
            await sut.AddAsync(NewFailure("u-3", $"evt-{i}", new DateTimeOffset(2026, 5, 1, i, 0, 0, TimeSpan.Zero)));

        // Act
        var result = await sut.GetRecentAsync("u-3", limit: 2);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRecentAsync_OnlyReturnsFailuresForGivenUser()
    {
        // Arrange
        var sut = CreateSut();
        await sut.AddAsync(NewFailure("u-4", "mine", DateTimeOffset.UtcNow));
        await sut.AddAsync(NewFailure("u-other", "theirs", DateTimeOffset.UtcNow));

        // Act
        var mine = await sut.GetRecentAsync("u-4", limit: 10);

        // Assert
        mine.Should().ContainSingle().Which.GoogleEventId.Should().Be("mine");
    }

    [Fact]
    public async Task MarkResolvedAsync_FlipsResolvedFlag()
    {
        // Arrange
        var sut = CreateSut();
        var failure = NewFailure("u-5", "evt-resolve", DateTimeOffset.UtcNow);
        await sut.AddAsync(failure);

        // Act
        await sut.MarkResolvedAsync(failure.Id);

        // Assert
        var refreshed = await _dbContext.SyncEventFailures.FindAsync(failure.Id);
        refreshed.Should().NotBeNull();
        refreshed!.Resolved.Should().BeTrue();
    }
}
