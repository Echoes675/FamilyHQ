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
        var (calendarRepo, tokenStore, _, currentUser, sut) = CreateSut();
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
        var payload = ok.Value!.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(ok.Value));
        payload["status"].Should().Be("active");
        payload["lastError"].Should().BeNull();
        payload["since"].Should().BeNull();

        var calendars = payload["calendars"]
            .Should().BeAssignableTo<System.Collections.IEnumerable>().Subject
            .Cast<object>()
            .Select(o => o.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(o)))
            .ToList();
        calendars.Should().HaveCount(2);
        calendars[0]["calendarId"].Should().Be(CalAId);
        calendars[0]["displayName"].Should().Be("A");
        calendars[0]["lastSyncedAt"].Should().Be(lastSyncedA);
        calendars[1]["calendarId"].Should().Be(CalBId);
        calendars[1]["lastSyncedAt"].Should().BeNull();
    }

    [Fact]
    public async Task GetConnectionStatus_WhenTokenNeedsReauth_ReturnsReauthStatusWithDetails()
    {
        // Arrange
        var (calendarRepo, tokenStore, _, currentUser, sut) = CreateSut();
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
        var payload = ok.Value!.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(ok.Value));
        payload["status"].Should().Be("needs_reauth");
        payload["lastError"].Should().Be("expired");
        payload["since"].Should().Be(since);
    }

    [Fact]
    public async Task GetConnectionStatus_WhenNoCurrentUser_ReturnsUnauthorized()
    {
        // Arrange
        var (_, _, _, currentUser, sut) = CreateSut();
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
        var (calendarRepo, tokenStore, _, currentUser, sut) = CreateSut();
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
        var (_, _, failureRepo, currentUser, sut) = CreateSut();
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
        var (_, _, _, currentUser, sut) = CreateSut();
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
        var (_, _, failureRepo, currentUser, sut) = CreateSut();
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
        var (_, _, failureRepo, currentUser, sut) = CreateSut();
        currentUser.SetupGet(c => c.UserId).Returns("u-clamp");
        failureRepo.Setup(r => r.GetRecentAsync("u-clamp", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncEventFailure>());

        // Act
        var result = await sut.GetSyncFailures(limit: requestedLimit, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        failureRepo.Verify(r => r.GetRecentAsync("u-clamp", expectedLimit, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (
        Mock<ICalendarRepository> CalendarRepo,
        Mock<ITokenStore> TokenStore,
        Mock<ISyncFailureRepository> FailureRepo,
        Mock<ICurrentUserService> CurrentUser,
        DiagnosticsController Sut) CreateSut()
    {
        var calendarRepoMock = new Mock<ICalendarRepository>();
        var tokenStoreMock   = new Mock<ITokenStore>();
        var failureRepoMock  = new Mock<ISyncFailureRepository>();
        var currentUserMock  = new Mock<ICurrentUserService>();
        var loggerMock       = new Mock<ILogger<DiagnosticsController>>();

        var sut = new DiagnosticsController(
            calendarRepoMock.Object,
            tokenStoreMock.Object,
            failureRepoMock.Object,
            currentUserMock.Object,
            loggerMock.Object);

        return (calendarRepoMock, tokenStoreMock, failureRepoMock, currentUserMock, sut);
    }
}
