using System.Net;
using System.Text.Json;
using FamilyHQ.Core.Interfaces;
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
        var (httpMock, systemUnderTest, _, validatorMock, _) = CreateSutFull();
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
        var (httpMock, systemUnderTest, _, validatorMock, _) = CreateSutFull();
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

        result.AccessToken.Should().Be("new-access-123");
        result.ExpiresIn.Should().Be(3600);
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

        var result = systemUnderTest.GetAuthorizationUrl(redirectUri, "test-state");

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

        var result = systemUnderTest.GetAuthorizationUrl("https://myapp.com/callback", "test-state");

        result.Should().StartWith("https://accounts.test.com/o/oauth2/auth");
    }

    [Fact]
    public void GetAuthorizationUrl_IncludesStateParam()
    {
        var (_, systemUnderTest) = CreateSut();

        var url = systemUnderTest.GetAuthorizationUrl("https://localhost/callback", "csrf-test-state");

        url.Should().Contain("&state=csrf-test-state");
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

    // ── FHQ-86: rotated-refresh-token persistence ──────────────────────────

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenResponseContainsRotatedRefreshToken_PersistsRotatedTokenBeforeReturning()
    {
        var (httpMock, sut, tokenStoreMock) = CreateSutWithTokenStore();
        SetupSuccessfulRefreshResponse(httpMock, rotatedRefreshToken: "rotated-refresh-token");

        var result = await sut.RefreshAccessTokenAsync("old-refresh-token");

        result.AccessToken.Should().Be("new-access-123");
        tokenStoreMock.Verify(
            t => t.SaveRefreshTokenAsync("rotated-refresh-token", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenPersistingRotatedToken_UsesUncancellableToken()
    {
        var (httpMock, sut, tokenStoreMock) = CreateSutWithTokenStore();
        SetupSuccessfulRefreshResponse(httpMock, rotatedRefreshToken: "rotated-refresh-token");
        using var callerCts = new CancellationTokenSource();

        await sut.RefreshAccessTokenAsync("old-refresh-token", callerCts.Token);

        tokenStoreMock.Verify(
            t => t.SaveRefreshTokenAsync("rotated-refresh-token", CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhilePersistingRotatedToken_DoesNotReturnUntilSaveCompletes()
    {
        var (httpMock, sut, tokenStoreMock) = CreateSutWithTokenStore();
        SetupSuccessfulRefreshResponse(httpMock, rotatedRefreshToken: "rotated-refresh-token");
        var saveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tokenStoreMock
            .Setup(t => t.SaveRefreshTokenAsync("rotated-refresh-token", It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                saveInvoked.TrySetResult();
                return saveGate.Task;
            });

        var refreshTask = sut.RefreshAccessTokenAsync("old-refresh-token");

        // Deterministic, timer-free ordering pin: the refresh must invoke the save (saveInvoked wins
        // the WhenAny) and must stay incomplete while the save task is held open — a regression to
        // fire-and-forget persistence would let refreshTask complete with the gate still pending.
        var firstCompleted = await Task.WhenAny(saveInvoked.Task, refreshTask);
        firstCompleted.Should().BeSameAs(saveInvoked.Task,
            "the refresh must invoke rotated-token persistence before it can complete");
        refreshTask.IsCompleted.Should().BeFalse(
            "the refresh must not return while the rotated-token save is still pending");

        saveGate.SetResult();
        var result = await refreshTask;
        result.AccessToken.Should().Be("new-access-123");
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenResponseHasNoRefreshToken_DoesNotSaveToTokenStore()
    {
        var (httpMock, sut, tokenStoreMock) = CreateSutWithTokenStore();
        SetupSuccessfulRefreshResponse(httpMock, rotatedRefreshToken: null);

        await sut.RefreshAccessTokenAsync("old-refresh-token");

        tokenStoreMock.Verify(
            t => t.SaveRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        tokenStoreMock.Verify(
            t => t.SaveRefreshTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenResponseHasEmptyRefreshToken_DoesNotSaveToTokenStore()
    {
        var (httpMock, sut, tokenStoreMock) = CreateSutWithTokenStore();
        SetupSuccessfulRefreshResponse(httpMock, rotatedRefreshToken: "");

        await sut.RefreshAccessTokenAsync("old-refresh-token");

        tokenStoreMock.Verify(
            t => t.SaveRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        tokenStoreMock.Verify(
            t => t.SaveRefreshTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenPersistingRotatedTokenFails_LogsErrorAndStillReturnsAccessToken()
    {
        var (httpMock, sut, loggerMock, _, tokenStoreMock) = CreateSutFull();
        SetupSuccessfulRefreshResponse(httpMock, rotatedRefreshToken: "rotated-refresh-token");
        tokenStoreMock
            .Setup(t => t.SaveRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("token store unavailable"));

        var result = await sut.RefreshAccessTokenAsync("old-refresh-token");

        result.AccessToken.Should().Be("new-access-123");
        loggerMock.Verify(l => l.Log(
            LogLevel.Error, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("rotated refresh token")),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenPersistingRotatedTokenFails_DoesNotLogTokenValue()
    {
        var (httpMock, sut, loggerMock, _, tokenStoreMock) = CreateSutFull();
        const string rotatedToken = "rotated-SECRET-refresh-token-VALUE";
        SetupSuccessfulRefreshResponse(httpMock, rotatedRefreshToken: rotatedToken);
        tokenStoreMock
            .Setup(t => t.SaveRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("token store unavailable"));

        await sut.RefreshAccessTokenAsync("old-refresh-token");

        loggerMock.Invocations
            .SelectMany(i => i.Arguments)
            .Select(a => a?.ToString() ?? string.Empty)
            .Should().NotContain(s => s.Contains(rotatedToken));
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenRotatedTokenPersisted_DoesNotLogTokenValue()
    {
        var (httpMock, sut, loggerMock, _, _) = CreateSutFull();
        const string rotatedToken = "rotated-SECRET-refresh-token-VALUE";
        SetupSuccessfulRefreshResponse(httpMock, rotatedRefreshToken: rotatedToken);

        await sut.RefreshAccessTokenAsync("old-refresh-token");

        loggerMock.Invocations
            .SelectMany(i => i.Arguments)
            .Select(a => a?.ToString() ?? string.Empty)
            .Should().NotContain(s => s.Contains(rotatedToken));
    }

    [Fact]
    public async Task RefreshAccessTokenAsync_WhenRotatedTokenPersisted_LogsRotationAtInformation()
    {
        var (httpMock, sut, loggerMock, _, _) = CreateSutFull();
        SetupSuccessfulRefreshResponse(httpMock, rotatedRefreshToken: "rotated-refresh-token");

        await sut.RefreshAccessTokenAsync("old-refresh-token");

        loggerMock.Verify(l => l.Log(
            LogLevel.Information, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("rotated")),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void SetupSuccessfulRefreshResponse(Mock<HttpMessageHandler> httpMock, string? rotatedRefreshToken)
    {
        var payload = new Dictionary<string, object>
        {
            ["access_token"] = "new-access-123",
            ["expires_in"] = 3600,
            ["token_type"] = "Bearer"
        };
        if (rotatedRefreshToken != null)
            payload["refresh_token"] = rotatedRefreshToken;

        httpMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(payload))
            });
    }

    private static (Mock<HttpMessageHandler> HttpMock, GoogleAuthService Sut) CreateSut()
    {
        var (httpMock, sut, _, _, _) = CreateSutFull();
        return (httpMock, sut);
    }

    private static (Mock<HttpMessageHandler> HttpMock, GoogleAuthService Sut, Mock<ILogger<GoogleAuthService>> LoggerMock) CreateSutWithLogger()
    {
        var (httpMock, sut, loggerMock, _, _) = CreateSutFull();
        return (httpMock, sut, loggerMock);
    }

    private static (Mock<HttpMessageHandler> HttpMock, GoogleAuthService Sut, Mock<ITokenStore> TokenStoreMock) CreateSutWithTokenStore()
    {
        var (httpMock, sut, _, _, tokenStoreMock) = CreateSutFull();
        return (httpMock, sut, tokenStoreMock);
    }

    private static (Mock<HttpMessageHandler> HttpMock, GoogleAuthService Sut, Mock<ILogger<GoogleAuthService>> LoggerMock, Mock<IIdTokenValidator> ValidatorMock, Mock<ITokenStore> TokenStoreMock) CreateSutFull()
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

        var tokenStoreMock = new Mock<ITokenStore>();

        var sut = new GoogleAuthService(httpClient, options, loggerMock.Object, validatorMock.Object, tokenStoreMock.Object);
        return (httpMessageHandlerMock, sut, loggerMock, validatorMock, tokenStoreMock);
    }
}
