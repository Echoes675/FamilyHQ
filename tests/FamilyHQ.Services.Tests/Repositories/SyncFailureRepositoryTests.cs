using FamilyHQ.Core.Models;
using FamilyHQ.Data.Repositories;
using FamilyHQ.Services.Tests.Fakes;
using FluentAssertions;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Repositories;

public class SyncFailureRepositoryTests
{
    private readonly FakeFamilyHqDbContext _db = new();

    private SyncFailureRepository CreateSut() => new(_db);

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
    public async Task AddAsync_CallsAddAndSaveChanges()
    {
        // Arrange
        var mockSet = _db.Setup<SyncEventFailure>();
        var sut = CreateSut();
        var failure = NewFailure("u-1", "evt-1", DateTimeOffset.UtcNow);

        // Act
        await sut.AddAsync(failure);

        // Assert
        mockSet.Verify(s => s.Add(It.Is<SyncEventFailure>(f =>
            f.UserId == "u-1" && f.GoogleEventId == "evt-1")), Times.Once);
        _db.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task GetRecentAsync_OrdersByFailedAtDescending()
    {
        // Arrange
        var older  = NewFailure("u-2", "evt-old", new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        var newer  = NewFailure("u-2", "evt-new", new DateTimeOffset(2026, 5, 5, 0, 0, 0, TimeSpan.Zero));
        var middle = NewFailure("u-2", "evt-mid", new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero));
        _db.Setup<SyncEventFailure>([older, newer, middle]);
        var sut = CreateSut();

        // Act
        var result = await sut.GetRecentAsync("u-2", limit: 10);

        // Assert
        result.Select(f => f.GoogleEventId).Should().ContainInOrder("evt-new", "evt-mid", "evt-old");
    }

    [Fact]
    public async Task GetRecentAsync_HonoursLimit()
    {
        // Arrange
        var failures = Enumerable.Range(0, 5)
            .Select(i => NewFailure("u-3", $"evt-{i}", new DateTimeOffset(2026, 5, 1, i, 0, 0, TimeSpan.Zero)))
            .ToList();
        _db.Setup<SyncEventFailure>(failures);
        var sut = CreateSut();

        // Act
        var result = await sut.GetRecentAsync("u-3", limit: 2);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRecentAsync_OnlyReturnsFailuresForGivenUser()
    {
        // Arrange
        var mine = NewFailure("u-4", "mine", DateTimeOffset.UtcNow);
        var theirs = NewFailure("u-other", "theirs", DateTimeOffset.UtcNow);
        _db.Setup<SyncEventFailure>([mine, theirs]);
        var sut = CreateSut();

        // Act
        var result = await sut.GetRecentAsync("u-4", limit: 10);

        // Assert
        result.Should().ContainSingle().Which.GoogleEventId.Should().Be("mine");
    }

    [Fact]
    public async Task MarkResolvedAsync_FlipsResolvedFlag()
    {
        // Arrange
        var failure = NewFailure("u-5", "evt-resolve", DateTimeOffset.UtcNow);
        _db.Setup<SyncEventFailure>([failure]);
        var sut = CreateSut();

        // Act
        await sut.MarkResolvedAsync(failure.Id);

        // Assert
        failure.Resolved.Should().BeTrue();
        _db.SaveChangesCount.Should().Be(1);
    }
}
