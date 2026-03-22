using FamilyHQ.WebUi.Services.Auth;
using FamilyHQ.WebUi.Services.Correlation;
using FluentAssertions;
using Moq;

namespace FamilyHQ.WebUi.Tests;

public class AuthenticationServiceTests
{
    private readonly Mock<IAuthTokenStore> _tokenStoreMock;
    private readonly Mock<ICorrelationIdTokenStore> _correlationStoreMock;
    private readonly AuthenticationService _systemUnderTest;

    public AuthenticationServiceTests()
    {
        _tokenStoreMock = new Mock<IAuthTokenStore>();
        _correlationStoreMock = new Mock<ICorrelationIdTokenStore>();
        _systemUnderTest = new AuthenticationService(_tokenStoreMock.Object, _correlationStoreMock.Object);
    }

    #region IsAuthenticatedAsync

    [Fact]
    public async Task IsAuthenticatedAsync_WhenNoToken_ReturnsFalse()
    {
        // Arrange
        _tokenStoreMock.Setup(s => s.GetTokenAsync())
            .ReturnsAsync((string?)null);

        // Act
        var result = await _systemUnderTest.IsAuthenticatedAsync();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsAuthenticatedAsync_WhenValidToken_ReturnsTrue()
    {
        // Arrange
        var token = CreateValidJwtToken("user-123", "testuser");
        _tokenStoreMock.Setup(s => s.GetTokenAsync())
            .ReturnsAsync(token);

        // Act
        var result = await _systemUnderTest.IsAuthenticatedAsync();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAuthenticatedAsync_WhenInvalidToken_ReturnsFalse()
    {
        // Arrange
        _tokenStoreMock.Setup(s => s.GetTokenAsync())
            .ReturnsAsync("invalid-token");

        // Act
        var result = await _systemUnderTest.IsAuthenticatedAsync();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetUserIdAsync

    [Fact]
    public async Task GetUserIdAsync_WhenValidToken_ReturnsUserId()
    {
        // Arrange
        var userId = "user-123";
        var token = CreateValidJwtToken(userId, "testuser");
        _tokenStoreMock.Setup(s => s.GetTokenAsync())
            .ReturnsAsync(token);

        // Act
        var result = await _systemUnderTest.GetUserIdAsync();

        // Assert
        result.Should().Be(userId);
    }

    [Fact]
    public async Task GetUserIdAsync_WhenNoToken_ReturnsNull()
    {
        // Arrange
        _tokenStoreMock.Setup(s => s.GetTokenAsync())
            .ReturnsAsync((string?)null);

        // Act
        var result = await _systemUnderTest.GetUserIdAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetUserIdAsync_WhenTokenWithoutSubClaim_ReturnsNull()
    {
        // Arrange
        var token = CreateJwtTokenWithoutSub();
        _tokenStoreMock.Setup(s => s.GetTokenAsync())
            .ReturnsAsync(token);

        // Act
        var result = await _systemUnderTest.GetUserIdAsync();

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetUsernameAsync

    [Fact]
    public async Task GetUsernameAsync_WhenValidTokenWithName_ReturnsUsername()
    {
        // Arrange
        var username = "testuser";
        var token = CreateValidJwtToken("user-123", username);
        _tokenStoreMock.Setup(s => s.GetTokenAsync())
            .ReturnsAsync(token);

        // Act
        var result = await _systemUnderTest.GetUsernameAsync();

        // Assert
        result.Should().Be(username);
    }

    [Fact]
    public async Task GetUsernameAsync_WhenValidTokenWithOnlyUserId_ReturnsNull()
    {
        // Arrange
        var token = CreateJwtTokenWithUserIdOnly("user-123");
        _tokenStoreMock.Setup(s => s.GetTokenAsync())
            .ReturnsAsync(token);

        // Act
        var result = await _systemUnderTest.GetUsernameAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetUsernameAsync_WhenNoToken_ReturnsNull()
    {
        // Arrange
        _tokenStoreMock.Setup(s => s.GetTokenAsync())
            .ReturnsAsync((string?)null);

        // Act
        var result = await _systemUnderTest.GetUsernameAsync();

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region SignOutAsync

    [Fact]
    public async Task SignOutAsync_ClearsTokenAndResetsState()
    {
        // Arrange - First authenticate to set up state
        var token = CreateValidJwtToken("user-123", "testuser");
        _tokenStoreMock.Setup(s => s.GetTokenAsync())
            .ReturnsAsync(token);
        await _systemUnderTest.IsAuthenticatedAsync(); // Initialize

        // Act
        await _systemUnderTest.SignOutAsync();

        // Assert
        _tokenStoreMock.Verify(s => s.ClearTokenAsync(), Times.Once);
        _correlationStoreMock.Verify(s => s.ClearSessionCorrelationIdAsync(), Times.Once);
        
        var isAuthenticated = await _systemUnderTest.IsAuthenticatedAsync();
        isAuthenticated.Should().BeFalse();

        var userId = await _systemUnderTest.GetUserIdAsync();
        userId.Should().BeNull();

        var username = await _systemUnderTest.GetUsernameAsync();
        username.Should().BeNull();
    }

    [Fact]
    public async Task SignOutAsync_WhenNotAuthenticated_CallsClearToken()
    {
        // Arrange
        _tokenStoreMock.Setup(s => s.GetTokenAsync())
            .ReturnsAsync((string?)null);

        // Act
        await _systemUnderTest.SignOutAsync();

        // Assert
        _tokenStoreMock.Verify(s => s.ClearTokenAsync(), Times.Once);
        _correlationStoreMock.Verify(s => s.ClearSessionCorrelationIdAsync(), Times.Once);
    }

    #endregion

    #region Token Cache Behavior

    [Fact]
    public async Task IsAuthenticatedAsync_CachesResultOnFirstCall()
    {
        // Arrange
        var token = CreateValidJwtToken("user-123", "testuser");
        _tokenStoreMock.Setup(s => s.GetTokenAsync())
            .ReturnsAsync(token)
            .Verifiable();

        // Act - First call should fetch token
        await _systemUnderTest.IsAuthenticatedAsync();
        // Second call should use cached result
        await _systemUnderTest.IsAuthenticatedAsync();

        // Assert - Token store should only be called once due to caching
        _tokenStoreMock.Verify(s => s.GetTokenAsync(), Times.Once);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a valid JWT token with user ID and name claims.
    /// </summary>
    private static string CreateValidJwtToken(string userId, string username)
    {
        // JWT format: header.payload.signature
        // We'll create a simple base64-encoded JSON payload
        var header = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        
        var payload = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(
                $"{{\"sub\":\"{userId}\",\"name\":\"{username}\",\"exp\":9999999999}}"));
        
        // Use a mock signature
        var signature = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("mock-signature"));

        return $"{header}.{payload}.{signature}";
    }

    /// <summary>
    /// Creates a JWT token with only the "sub" claim (no username).
    /// </summary>
    private static string CreateJwtTokenWithUserIdOnly(string userId)
    {
        var header = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        
        var payload = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(
                $"{{\"sub\":\"{userId}\",\"exp\":9999999999}}"));
        
        var signature = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("mock-signature"));

        return $"{header}.{payload}.{signature}";
    }

    /// <summary>
    /// Creates a JWT token without the "sub" claim.
    /// </summary>
    private static string CreateJwtTokenWithoutSub()
    {
        var header = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        
        var payload = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(
                "{\"name\":\"testuser\",\"exp\":9999999999}"));
        
        var signature = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("mock-signature"));

        return $"{header}.{payload}.{signature}";
    }

    #endregion
}
