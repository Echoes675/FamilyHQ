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
    [Fact]
    public async Task ExchangeCodeForTokenAsync_WhenSuccessful_ReturnsTokens()
    {
        // Arrange
        var (httpMock, systemUnderTest) = CreateSut();
        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = "access-123",
            refresh_token = "refresh-456",
            expires_in = 3600,
            token_type = "Bearer"
        });

        httpMock.Protected()
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
        var result = await systemUnderTest.ExchangeCodeForTokenAsync("auth-code-789", "https://localhost/callback");

        // Assert
        result.AccessToken.Should().Be("access-123");
        result.RefreshToken.Should().Be("refresh-456");
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenSuccessful_ReturnsNewAccessToken()
    {
        // Arrange
        var (httpMock, systemUnderTest) = CreateSut();
        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = "new-access-123",
            expires_in = 3600,
            token_type = "Bearer"
        });

        httpMock.Protected()
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
        var result = await systemUnderTest.RefreshAccessTokenAsync("old-refresh-token");

        // Assert
        result.Should().Be("new-access-123");
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenFails_ThrowsException()
    {
        // Arrange
        var (httpMock, systemUnderTest) = CreateSut();
        httpMock.Protected()
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
        await systemUnderTest.Invoking(s => s.RefreshAccessTokenAsync("bad-token"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to refresh token*");
    }

    private static (Mock<HttpMessageHandler> HttpMock, GoogleAuthService SystemUnderTest) CreateSut()
    {
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(httpMessageHandlerMock.Object);
        
        var options = Microsoft.Extensions.Options.Options.Create(new GoogleCalendarOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            AuthBaseUrl = "https://test.oauth.com"
        });

        var loggerMock = new Mock<ILogger<GoogleAuthService>>();
        
        var systemUnderTest = new GoogleAuthService(httpClient, options, loggerMock.Object);
        return (httpMessageHandlerMock, systemUnderTest);
    }
}
