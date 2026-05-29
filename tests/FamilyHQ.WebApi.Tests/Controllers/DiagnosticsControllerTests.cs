using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class DiagnosticsControllerTests
{
    private static readonly Guid CalAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CalBId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ── GetConnectionStatus ──────────────────────────────────────────────────

    [Fact]
    public async Task GetConnectionStatus_WhenTokenActive_ReturnsActiveWithPerCalendarLastSyncedAt()
    {
        // Arrange
        var (calendarRepo, tokenStore, _, _, currentUser, sut) = CreateSut();
        currentUser.SetupGet(c => c.UserId).Returns("u-1");
        var lastSyncedA = new DateTimeOffset(2026, 5, 12, 9, 0, 0, TimeSpan.Zero);
        var calA = new CalendarInfo
        {
            Id = CalAId, DisplayName = "A", UserId = "u-1",
            SyncState = new SyncState { LastSyncedAt = lastSyncedA }
        };
        var calB = new CalendarInfo
        {
            Id = CalBId, DisplayName = "B", UserId = "u-1",
            SyncState = null
        };

        calendarRepo.Setup(r => r.GetCalendarsByUserIdAsync("u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calA, calB });
        calendarRepo.Setup(r => r.GetSyncStateAsync(CalAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncState { CalendarInfoId = CalAId, LastSyncedAt = lastSyncedA });
        calendarRepo.Setup(r => r.GetSyncStateAsync(CalBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);

        tokenStore.Setup(t => t.GetAuthStatusAsync("u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthStatusResult(TokenAuthStatus.Active, null, null));

        // Act
        var result = await sut.GetConnectionStatus(CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ConnectionStatusWithCalendarsDto>().Subject;
        dto.Status.Should().Be("active");
        dto.LastError.Should().BeNull();
        dto.Since.Should().BeNull();
        dto.Calendars.Should().HaveCount(2);
        dto.Calendars[0].CalendarId.Should().Be(CalAId);
        dto.Calendars[0].DisplayName.Should().Be("A");
        dto.Calendars[0].LastSyncedAt.Should().Be(lastSyncedA);
        dto.Calendars[1].CalendarId.Should().Be(CalBId);
        dto.Calendars[1].LastSyncedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetConnectionStatus_WhenTokenNeedsReauth_ReturnsReauthStatusWithDetails()
    {
        // Arrange
        var (calendarRepo, tokenStore, _, _, currentUser, sut) = CreateSut();
        var since = new DateTimeOffset(2026, 5, 13, 18, 34, 0, TimeSpan.Zero);
        currentUser.SetupGet(c => c.UserId).Returns("u-2");
        calendarRepo.Setup(r => r.GetCalendarsByUserIdAsync("u-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo>());
        tokenStore.Setup(t => t.GetAuthStatusAsync("u-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthStatusResult(TokenAuthStatus.NeedsReauth, "expired", since));

        // Act
        var result = await sut.GetConnectionStatus(CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ConnectionStatusWithCalendarsDto>().Subject;
        dto.Status.Should().Be("needs_reauth");
        dto.LastError.Should().Be("expired");
        dto.Since.Should().Be(since);
    }

    [Fact]
    public async Task GetConnectionStatus_WhenNoCurrentUser_ReturnsUnauthorized()
    {
        // Arrange
        var (_, _, _, _, currentUser, sut) = CreateSut();
        currentUser.SetupGet(c => c.UserId).Returns((string?)null);

        // Act
        var result = await sut.GetConnectionStatus(CancellationToken.None);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task GetConnectionStatus_OnlyReturnsCalendarsOwnedByCurrentUser()
    {
        // Arrange — repository's GetCalendarsByUserIdAsync is the scoping boundary; this
        // test asserts we use it (passing the current user id) rather than the
        // unscoped GetCalendarsAsync.
        var (calendarRepo, tokenStore, _, _, currentUser, sut) = CreateSut();
        currentUser.SetupGet(c => c.UserId).Returns("u-mine");
        calendarRepo.Setup(r => r.GetCalendarsByUserIdAsync("u-mine", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo>
            {
                new CalendarInfo { Id = CalAId, DisplayName = "Mine", UserId = "u-mine" }
            });
        tokenStore.Setup(t => t.GetAuthStatusAsync("u-mine", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthStatusResult(TokenAuthStatus.Active, null, null));

        // Act
        var result = await sut.GetConnectionStatus(CancellationToken.None);

        // Assert — only the current user's id is passed to the repo
        calendarRepo.Verify(r => r.GetCalendarsByUserIdAsync("u-mine", It.IsAny<CancellationToken>()), Times.Once);
        calendarRepo.Verify(r => r.GetCalendarsByUserIdAsync(It.Is<string>(s => s != "u-mine"), It.IsAny<CancellationToken>()), Times.Never);
        result.Should().BeOfType<OkObjectResult>();
    }

    // ── GetSyncFailures ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetSyncFailures_ReturnsUserFailuresAsDtos()
    {
        // Arrange
        var (_, _, failureRepo, _, currentUser, sut) = CreateSut();
        currentUser.SetupGet(c => c.UserId).Returns("u-3");
        var failure = new SyncEventFailure
        {
            Id = Guid.NewGuid(),
            UserId = "u-3",
            CalendarInfoId = CalAId,
            GoogleEventId = "evt-1",
            EventTitle = "Title",
            FailureReason = "boom",
            ExceptionType = "System.InvalidOperationException",
            FailedAt = new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero),
            Resolved = false
        };
        failureRepo.Setup(r => r.GetRecentAsync("u-3", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncEventFailure> { failure });

        // Act
        var result = await sut.GetSyncFailures(limit: 100, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<IReadOnlyList<SyncFailureDto>>().Subject;
        dtos.Should().ContainSingle();
        var dto = dtos[0];
        dto.Id.Should().Be(failure.Id);
        dto.GoogleEventId.Should().Be("evt-1");
        dto.EventTitle.Should().Be("Title");
        dto.FailureReason.Should().Be("boom");
        dto.ExceptionType.Should().Be("System.InvalidOperationException");
    }

    [Fact]
    public async Task GetSyncFailures_WhenNoCurrentUser_ReturnsUnauthorized()
    {
        // Arrange
        var (_, _, _, _, currentUser, sut) = CreateSut();
        currentUser.SetupGet(c => c.UserId).Returns((string?)null);

        // Act
        var result = await sut.GetSyncFailures(limit: 100, CancellationToken.None);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task GetSyncFailures_ScopesToCurrentUserAndDoesNotReturnAnotherUsersData()
    {
        // Arrange — repo is the scoping boundary; assert we pass the current user id only.
        var (_, _, failureRepo, _, currentUser, sut) = CreateSut();
        currentUser.SetupGet(c => c.UserId).Returns("u-me");
        failureRepo.Setup(r => r.GetRecentAsync("u-me", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncEventFailure>());
        failureRepo.Setup(r => r.GetRecentAsync(It.Is<string>(s => s != "u-me"), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncEventFailure>
            {
                new SyncEventFailure { UserId = "u-other", GoogleEventId = "leak" }
            });

        // Act
        var result = await sut.GetSyncFailures(limit: 100, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<IReadOnlyList<SyncFailureDto>>().Subject;
        dtos.Should().BeEmpty();
        failureRepo.Verify(r => r.GetRecentAsync("u-me", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        failureRepo.Verify(r => r.GetRecentAsync(It.Is<string>(s => s != "u-me"), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(501, 500)]
    [InlineData(1000, 500)]
    [InlineData(50, 50)]
    [InlineData(500, 500)]
    public async Task GetSyncFailures_ClampsLimitToValidRange(int requestedLimit, int expectedLimit)
    {
        // Arrange
        var (_, _, failureRepo, _, currentUser, sut) = CreateSut();
        currentUser.SetupGet(c => c.UserId).Returns("u-clamp");
        failureRepo.Setup(r => r.GetRecentAsync("u-clamp", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncEventFailure>());

        // Act
        var result = await sut.GetSyncFailures(limit: requestedLimit, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        failureRepo.Verify(r => r.GetRecentAsync("u-clamp", expectedLimit, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetFailedSyncRuns ────────────────────────────────────────────────────

    [Fact]
    public async Task GetFailedSyncRuns_ReturnsMappedDtos_ForCurrentUser()
    {
        // Arrange
        var (_, _, _, syncJobQueue, currentUser, sut) = CreateSut();
        currentUser.SetupGet(c => c.UserId).Returns("u-1");
        var calId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        syncJobQueue.Setup(q => q.GetRecentFailuresAsync("u-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarSyncJob>
            {
                new()
                {
                    Id = jobId, CalendarInfoId = calId, AttemptCount = 5,
                    LastError = "boom", Source = SyncJobSource.Webhook,
                    Status = SyncJobStatus.Failed,
                    CompletedAt = new DateTimeOffset(2026, 5, 29, 10, 0, 0, TimeSpan.Zero)
                }
            });

        // Act
        var result = await sut.GetFailedSyncRuns(limit: 100, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<IReadOnlyList<FailedSyncRunDto>>().Subject;
        dtos.Should().ContainSingle();
        dtos[0].Id.Should().Be(jobId);
        dtos[0].CalendarInfoId.Should().Be(calId);
        dtos[0].AttemptCount.Should().Be(5);
        dtos[0].LastError.Should().Be("boom");
        dtos[0].Source.Should().Be("Webhook");
        dtos[0].LastAttemptAt.Should().Be(new DateTimeOffset(2026, 5, 29, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetFailedSyncRuns_WhenCompletedAtNull_FallsBackToEnqueuedAt()
    {
        // Arrange
        var (_, _, _, syncJobQueue, currentUser, sut) = CreateSut();
        currentUser.SetupGet(c => c.UserId).Returns("u-1");
        var enqueuedAt = new DateTimeOffset(2026, 5, 28, 8, 0, 0, TimeSpan.Zero);
        syncJobQueue.Setup(q => q.GetRecentFailuresAsync("u-1", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarSyncJob>
            {
                new()
                {
                    Id = Guid.NewGuid(), AttemptCount = 1, Source = SyncJobSource.Periodic,
                    Status = SyncJobStatus.Failed, CompletedAt = null, EnqueuedAt = enqueuedAt
                }
            });

        // Act
        var result = await sut.GetFailedSyncRuns(limit: 100, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<IReadOnlyList<FailedSyncRunDto>>().Subject;
        dtos[0].Source.Should().Be("Periodic");
        dtos[0].LastAttemptAt.Should().Be(enqueuedAt);
    }

    [Fact]
    public async Task GetFailedSyncRuns_WhenNoCurrentUser_ReturnsUnauthorized()
    {
        // Arrange
        var (_, _, _, _, currentUser, sut) = CreateSut();
        currentUser.SetupGet(c => c.UserId).Returns((string?)null);

        // Act
        var result = await sut.GetFailedSyncRuns(limit: 100, CancellationToken.None);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(501, 500)]
    [InlineData(1000, 500)]
    [InlineData(50, 50)]
    [InlineData(500, 500)]
    public async Task GetFailedSyncRuns_ClampsLimitToValidRange(int requestedLimit, int expectedLimit)
    {
        // Arrange
        var (_, _, _, syncJobQueue, currentUser, sut) = CreateSut();
        currentUser.SetupGet(c => c.UserId).Returns("u-clamp");
        syncJobQueue.Setup(q => q.GetRecentFailuresAsync("u-clamp", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarSyncJob>());

        // Act
        var result = await sut.GetFailedSyncRuns(limit: requestedLimit, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        syncJobQueue.Verify(q => q.GetRecentFailuresAsync("u-clamp", expectedLimit, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (
        Mock<ICalendarRepository> CalendarRepo,
        Mock<ITokenStore> TokenStore,
        Mock<ISyncFailureRepository> FailureRepo,
        Mock<ICalendarSyncJobQueue> SyncJobQueue,
        Mock<ICurrentUserService> CurrentUser,
        DiagnosticsController Sut) CreateSut()
    {
        var calendarRepoMock = new Mock<ICalendarRepository>();
        var tokenStoreMock   = new Mock<ITokenStore>();
        var failureRepoMock  = new Mock<ISyncFailureRepository>();
        var syncJobQueueMock = new Mock<ICalendarSyncJobQueue>();
        var currentUserMock  = new Mock<ICurrentUserService>();
        var loggerMock       = new Mock<ILogger<DiagnosticsController>>();

        var sut = new DiagnosticsController(
            calendarRepoMock.Object,
            tokenStoreMock.Object,
            failureRepoMock.Object,
            syncJobQueueMock.Object,
            currentUserMock.Object,
            loggerMock.Object);

        return (calendarRepoMock, tokenStoreMock, failureRepoMock, syncJobQueueMock, currentUserMock, sut);
    }
}
