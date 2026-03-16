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
    }

    private DatabaseTokenStore CreateSut(string userId)
    {
        _currentUserServiceMock.Setup(x => x.UserId).Returns(userId);
        return new DatabaseTokenStore(
            _dbContext,
            _currentUserServiceMock.Object,
            _dataProtectionProvider,
            _loggerMock.Object);
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
            _loggerMock.Object);
            
        var sut2 = new DatabaseTokenStore(
            dbContext2,
            currentUserService2.Object,
            dataProtectionProvider2,
            _loggerMock.Object);

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
            _loggerMock.Object);

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
            _loggerMock.Object);

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
            provider: "Google");

        // Act
        await sut.SaveRefreshTokenAsync(token);
        var result = await sut.GetRefreshTokenAsync();

        // Assert
        Assert.Equal(token, result);
        
        dbContext.Dispose();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
