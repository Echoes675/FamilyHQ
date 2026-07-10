using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using FamilyHQ.Data.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Repositories;

/// <summary>
/// Pure unit tests for the claim <em>retry policy</em> in <see cref="CalendarSyncJobRepository.ClaimNextAsync"/>.
/// The EF mechanism is fully substituted via <see cref="ISyncJobClaimStore"/> — no database, no InMemory
/// provider. The <see cref="FamilyHqDbContext"/> ctor arg is an inert, provider-less instance this code
/// path never touches; it only exists to satisfy the constructor.
/// </summary>
public class CalendarSyncJobClaimPolicyTests
{
    private static readonly Guid Job1Id = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Job2Id = new("22222222-2222-2222-2222-222222222222");

    private readonly Mock<ISyncJobClaimStore> _claimStore = new(MockBehavior.Strict);
    private readonly Mock<ILogger<CalendarSyncJobRepository>> _logger = new();

    private CalendarSyncJobRepository CreateSut() =>
        new(InertContext(), TimeProvider.System, _claimStore.Object, _logger.Object);

    private static FamilyHqDbContext InertContext() =>
        new(new DbContextOptionsBuilder<FamilyHqDbContext>().Options);

    private static CalendarSyncJob Pending(Guid id) => new() { Id = id, UserId = "u", Status = SyncJobStatus.Pending };

    [Fact]
    public async Task ClaimNextAsync_WhenFirstClaimLosesRace_RetriesAndClaimsNextJob()
    {
        var job1 = Pending(Job1Id);
        var job2 = Pending(Job2Id);
        _claimStore.SetupSequence(s => s.FindNextClaimableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(job1)
            .ReturnsAsync(job2);
        _claimStore.Setup(s => s.TryClaimAsync(job1, It.IsAny<CancellationToken>())).ReturnsAsync(false); // lost the race
        _claimStore.Setup(s => s.TryClaimAsync(job2, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var claimed = await CreateSut().ClaimNextAsync();

        claimed.Should().BeSameAs(job2);
        _claimStore.Verify(s => s.FindNextClaimableAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ClaimNextAsync_WhenQueueEmpty_ReturnsNullWithoutClaiming()
    {
        _claimStore.Setup(s => s.FindNextClaimableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarSyncJob?)null);

        var claimed = await CreateSut().ClaimNextAsync();

        claimed.Should().BeNull();
        _claimStore.Verify(s => s.TryClaimAsync(It.IsAny<CalendarSyncJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClaimNextAsync_WhenContentionExceedsBudget_ReturnsNullAndWarns()
    {
        // Every candidate is stolen by a competing worker before we can claim it.
        _claimStore.Setup(s => s.FindNextClaimableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Pending(Job1Id));
        _claimStore.Setup(s => s.TryClaimAsync(It.IsAny<CalendarSyncJob>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var claimed = await CreateSut().ClaimNextAsync();

        claimed.Should().BeNull();
        _claimStore.Verify(s => s.TryClaimAsync(It.IsAny<CalendarSyncJob>(), It.IsAny<CancellationToken>()), Times.Exactly(5));
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
