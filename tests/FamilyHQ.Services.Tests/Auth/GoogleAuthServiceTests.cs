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
    public async Task ExchangeCodeForTokenAsync_WhenResponseContainsIdToken_ExtractsUserIdFromSub()
    {
        // Arrange
        var (httpMock, systemUnderTest) = CreateSut();
        var idToken = CreateTestIdToken("google-user-123", "user@example.com");
        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = "access-123",
            refresh_token = "refresh-456",
            expires_in = 3600,
            token_type = "Bearer",
            id_token = idToken
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
        result.UserId.Should().Be("google-user-123");
        result.Email.Should().Be("user@example.com");
    }

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
            token_type = "Bearer",
            id_token = CreateTestIdToken("some-user")
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
    public async Task RefreshAccessTokenAsync_WhenInvalidGrant_ThrowsGoogleReauthRequiredException()
    {
        // Arrange
        var (httpMock, systemUnderTest) = CreateSut();
        var body = JsonSerializer.Serialize(new
        {
            error = "invalid_grant",
            error_description = "Token has been expired or revoked."
        });
        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(body)
            });

        // Act & Assert
        var ex = await systemUnderTest.Invoking(s => s.RefreshAccessTokenAsync("rt"))
            .Should().ThrowAsync<GoogleReauthRequiredException>();
        ex.Which.FailureSource.Should().Be(GoogleAuthFailureSource.TokenRefresh);
        ex.Which.ErrorDescription.Should().Be("Token has been expired or revoked.");
    }

    [Theory]
    [InlineData("unauthorized_client")]
    [InlineData("invalid_token")]
    public async Task RefreshAccessTokenAsync_WhenReauthErrorCode_ThrowsGoogleReauthRequiredException(string error)
    {
        // Arrange
        var (httpMock, systemUnderTest) = CreateSut();
        var body = JsonSerializer.Serialize(new { error, error_description = "needs reconsent" });
        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(body)
            });

        // Act & Assert
        await systemUnderTest.Invoking(s => s.RefreshAccessTokenAsync("rt"))
            .Should().ThrowAsync<GoogleReauthRequiredException>();
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenNonReauth4xx_ThrowsInvalidOperationWithParsedError()
    {
        // Arrange
        var (httpMock, systemUnderTest) = CreateSut();
        var body = JsonSerializer.Serialize(new
        {
            error = "rate_limit_exceeded",
            error_description = "Too many requests"
        });
        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(body)
            });

        // Act & Assert
        await systemUnderTest.Invoking(s => s.RefreshAccessTokenAsync("rt"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*rate_limit_exceeded*");
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenFails_DoesNotLogRefreshTokenValue()
    {
        // Arrange — verifying the refresh token secret never reaches the log scope, per security skill.
        var (httpMock, systemUnderTest, loggerMock) = CreateSutWithLogger();
        var body = JsonSerializer.Serialize(new { error = "invalid_grant", error_description = "revoked" });
        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(body)
            });
        const string secretToken = "super-secret-refresh-token-VALUE";

        // Act
        try { await systemUnderTest.RefreshAccessTokenAsync(secretToken); } catch { /* expected */ }

        // Assert
        loggerMock.Invocations
            .SelectMany(i => i.Arguments)
            .Select(a => a?.ToString() ?? string.Empty)
            .Should().NotContain(s => s.Contains(secretToken));
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_WhenFails_DoesNotLogRawBodyButLogsParsedError()
    {
        // Arrange — the raw OAuth body must never reach the log; only the parsed error code may, per security/logging skills.
        var (httpMock, systemUnderTest, loggerMock) = CreateSutWithLogger();
        var body = JsonSerializer.Serialize(new
        {
            error = "invalid_grant",
            error_description = "Bad Request",
            access_token = "SENSITIVE-LEAK"
        });
        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(body)
            });

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => systemUnderTest.ExchangeCodeForTokenAsync("the-code", "https://app/callback"));

        // Assert — no log entry leaks the sensitive token value from the raw body.
        loggerMock.Verify(l => l.Log(
            It.IsAny<LogLevel>(), It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("SENSITIVE-LEAK")),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);

        // Assert — the parsed OAuth error code is logged at Error level.
        loggerMock.Verify(l => l.Log(
            LogLevel.Error, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("invalid_grant")),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public void GetAuthorizationUrl_WithAllParameters_ReturnsUrlWithRequiredQueryParams()
    {
        // Arrange
        var (_, systemUnderTest) = CreateSut();
        var redirectUri = "https://myapp.com/api/auth/callback";

        // Act
        var result = systemUnderTest.GetAuthorizationUrl(redirectUri);

        // Assert
        var uri = new Uri(result);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        query["client_id"].Should().Be("test-client-id");
        query["redirect_uri"].Should().Be(redirectUri);
        query["response_type"].Should().Be("code");
        query["scope"].Should().Be("openid email https://www.googleapis.com/auth/calendar");
        query["access_type"].Should().Be("offline");
        query["prompt"].Should().Be("consent");
    }

    [Fact]
    public void GetAuthorizationUrl_UsesAuthPromptUrlAsBase()
    {
        // Arrange
        var (_, systemUnderTest) = CreateSut();

        // Act
        var result = systemUnderTest.GetAuthorizationUrl("https://myapp.com/callback");

        // Assert
        result.Should().StartWith("https://accounts.test.com/o/oauth2/auth");
    }

    private static string CreateTestIdToken(string sub, string? email = null)
    {
        var header = Base64UrlEncode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = email is null
            ? Base64UrlEncode($"{{\"sub\":\"{sub}\"}}")
            : Base64UrlEncode($"{{\"sub\":\"{sub}\",\"email\":\"{email}\"}}");
        return $"{header}.{payload}.";
    }

    private static string Base64UrlEncode(string input)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(input))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static (Mock<HttpMessageHandler> HttpMock, GoogleAuthService SystemUnderTest) CreateSut()
    {
        var (httpMock, sut, _) = CreateSutWithLogger();
        return (httpMock, sut);
    }

    private static (Mock<HttpMessageHandler> HttpMock, GoogleAuthService SystemUnderTest, Mock<ILogger<GoogleAuthService>> LoggerMock) CreateSutWithLogger()
    {
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(httpMessageHandlerMock.Object);

        var options = Microsoft.Extensions.Options.Options.Create(new GoogleCalendarOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            AuthPromptUrl = "https://accounts.test.com/o/oauth2/auth",
            AuthBaseUrl = "https://test.oauth.com"
        });

        var loggerMock = new Mock<ILogger<GoogleAuthService>>();

        var systemUnderTest = new GoogleAuthService(httpClient, options, loggerMock.Object);
        return (httpMessageHandlerMock, systemUnderTest, loggerMock);
    }
}
