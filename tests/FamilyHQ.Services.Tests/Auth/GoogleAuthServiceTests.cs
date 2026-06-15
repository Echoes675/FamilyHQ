using System.Net;
using System.Text.Json;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MsOptions = Microsoft.Extensions.Options;
using Moq.Protected;
using Xunit;

namespace FamilyHQ.Services.Tests.Auth;

public class GoogleAuthServiceTests
{
    [Fact]
    public async Task ExchangeCodeForTokenAsync_WhenResponseContainsIdToken_ExtractsUserIdFromSub()
    {
        var (httpMock, systemUnderTest, _, validatorMock) = CreateSutFull();
        validatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdTokenClaims("google-user-123", "user@example.com"));

        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = "access-123",
            refresh_token = "refresh-456",
            expires_in = 3600,
            token_type = "Bearer",
            id_token = "any-token"
        });
        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson)
            });

        var result = await systemUnderTest.ExchangeCodeForTokenAsync("auth-code-789", "https://localhost/callback");

        result.UserId.Should().Be("google-user-123");
        result.Email.Should().Be("user@example.com");
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_WhenSuccessful_ReturnsTokens()
    {
        var (httpMock, systemUnderTest) = CreateSut();
        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = "access-123",
            refresh_token = "refresh-456",
            expires_in = 3600,
            token_type = "Bearer",
            id_token = "any-token"
        });
        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson)
            });

        var result = await systemUnderTest.ExchangeCodeForTokenAsync("auth-code-789", "https://localhost/callback");

        result.AccessToken.Should().Be("access-123");
        result.RefreshToken.Should().Be("refresh-456");
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_WhenIdTokenValidationFails_ThrowsIdTokenValidationException()
    {
        var (httpMock, systemUnderTest, _, validatorMock) = CreateSutFull();
        validatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IdTokenValidationException("Signature validation failed."));

        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = "access-123",
            refresh_token = "refresh-456",
            expires_in = 3600,
            token_type = "Bearer",
            id_token = "bad-token"
        });
        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson)
            });

        await systemUnderTest.Invoking(s => s.ExchangeCodeForTokenAsync("auth-code", "https://localhost/callback"))
            .Should().ThrowAsync<IdTokenValidationException>()
            .WithMessage("Signature validation failed.");
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenSuccessful_ReturnsNewAccessToken()
    {
        var (httpMock, systemUnderTest) = CreateSut();
        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = "new-access-123",
            expires_in = 3600,
            token_type = "Bearer"
        });
        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson)
            });

        var result = await systemUnderTest.RefreshAccessTokenAsync("old-refresh-token");

        result.Should().Be("new-access-123");
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenInvalidGrant_ThrowsGoogleReauthRequiredException()
    {
        var (httpMock, systemUnderTest) = CreateSut();
        var body = JsonSerializer.Serialize(new
        {
            error = "invalid_grant",
            error_description = "Token has been expired or revoked."
        });
        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(body)
            });

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
        var (httpMock, systemUnderTest) = CreateSut();
        var body = JsonSerializer.Serialize(new { error, error_description = "needs reconsent" });
        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(body)
            });

        await systemUnderTest.Invoking(s => s.RefreshAccessTokenAsync("rt"))
            .Should().ThrowAsync<GoogleReauthRequiredException>();
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenNonReauth4xx_ThrowsInvalidOperationWithParsedError()
    {
        var (httpMock, systemUnderTest) = CreateSut();
        var body = JsonSerializer.Serialize(new
        {
            error = "rate_limit_exceeded",
            error_description = "Too many requests"
        });
        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(body)
            });

        await systemUnderTest.Invoking(s => s.RefreshAccessTokenAsync("rt"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*rate_limit_exceeded*");
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenFails_DoesNotLogRefreshTokenValue()
    {
        var (httpMock, sut, loggerMock) = CreateSutWithLogger();
        var body = JsonSerializer.Serialize(new { error = "invalid_grant", error_description = "revoked" });
        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(body)
            });
        const string secretToken = "super-secret-refresh-token-VALUE";

        try { await sut.RefreshAccessTokenAsync(secretToken); } catch { /* expected */ }

        loggerMock.Invocations
            .SelectMany(i => i.Arguments)
            .Select(a => a?.ToString() ?? string.Empty)
            .Should().NotContain(s => s.Contains(secretToken));
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_WhenFails_DoesNotLogRawBodyButLogsParsedError()
    {
        var (httpMock, systemUnderTest, loggerMock) = CreateSutWithLogger();
        var body = JsonSerializer.Serialize(new
        {
            error = "invalid_grant",
            error_description = "Bad Request",
            access_token = "SENSITIVE-LEAK"
        });
        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(body)
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => systemUnderTest.ExchangeCodeForTokenAsync("the-code", "https://app/callback"));

        loggerMock.Verify(l => l.Log(
            It.IsAny<LogLevel>(), It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("SENSITIVE-LEAK")),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);

        loggerMock.Verify(l => l.Log(
            LogLevel.Error, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("invalid_grant")),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public void GetAuthorizationUrl_WithAllParameters_ReturnsUrlWithRequiredQueryParams()
    {
        var (_, systemUnderTest) = CreateSut();
        var redirectUri = "https://myapp.com/api/auth/callback";

        var result = systemUnderTest.GetAuthorizationUrl(redirectUri);

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
        var (_, systemUnderTest) = CreateSut();

        var result = systemUnderTest.GetAuthorizationUrl("https://myapp.com/callback");

        result.Should().StartWith("https://accounts.test.com/o/oauth2/auth");
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_ReturnsGrantedScopeFromResponse()
    {
        var (httpMock, sut) = CreateSut();
        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = "a", refresh_token = "r", expires_in = 3600, token_type = "Bearer",
            id_token = "any-token",
            scope = "openid email https://www.googleapis.com/auth/calendar"
        });
        httpMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent(responseJson) });

        var result = await sut.ExchangeCodeForTokenAsync("code", "https://localhost/callback");

        result.GrantedScope.Should().Be("openid email https://www.googleapis.com/auth/calendar");
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_LogsGrantedScopeAtInformation()
    {
        var (httpMock, sut, loggerMock) = CreateSutWithLogger();
        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = "a", refresh_token = "r", expires_in = 3600, token_type = "Bearer",
            id_token = "any-token",
            scope = "openid email https://www.googleapis.com/auth/calendar"
        });
        httpMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent(responseJson) });

        await sut.ExchangeCodeForTokenAsync("code", "https://localhost/callback");

        loggerMock.Verify(l => l.Log(
            LogLevel.Information, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("https://www.googleapis.com/auth/calendar")),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_LogsGrantedScopeAtInformation()
    {
        var (httpMock, sut, loggerMock) = CreateSutWithLogger();
        var responseJson = JsonSerializer.Serialize(new
        {
            access_token = "new", expires_in = 3600, token_type = "Bearer",
            scope = "openid email https://www.googleapis.com/auth/calendar"
        });
        httpMock.Protected().Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent(responseJson) });

        await sut.RefreshAccessTokenAsync("rt");

        loggerMock.Verify(l => l.Log(
            LogLevel.Information, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("https://www.googleapis.com/auth/calendar")),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static (Mock<HttpMessageHandler> HttpMock, GoogleAuthService Sut) CreateSut()
    {
        var (httpMock, sut, _, _) = CreateSutFull();
        return (httpMock, sut);
    }

    private static (Mock<HttpMessageHandler> HttpMock, GoogleAuthService Sut, Mock<ILogger<GoogleAuthService>> LoggerMock) CreateSutWithLogger()
    {
        var (httpMock, sut, loggerMock, _) = CreateSutFull();
        return (httpMock, sut, loggerMock);
    }

    private static (Mock<HttpMessageHandler> HttpMock, GoogleAuthService Sut, Mock<ILogger<GoogleAuthService>> LoggerMock, Mock<IIdTokenValidator> ValidatorMock) CreateSutFull()
    {
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(httpMessageHandlerMock.Object);

        var options = MsOptions.Options.Create(new GoogleCalendarOptions
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            AuthPromptUrl = "https://accounts.test.com/o/oauth2/auth",
            AuthBaseUrl = "https://test.oauth.com"
        });

        var loggerMock = new Mock<ILogger<GoogleAuthService>>();
        var validatorMock = new Mock<IIdTokenValidator>();
        validatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdTokenClaims("default-user", "default@example.com"));

        var sut = new GoogleAuthService(httpClient, options, loggerMock.Object, validatorMock.Object);
        return (httpMessageHandlerMock, sut, loggerMock, validatorMock);
    }
}
