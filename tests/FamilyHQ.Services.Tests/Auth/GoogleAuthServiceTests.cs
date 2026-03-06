using System.Net;
using System.Text.Json;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.Extensions.Options;
using Moq.Protected;
using Xunit;

namespace FamilyHQ.Services.Tests.Auth;

public class GoogleAuthServiceTests
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly GoogleAuthService _sut;

    public GoogleAuthServiceTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        
        var options = Microsoft.Extensions.Options.Options.Create(new GoogleCalendarOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            AuthBaseUrl = "https://test.oauth.com"
        });

        var loggerMock = new Mock<ILogger<GoogleAuthService>>();
        
        _sut = new GoogleAuthService(httpClient, options, loggerMock.Object);
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_WhenSuccessful_ReturnsTokens()
    {
        // Arrange
        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = "access-123",
            refresh_token = "refresh-456",
            expires_in = 3600,
            token_type = "Bearer"
        });

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson)
            });

        // Act
        var result = await _sut.ExchangeCodeForTokenAsync("auth-code-789", "https://localhost/callback");

        // Assert
        result.AccessToken.Should().Be("access-123");
        result.RefreshToken.Should().Be("refresh-456");
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenSuccessful_ReturnsNewAccessToken()
    {
        // Arrange
        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = "new-access-123",
            expires_in = 3600,
            token_type = "Bearer"
        });

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson)
            });

        // Act
        var result = await _sut.RefreshAccessTokenAsync("old-refresh-token");

        // Assert
        result.Should().Be("new-access-123");
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenFails_ThrowsException()
    {
        // Arrange
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent("invalid_grant")
            });

        // Act & Assert
        await _sut.Invoking(s => s.RefreshAccessTokenAsync("bad-token"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to refresh token*");
    }
}
