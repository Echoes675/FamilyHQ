using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Tests.Fakes;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Auth;

/// <summary>
/// Uses <see cref="FakeFamilyHqDbContext"/> (FHQ-146) — no InMemory provider, no real DB. Writes do not
/// round-trip through the mock DbSet, so save-then-read specs either (a) capture the entity passed to
/// <c>Add</c> via a Moq callback and re-seed the fake with it before reading, or (b) seed the existing
/// row up front and assert the mutation the repository made in place. See
/// <see cref="FakeFamilyHqDbContext"/>'s doc comment for the underlying constraint.
/// </summary>
public class DatabaseTokenStoreTests
{
    private const string Purpose = "FamilyHQ.Tokens";

    private readonly FakeFamilyHqDbContext _db = new();
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly Mock<ILogger<DatabaseTokenStore>> _loggerMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<IConnectionStatusBroadcaster> _broadcasterMock;

    public DatabaseTokenStoreTests()
    {
        // Use EphemeralDataProtectionProvider for testing (designed for unit tests)
        _dataProtectionProvider = new EphemeralDataProtectionProvider();

        _loggerMock = new Mock<ILogger<DatabaseTokenStore>>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _broadcasterMock = new Mock<IConnectionStatusBroadcaster>();
    }

    private DatabaseTokenStore CreateSut(string userId)
    {
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);
        return new DatabaseTokenStore(
            _db,
            _currentUserServiceMock.Object,
            _dataProtectionProvider,
            _loggerMock.Object,
            _broadcasterMock.Object);
    }

    private (DatabaseTokenStore sut, Mock<IConnectionStatusBroadcaster> broadcaster) CreateSutWithBroadcaster()
    {
        // The four broadcast-behaviour tests don't rely on _currentUserService (they pass
        // the userId explicitly), so we don't need to configure it here.
        var sut = new DatabaseTokenStore(
            _db,
            _currentUserServiceMock.Object,
            _dataProtectionProvider,
            _loggerMock.Object,
            _broadcasterMock.Object);
        return (sut, _broadcasterMock);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_ThenGetRefreshTokenAsync_ReturnsSavedToken()
    {
        // Arrange
        var userId = "test-user-123";
        var refreshToken = "test-refresh-token-12345";
        var mockSet = _db.Setup<UserToken>();
        UserToken? addedToken = null;
        mockSet.Setup(s => s.Add(It.IsAny<UserToken>())).Callback<UserToken>(t => addedToken = t);
        var sut = CreateSut(userId);

        // Act — the write is interaction-based (a mock DbSet does not reflect Add on later reads).
        await sut.SaveRefreshTokenAsync(refreshToken);

        addedToken.Should().NotBeNull();
        _db.SaveChangesCount.Should().Be(1);

        // Re-seed the fake with exactly what was written, then read it back through the SUT to prove
        // GetRefreshTokenAsync finds and decrypts it correctly.
        _db.Setup<UserToken>([addedToken!]);
        var result = await sut.GetRefreshTokenAsync();

        // Assert
        Assert.Equal(refreshToken, result);
    }

    [Fact]
    public async Task GetRefreshTokenAsync_WhenNoTokenExists_ReturnsNull()
    {
        // Arrange
        var userId = "non-existent-user";
        _db.Setup<UserToken>();
        var sut = CreateSut(userId);

        // Act
        var result = await sut.GetRefreshTokenAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_StoresEncryptedToken_InDatabase()
    {
        // Arrange
        var userId = "test-user-456";
        var refreshToken = "my-secret-refresh-token";
        var mockSet = _db.Setup<UserToken>();
        UserToken? addedToken = null;
        mockSet.Setup(s => s.Add(It.IsAny<UserToken>())).Callback<UserToken>(t => addedToken = t);
        var sut = CreateSut(userId);

        // Act
        await sut.SaveRefreshTokenAsync(refreshToken);

        // Assert - the entity passed to Add has the encrypted token (not plain text)
        addedToken.Should().NotBeNull();
        Assert.Equal(userId, addedToken!.UserId);
        Assert.NotEqual(refreshToken, addedToken.RefreshToken);
        // The stored token should be different (encrypted) from the original
        Assert.NotEqual(refreshToken, addedToken.RefreshToken);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_ForMultipleUsers_EachHasOwnToken()
    {
        // Arrange
        var userId1 = "user-1";
        var userId2 = "user-2";
        var token1 = "token-for-user-1";
        var token2 = "token-for-user-2";

        // Note: Each user needs their own DatabaseTokenStore instance because
        // the SemaphoreSlim is instance-specific.
        var currentUserService1 = new Mock<ICurrentUserService>();
        currentUserService1.Setup(x => x.UserId).Returns(userId1);

        var currentUserService2 = new Mock<ICurrentUserService>();
        currentUserService2.Setup(x => x.UserId).Returns(userId2);

        // Use different EphemeralDataProtectionProvider instances to simulate
        // different encryption keys per user store (like in production)
        var dataProtectionProvider1 = new EphemeralDataProtectionProvider();
        var dataProtectionProvider2 = new EphemeralDataProtectionProvider();

        var sut1 = new DatabaseTokenStore(
            _db,
            currentUserService1.Object,
            dataProtectionProvider1,
            _loggerMock.Object,
            _broadcasterMock.Object);

        var sut2 = new DatabaseTokenStore(
            _db,
            currentUserService2.Object,
            dataProtectionProvider2,
            _loggerMock.Object,
            _broadcasterMock.Object);

        var mockSet = _db.Setup<UserToken>();
        var added = new List<UserToken>();
        mockSet.Setup(s => s.Add(It.IsAny<UserToken>())).Callback<UserToken>(t => added.Add(t));

        // Act — each user's store writes its own row into the ONE shared fake (per-user isolation is a
        // query-filter behaviour, not a separate-database behaviour — production shares one DbContext).
        await sut1.SaveRefreshTokenAsync(token1);
        await sut2.SaveRefreshTokenAsync(token2);

        added.Should().HaveCount(2);
        _db.Setup<UserToken>(added);

        // Assert - each user gets back only their own token, decrypted with their own protector.
        var result1 = await sut1.GetRefreshTokenAsync();
        var result2 = await sut2.GetRefreshTokenAsync();

        Assert.Equal(token1, result1);
        Assert.Equal(token2, result2);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_ForSameUser_UpdatesExistingToken_NotDuplicates()
    {
        // Arrange
        var userId = "test-user-update";
        var updatedToken = "updated-token";
        var protector = _dataProtectionProvider.CreateProtector(Purpose);
        var existing = new UserToken
        {
            UserId = userId,
            Provider = "Google",
            RefreshToken = protector.Protect("original-token"),
            AuthStatus = TokenAuthStatus.Active
        };
        var mockSet = _db.Setup<UserToken>([existing]);
        var sut = CreateSut(userId);

        // Act - save a new token for the same user
        await sut.SaveRefreshTokenAsync(updatedToken);

        // Assert - the existing row was updated in place, not duplicated.
        mockSet.Verify(s => s.Add(It.IsAny<UserToken>()), Times.Never);
        _db.SaveChangesCount.Should().Be(1);
        protector.Unprotect(existing.RefreshToken).Should().Be(updatedToken);
    }

    [Fact]
    public async Task GetRefreshTokenAsync_WhenUserIdIsNull_ReturnsNull()
    {
        // Arrange
        _currentUserServiceMock.Setup(x => x.UserId).Returns((string?)null);
        var sut = new DatabaseTokenStore(
            _db,
            _currentUserServiceMock.Object,
            _dataProtectionProvider,
            _loggerMock.Object,
            _broadcasterMock.Object);

        // Act
        var result = await sut.GetRefreshTokenAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_WhenUserIdIsNull_ThrowsInvalidOperationException()
    {
        // Arrange
        _currentUserServiceMock.Setup(x => x.UserId).Returns((string?)null);
        var sut = new DatabaseTokenStore(
            _db,
            _currentUserServiceMock.Object,
            _dataProtectionProvider,
            _loggerMock.Object,
            _broadcasterMock.Object);

        // Act & Assert - throws before touching the DbSet, so nothing needs seeding.
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sut.SaveRefreshTokenAsync("some-token"));
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_WithEmptyToken_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut("test-user");

        // Act & Assert - throws before touching the DbSet, so nothing needs seeding.
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await sut.SaveRefreshTokenAsync(""));
    }

    [Fact]
    public async Task GetRefreshTokenAsync_ForSpecificProvider_ReturnsCorrectToken()
    {
        // Arrange
        var userId = "test-user-provider";
        var token = "google-token";

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.Setup(x => x.UserId).Returns(userId);

        var dataProtectionProvider = new EphemeralDataProtectionProvider();

        var sut = new DatabaseTokenStore(
            _db,
            currentUserService.Object,
            dataProtectionProvider,
            _loggerMock.Object,
            _broadcasterMock.Object,
            provider: "Google");

        var mockSet = _db.Setup<UserToken>();
        UserToken? addedToken = null;
        mockSet.Setup(s => s.Add(It.IsAny<UserToken>())).Callback<UserToken>(t => addedToken = t);

        // Act
        await sut.SaveRefreshTokenAsync(token);
        addedToken.Should().NotBeNull();
        _db.Setup<UserToken>([addedToken!]);
        var result = await sut.GetRefreshTokenAsync();

        // Assert
        Assert.Equal(token, result);
    }

    [Fact]
    public async Task MarkNeedsReauthAsync_PersistsStatusDescriptionAndTimestamp()
    {
        // Arrange
        var userId = "test-user-needs-reauth";
        var existing = new UserToken
        {
            UserId = userId,
            Provider = "Google",
            RefreshToken = "irrelevant-ciphertext",
            AuthStatus = TokenAuthStatus.Active
        };
        _db.Setup<UserToken>([existing]);
        var sut = CreateSut(userId);

        // Act
        await sut.MarkNeedsReauthAsync(userId, "Token has been expired or revoked.", CancellationToken.None);

        // Assert - the repository mutates the seeded row in place.
        Assert.Equal(TokenAuthStatus.NeedsReauth, existing.AuthStatus);
        Assert.Equal("Token has been expired or revoked.", existing.LastAuthErrorDescription);
        Assert.NotNull(existing.AuthStatusChangedAt);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_AfterNeedsReauth_ResetsToActiveAndClearsError()
    {
        // Arrange - seed a row already flagged NeedsReauth (the state a prior MarkNeedsReauthAsync call
        // would have produced).
        var userId = "test-user-reset";
        var existing = new UserToken
        {
            UserId = userId,
            Provider = "Google",
            RefreshToken = "old-encrypted-value",
            AuthStatus = TokenAuthStatus.NeedsReauth,
            LastAuthErrorDescription = "previous error"
        };
        _db.Setup<UserToken>([existing]);
        var sut = CreateSut(userId);

        // Act — re-consent flow saves a fresh refresh token
        await sut.SaveRefreshTokenAsync("brand-new-token");

        // Assert
        Assert.Equal(TokenAuthStatus.Active, existing.AuthStatus);
        Assert.Null(existing.LastAuthErrorDescription);
        Assert.NotNull(existing.AuthStatusChangedAt);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_WithExplicitUserId_AfterNeedsReauth_ResetsToActive()
    {
        // Arrange
        var userId = "test-user-callback-reset";
        var existing = new UserToken
        {
            UserId = userId,
            Provider = "Google",
            RefreshToken = "old-encrypted-value",
            AuthStatus = TokenAuthStatus.NeedsReauth,
            LastAuthErrorDescription = "old error"
        };
        _db.Setup<UserToken>([existing]);
        var sut = CreateSut(userId);

        // Act — the explicit-userId overload is used by AuthController.Callback
        await sut.SaveRefreshTokenAsync("post-reconsent-token", userId);

        // Assert
        Assert.Equal(TokenAuthStatus.Active, existing.AuthStatus);
        Assert.Null(existing.LastAuthErrorDescription);
    }

    [Fact]
    public async Task GetAuthStatusAsync_WhenNoToken_ReturnsActiveWithNullError()
    {
        // Arrange
        _db.Setup<UserToken>();
        var sut = CreateSut("any-user");

        // Act
        var result = await sut.GetAuthStatusAsync("unknown-user", CancellationToken.None);

        // Assert
        Assert.Equal(TokenAuthStatus.Active, result.Status);
        Assert.Null(result.LastError);
        Assert.Null(result.Since);
    }

    [Fact]
    public async Task GetAuthStatusAsync_AfterMarkNeedsReauth_ReturnsNeedsReauthWithErrorAndTimestamp()
    {
        // Arrange
        var userId = "test-user-get-status";
        var existing = new UserToken
        {
            UserId = userId,
            Provider = "Google",
            RefreshToken = "irrelevant-ciphertext",
            AuthStatus = TokenAuthStatus.Active
        };
        _db.Setup<UserToken>([existing]);
        var sut = CreateSut(userId);
        await sut.MarkNeedsReauthAsync(userId, "invalid_grant occurred", CancellationToken.None);

        // Act
        var result = await sut.GetAuthStatusAsync(userId, CancellationToken.None);

        // Assert
        Assert.Equal(TokenAuthStatus.NeedsReauth, result.Status);
        Assert.Equal("invalid_grant occurred", result.LastError);
        Assert.NotNull(result.Since);
    }

    [Fact]
    public async Task MarkNeedsReauthAsync_WhenTokenUpdates_BroadcastsConnectionStatusUpdated()
    {
        var (sut, broadcasterMock) = CreateSutWithBroadcaster();
        // Seed an existing token for the user.
        _db.Setup<UserToken>([new UserToken
        {
            UserId = "u-broadcast",
            Provider = "Google",
            RefreshToken = "ignored",
            AuthStatus = TokenAuthStatus.Active
        }]);

        await sut.MarkNeedsReauthAsync("u-broadcast", "Forbidden", CancellationToken.None);

        broadcasterMock.Verify(
            b => b.BroadcastConnectionStatusUpdatedAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MarkNeedsReauthAsync_WhenNoTokenRow_DoesNotBroadcast()
    {
        var (sut, broadcasterMock) = CreateSutWithBroadcaster();
        _db.Setup<UserToken>();

        await sut.MarkNeedsReauthAsync("u-no-token", "Forbidden", CancellationToken.None);

        broadcasterMock.Verify(
            b => b.BroadcastConnectionStatusUpdatedAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_WhenAuthStatusFlipsToActive_BroadcastsConnectionStatusUpdated()
    {
        var (sut, broadcasterMock) = CreateSutWithBroadcaster();
        _db.Setup<UserToken>([new UserToken
        {
            UserId = "u-flip",
            Provider = "Google",
            RefreshToken = "old-encrypted-value",
            AuthStatus = TokenAuthStatus.NeedsReauth,
            LastAuthErrorDescription = "Forbidden"
        }]);

        await sut.SaveRefreshTokenAsync("new-refresh-token", "u-flip", CancellationToken.None);

        broadcasterMock.Verify(
            b => b.BroadcastConnectionStatusUpdatedAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_WhenAuthStatusAlreadyActive_DoesNotBroadcast()
    {
        var (sut, broadcasterMock) = CreateSutWithBroadcaster();
        _db.Setup<UserToken>([new UserToken
        {
            UserId = "u-noop",
            Provider = "Google",
            RefreshToken = "old-encrypted-value",
            AuthStatus = TokenAuthStatus.Active
        }]);

        await sut.SaveRefreshTokenAsync("new-refresh-token", "u-noop", CancellationToken.None);

        broadcasterMock.Verify(
            b => b.BroadcastConnectionStatusUpdatedAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAllUserAuthStatesAsync_ReturnsEachUserWithTheirAuthStatus()
    {
        // Arrange — one healthy user, one flagged for re-auth.
        _db.Setup<UserToken>([
            new UserToken
            {
                UserId = "u-active",
                Provider = "Google",
                RefreshToken = "enc-active",
                AuthStatus = TokenAuthStatus.Active
            },
            new UserToken
            {
                UserId = "u-stale",
                Provider = "Google",
                RefreshToken = "enc-stale",
                AuthStatus = TokenAuthStatus.NeedsReauth
            }
        ]);
        var sut = CreateSut("seed-user");

        // Act
        var states = await sut.GetAllUserAuthStatesAsync(CancellationToken.None);

        // Assert
        states.Should().HaveCount(2);
        states.Should().ContainSingle(s => s.UserId == "u-active" && s.AuthStatus == TokenAuthStatus.Active);
        states.Should().ContainSingle(s => s.UserId == "u-stale" && s.AuthStatus == TokenAuthStatus.NeedsReauth);
    }
}
