using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using FamilyHQ.Services.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Auth;

public class DatabaseTokenStoreTests : IDisposable
{
    private readonly FamilyHqDbContext _dbContext;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly Mock<ILogger<DatabaseTokenStore>> _loggerMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<IConnectionStatusBroadcaster> _broadcasterMock;

    public DatabaseTokenStoreTests()
    {
        // Create in-memory database
        var options = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FamilyHqDbContext(options);

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
            _dbContext,
            _currentUserServiceMock.Object,
            _dataProtectionProvider,
            _loggerMock.Object,
            _broadcasterMock.Object);
    }

    private (DatabaseTokenStore sut, Mock<IConnectionStatusBroadcaster> broadcaster, FamilyHqDbContext db) CreateSutWithBroadcaster()
    {
        // The four broadcast-behaviour tests don't rely on _currentUserService (they pass
        // the userId explicitly), so we don't need to configure it here.
        var sut = new DatabaseTokenStore(
            _dbContext,
            _currentUserServiceMock.Object,
            _dataProtectionProvider,
            _loggerMock.Object,
            _broadcasterMock.Object);
        return (sut, _broadcasterMock, _dbContext);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_ThenGetRefreshTokenAsync_ReturnsSavedToken()
    {
        // Arrange
        var userId = "test-user-123";
        var refreshToken = "test-refresh-token-12345";
        var sut = CreateSut(userId);

        // Act
        await sut.SaveRefreshTokenAsync(refreshToken);
        var result = await sut.GetRefreshTokenAsync();

        // Assert
        Assert.Equal(refreshToken, result);
    }

    [Fact]
    public async Task GetRefreshTokenAsync_WhenNoTokenExists_ReturnsNull()
    {
        // Arrange
        var userId = "non-existent-user";
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
        var sut = CreateSut(userId);

        // Act
        await sut.SaveRefreshTokenAsync(refreshToken);

        // Assert - Check the database has the encrypted token (not plain text)
        var storedToken = await _dbContext.UserTokens
            .FirstOrDefaultAsync(t => t.UserId == userId);
        
        Assert.NotNull(storedToken);
        Assert.NotEqual(refreshToken, storedToken.RefreshToken);
        // The stored token should be different (encrypted) from the original
        Assert.NotEqual(refreshToken, storedToken.RefreshToken);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_ForMultipleUsers_EachHasOwnToken()
    {
        // Arrange
        var userId1 = "user-1";
        var userId2 = "user-2";
        var token1 = "token-for-user-1";
        var token2 = "token-for-user-2";
        
        // Use same data protection provider for both instances
        var options1 = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var dbContext1 = new FamilyHqDbContext(options1);
        
        var options2 = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var dbContext2 = new FamilyHqDbContext(options2);
        
        // Note: Each user needs their own DatabaseTokenStore instance because
        // the SemaphoreSlim is instance-specific and we need separate contexts
        var currentUserService1 = new Mock<ICurrentUserService>();
        currentUserService1.Setup(x => x.UserId).Returns(userId1);
        
        var currentUserService2 = new Mock<ICurrentUserService>();
        currentUserService2.Setup(x => x.UserId).Returns(userId2);
        
        // Use different EphemeralDataProtectionProvider instances to simulate 
        // different encryption keys per user store (like in production)
        var dataProtectionProvider1 = new EphemeralDataProtectionProvider();
        var dataProtectionProvider2 = new EphemeralDataProtectionProvider();
        
        var sut1 = new DatabaseTokenStore(
            dbContext1,
            currentUserService1.Object,
            dataProtectionProvider1,
            _loggerMock.Object,
            _broadcasterMock.Object);

        var sut2 = new DatabaseTokenStore(
            dbContext2,
            currentUserService2.Object,
            dataProtectionProvider2,
            _loggerMock.Object,
            _broadcasterMock.Object);

        // Act
        await sut1.SaveRefreshTokenAsync(token1);
        await sut2.SaveRefreshTokenAsync(token2);

        // Assert - Each user has their own token in their own database
        var storedToken1 = await dbContext1.UserTokens.FirstOrDefaultAsync(t => t.UserId == userId1);
        var storedToken2 = await dbContext2.UserTokens.FirstOrDefaultAsync(t => t.UserId == userId2);
        
        Assert.NotNull(storedToken1);
        Assert.NotNull(storedToken2);
        
        // Verify that when retrieved, each user gets their own token
        var result1 = await sut1.GetRefreshTokenAsync();
        var result2 = await sut2.GetRefreshTokenAsync();
        
        Assert.Equal(token1, result1);
        Assert.Equal(token2, result2);
        
        dbContext1.Dispose();
        dbContext2.Dispose();
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_ForSameUser_UpdatesExistingToken_NotDuplicates()
    {
        // Arrange
        var userId = "test-user-update";
        var originalToken = "original-token";
        var updatedToken = "updated-token";
        var sut = CreateSut(userId);

        // Act - Save original token
        await sut.SaveRefreshTokenAsync(originalToken);
        
        // Save updated token
        await sut.SaveRefreshTokenAsync(updatedToken);

        // Assert - Only one token exists for this user
        var tokenCount = await _dbContext.UserTokens.CountAsync(t => t.UserId == userId);
        Assert.Equal(1, tokenCount);

        // The token should be the updated one
        var result = await sut.GetRefreshTokenAsync();
        Assert.Equal(updatedToken, result);
    }

    [Fact]
    public async Task GetRefreshTokenAsync_WhenUserIdIsNull_ReturnsNull()
    {
        // Arrange
        _currentUserServiceMock.Setup(x => x.UserId).Returns((string?)null);
        var sut = new DatabaseTokenStore(
            _dbContext,
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
            _dbContext,
            _currentUserServiceMock.Object,
            _dataProtectionProvider,
            _loggerMock.Object,
            _broadcasterMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sut.SaveRefreshTokenAsync("some-token"));
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_WithEmptyToken_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut("test-user");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await sut.SaveRefreshTokenAsync(""));
    }

    [Fact]
    public async Task GetRefreshTokenAsync_ForSpecificProvider_ReturnsCorrectToken()
    {
        // Arrange
        var userId = "test-user-provider";
        var token = "google-token";
        
        var options = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var dbContext = new FamilyHqDbContext(options);
        
        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.Setup(x => x.UserId).Returns(userId);
        
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        
        var sut = new DatabaseTokenStore(
            dbContext,
            currentUserService.Object,
            dataProtectionProvider,
            _loggerMock.Object,
            _broadcasterMock.Object,
            provider: "Google");

        // Act
        await sut.SaveRefreshTokenAsync(token);
        var result = await sut.GetRefreshTokenAsync();

        // Assert
        Assert.Equal(token, result);
        
        dbContext.Dispose();
    }

    [Fact]
    public async Task MarkNeedsReauthAsync_PersistsStatusDescriptionAndTimestamp()
    {
        // Arrange
        var userId = "test-user-needs-reauth";
        var sut = CreateSut(userId);
        await sut.SaveRefreshTokenAsync("initial-token");

        // Act
        await sut.MarkNeedsReauthAsync(userId, "Token has been expired or revoked.", CancellationToken.None);

        // Assert
        var stored = await _dbContext.UserTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId);
        Assert.NotNull(stored);
        Assert.Equal(TokenAuthStatus.NeedsReauth, stored.AuthStatus);
        Assert.Equal("Token has been expired or revoked.", stored.LastAuthErrorDescription);
        Assert.NotNull(stored.AuthStatusChangedAt);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_AfterNeedsReauth_ResetsToActiveAndClearsError()
    {
        // Arrange
        var userId = "test-user-reset";
        var sut = CreateSut(userId);
        await sut.SaveRefreshTokenAsync("first-token");
        await sut.MarkNeedsReauthAsync(userId, "previous error", CancellationToken.None);

        // Act — re-consent flow saves a fresh refresh token
        await sut.SaveRefreshTokenAsync("brand-new-token");

        // Assert
        var stored = await _dbContext.UserTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId);
        Assert.NotNull(stored);
        Assert.Equal(TokenAuthStatus.Active, stored.AuthStatus);
        Assert.Null(stored.LastAuthErrorDescription);
        Assert.NotNull(stored.AuthStatusChangedAt);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_WithExplicitUserId_AfterNeedsReauth_ResetsToActive()
    {
        // Arrange
        var userId = "test-user-callback-reset";
        var sut = CreateSut(userId);
        await sut.SaveRefreshTokenAsync("first-token");
        await sut.MarkNeedsReauthAsync(userId, "old error", CancellationToken.None);

        // Act — the explicit-userId overload is used by AuthController.Callback
        await sut.SaveRefreshTokenAsync("post-reconsent-token", userId);

        // Assert
        var stored = await _dbContext.UserTokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId);
        Assert.NotNull(stored);
        Assert.Equal(TokenAuthStatus.Active, stored.AuthStatus);
        Assert.Null(stored.LastAuthErrorDescription);
    }

    [Fact]
    public async Task GetAuthStatusAsync_WhenNoToken_ReturnsActiveWithNullError()
    {
        // Arrange
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
        var sut = CreateSut(userId);
        await sut.SaveRefreshTokenAsync("a-token");
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
        var (sut, broadcasterMock, dbContext) = CreateSutWithBroadcaster();
        // Seed an existing token for the user.
        dbContext.UserTokens.Add(new UserToken
        {
            UserId = "u-broadcast",
            Provider = "Google",
            RefreshToken = "ignored",
            AuthStatus = TokenAuthStatus.Active
        });
        await dbContext.SaveChangesAsync();

        await sut.MarkNeedsReauthAsync("u-broadcast", "Forbidden", CancellationToken.None);

        broadcasterMock.Verify(
            b => b.BroadcastConnectionStatusUpdatedAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MarkNeedsReauthAsync_WhenNoTokenRow_DoesNotBroadcast()
    {
        var (sut, broadcasterMock, _) = CreateSutWithBroadcaster();

        await sut.MarkNeedsReauthAsync("u-no-token", "Forbidden", CancellationToken.None);

        broadcasterMock.Verify(
            b => b.BroadcastConnectionStatusUpdatedAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_WhenAuthStatusFlipsToActive_BroadcastsConnectionStatusUpdated()
    {
        var (sut, broadcasterMock, dbContext) = CreateSutWithBroadcaster();
        dbContext.UserTokens.Add(new UserToken
        {
            UserId = "u-flip",
            Provider = "Google",
            RefreshToken = "old-encrypted-value",
            AuthStatus = TokenAuthStatus.NeedsReauth,
            LastAuthErrorDescription = "Forbidden"
        });
        await dbContext.SaveChangesAsync();

        await sut.SaveRefreshTokenAsync("new-refresh-token", "u-flip", CancellationToken.None);

        broadcasterMock.Verify(
            b => b.BroadcastConnectionStatusUpdatedAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_WhenAuthStatusAlreadyActive_DoesNotBroadcast()
    {
        var (sut, broadcasterMock, dbContext) = CreateSutWithBroadcaster();
        dbContext.UserTokens.Add(new UserToken
        {
            UserId = "u-noop",
            Provider = "Google",
            RefreshToken = "old-encrypted-value",
            AuthStatus = TokenAuthStatus.Active
        });
        await dbContext.SaveChangesAsync();

        await sut.SaveRefreshTokenAsync("new-refresh-token", "u-noop", CancellationToken.None);

        broadcasterMock.Verify(
            b => b.BroadcastConnectionStatusUpdatedAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
