using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using FamilyHQ.WebApi.Controllers;
using FamilyHQ.WebApi.Hubs;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class AuthControllerTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Login_RedirectsToAuthorizationUrl()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.Login();

        // Assert
        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().StartWith("https://sim.test/oauth2/auth");
        redirect.Url.Should().Contain("client_id=simulator-client");
        redirect.Url.Should().Contain("response_type=code");
    }

    [Fact]
    public void Login_WhenBehindReverseProxy_UsesHttpsCallbackUrl()
    {
        // Arrange — simulate Traefik forwarding X-Forwarded-Proto: https
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new Microsoft.AspNetCore.Http.HostString("preprod.familyhq.alphaepsilon.co.uk:8400");

        var sut = CreateSut(httpContext: httpContext);

        // Act
        var result = sut.Login();

        // Assert
        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Contain("redirect_uri=https%3A%2F%2Fpreprod.familyhq.alphaepsilon.co.uk%3A8400%2Fapi%2Fauth%2Fcallback");
    }

    [Fact]
    public async Task Callback_WhenCodeExchangeSucceeds_SavesRefreshToken()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var tokenStoreMock = new Mock<ITokenStore>();
        var sut = CreateSut(tokenStore: tokenStoreMock.Object,
            httpContext: httpContext,
            dataProtectionProvider: CreateProviderMock(protectorMock));

        await sut.Callback("dummy_code_for_user1", "test-state");

        tokenStoreMock.Verify(t => t.SaveRefreshTokenAsync("simulated_refresh_token", "user1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Callback_WhenCodeExchangeSucceeds_RedirectsToFrontendLoginSuccess()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var sut = CreateSut(httpContext: httpContext, dataProtectionProvider: CreateProviderMock(protectorMock));

        var result = await sut.Callback("dummy_code_for_user1", "test-state");

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().StartWith("https://frontend.test/login-success?code=");
        var uri = new Uri(redirect.Url);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        Uri.UnescapeDataString(query["code"].ToString()).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Callback_WhenFrontendBaseUrlIsNotConfigured_ThrowsInvalidOperationException()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var sut = CreateSut(frontendBaseUrl: null,
            httpContext: httpContext,
            dataProtectionProvider: CreateProviderMock(protectorMock));

        var act = () => sut.Callback("dummy_code_for_user1", "test-state");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*FrontendBaseUrl*");
    }

    [Fact]
    public async Task Callback_WhenWebhookRegistrationEnabled_CallsRegisterAllAsync()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var webhookServiceMock = new Mock<IWebhookRegistrationService>();
        var sut = CreateSut(webhookRegistrationEnabled: true,
            webhookRegistrationService: webhookServiceMock.Object,
            httpContext: httpContext,
            dataProtectionProvider: CreateProviderMock(protectorMock));

        await sut.Callback("dummy_code_for_user1", "test-state");

        webhookServiceMock.Verify(
            w => w.RegisterAllAsync("user1", It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Callback_WhenWebhookRegistrationDisabled_DoesNotCallRegisterAllAsync()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var webhookServiceMock = new Mock<IWebhookRegistrationService>();
        var sut = CreateSut(webhookRegistrationEnabled: false,
            webhookRegistrationService: webhookServiceMock.Object,
            httpContext: httpContext,
            dataProtectionProvider: CreateProviderMock(protectorMock));

        await sut.Callback("dummy_code_for_user1", "test-state");

        webhookServiceMock.Verify(
            w => w.RegisterAllAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Callback_WhenRefreshTokenIsNull_DoesNotSaveToTokenStore()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var tokenStoreMock = new Mock<ITokenStore>();
        var sut = CreateSut(tokenStore: tokenStoreMock.Object,
            includeRefreshToken: false,
            httpContext: httpContext,
            dataProtectionProvider: CreateProviderMock(protectorMock));

        await sut.Callback("dummy_code_for_user1", "test-state");

        tokenStoreMock.Verify(t => t.SaveRefreshTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Callback_WhenNoRefreshTokenReturnedAndNoneStored_RedirectsToReconsentInsteadOfLoginSuccess()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var tokenStoreMock = CreateTokenStoreMock(storedRefreshToken: null);
        var sut = CreateSut(tokenStore: tokenStoreMock.Object,
            includeRefreshToken: false,
            httpContext: httpContext,
            dataProtectionProvider: CreateProviderMock(protectorMock));

        var result = await sut.Callback("dummy_code_for_user1", "test-state");

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(AuthController.Login));
    }

    [Fact]
    public async Task Callback_WhenNoRefreshTokenReturnedAndNoneStored_MarksNeedsReauthWithMessage()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var tokenStoreMock = CreateTokenStoreMock(storedRefreshToken: null);
        var sut = CreateSut(tokenStore: tokenStoreMock.Object,
            includeRefreshToken: false,
            httpContext: httpContext,
            dataProtectionProvider: CreateProviderMock(protectorMock));

        await sut.Callback("dummy_code_for_user1", "test-state");

        tokenStoreMock.Verify(
            t => t.MarkNeedsReauthAsync("user1", AuthController.MissingRefreshTokenMessage, CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task Callback_WhenNoRefreshTokenReturnedAndNoneStored_DoesNotEnqueueSync()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var tokenStoreMock = CreateTokenStoreMock(storedRefreshToken: null);
        var queueMock = new Mock<ICalendarSyncJobQueue>();
        var sut = CreateSut(tokenStore: tokenStoreMock.Object,
            includeRefreshToken: false,
            syncJobQueue: queueMock.Object,
            httpContext: httpContext,
            dataProtectionProvider: CreateProviderMock(protectorMock));

        await sut.Callback("dummy_code_for_user1", "test-state");

        queueMock.Verify(
            q => q.EnqueueAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<SyncJobSource>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Callback_WhenNoRefreshTokenReturnedAndNoneStoredAndMarkFails_StillRedirectsToReconsent()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var tokenStoreMock = CreateTokenStoreMock(storedRefreshToken: null);
        tokenStoreMock
            .Setup(t => t.MarkNeedsReauthAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated DB write failure"));
        var sut = CreateSut(tokenStore: tokenStoreMock.Object,
            includeRefreshToken: false,
            httpContext: httpContext,
            dataProtectionProvider: CreateProviderMock(protectorMock));

        var result = await sut.Callback("dummy_code_for_user1", "test-state");

        // The mark is best-effort — a failed DB write must not 500 the callback when the
        // remedy IS the re-consent redirect.
        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(AuthController.Login));
    }

    [Fact]
    public async Task Callback_WhenNoRefreshTokenReturnedAndCalendarScopeMissing_RedirectsToReconsent()
    {
        // Pins precedence: with BOTH the refresh token and the calendar scope missing, the FHQ-87
        // guard wins over the FHQ-60 missing-scope handling — re-consent fixes both, whereas the
        // FHQ-60 login-success path would hand over a JWT with no stored refresh token behind it.
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var tokenStoreMock = CreateTokenStoreMock(storedRefreshToken: null);
        var sut = CreateSut(tokenStore: tokenStoreMock.Object,
            includeRefreshToken: false,
            grantedScope: "openid email",
            httpContext: httpContext,
            dataProtectionProvider: CreateProviderMock(protectorMock));

        var result = await sut.Callback("dummy_code_for_user1", "test-state");

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(AuthController.Login));
        tokenStoreMock.Verify(
            t => t.MarkNeedsReauthAsync("user1", AuthController.MissingRefreshTokenMessage, CancellationToken.None),
            Times.Once);
        tokenStoreMock.Verify(
            t => t.MarkNeedsReauthAsync(It.IsAny<string>(), AuthController.MissingCalendarScopeMessage, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Callback_WhenNoRefreshTokenReturnedButOneStored_RedirectsToFrontendLoginSuccess()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var tokenStoreMock = CreateTokenStoreMock(storedRefreshToken: "previously-stored-refresh-token");
        var sut = CreateSut(tokenStore: tokenStoreMock.Object,
            includeRefreshToken: false,
            httpContext: httpContext,
            dataProtectionProvider: CreateProviderMock(protectorMock));

        var result = await sut.Callback("dummy_code_for_user1", "test-state");

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().StartWith("https://frontend.test/login-success?code=");
    }

    [Fact]
    public async Task Callback_WhenNoRefreshTokenReturnedButOneStored_DoesNotMarkNeedsReauth()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var tokenStoreMock = CreateTokenStoreMock(storedRefreshToken: "previously-stored-refresh-token");
        var sut = CreateSut(tokenStore: tokenStoreMock.Object,
            includeRefreshToken: false,
            httpContext: httpContext,
            dataProtectionProvider: CreateProviderMock(protectorMock));

        await sut.Callback("dummy_code_for_user1", "test-state");

        tokenStoreMock.Verify(
            t => t.MarkNeedsReauthAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Callback_WhenGrantMissingCalendarScope_MarksNeedsReauthWithMessage()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var tokenStoreMock = new Mock<ITokenStore>();
        var queueMock = new Mock<ICalendarSyncJobQueue>();
        var sut = CreateSut(tokenStore: tokenStoreMock.Object,
            grantedScope: "openid email",
            syncJobQueue: queueMock.Object,
            httpContext: httpContext,
            dataProtectionProvider: CreateProviderMock(protectorMock));

        var result = await sut.Callback("dummy_code_for_user1", "test-state");

        tokenStoreMock.Verify(t => t.MarkNeedsReauthAsync("user1", AuthController.MissingCalendarScopeMessage, It.IsAny<CancellationToken>()), Times.Once);
        queueMock.Verify(q => q.EnqueueAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<SyncJobSource>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().StartWith("https://frontend.test/login-success?code=");
        var uri = new Uri(redirect.Url);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        Uri.UnescapeDataString(query["code"].ToString()).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Callback_WhenGrantHasCalendarScope_DoesNotMarkReauth()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var tokenStoreMock = new Mock<ITokenStore>();
        var sut = CreateSut(tokenStore: tokenStoreMock.Object,
            httpContext: httpContext,
            dataProtectionProvider: CreateProviderMock(protectorMock));

        await sut.Callback("dummy_code_for_user1", "test-state");

        tokenStoreMock.Verify(t => t.MarkNeedsReauthAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Login_SetsHttpOnlySameSiteLaxOAuthStateCookie()
    {
        var httpContext = new DefaultHttpContext();
        var sut = CreateSut(httpContext: httpContext);

        sut.Login();

        var setCookieHeader = httpContext.Response.Headers["Set-Cookie"].ToString();
        setCookieHeader.Should().Contain("oauth_state=");
        setCookieHeader.ToLowerInvariant().Should().Contain("httponly");
        setCookieHeader.ToLowerInvariant().Should().Contain("samesite=lax");
    }

    [Fact]
    public void Login_IncludesStateParamInRedirectUrl()
    {
        var httpContext = new DefaultHttpContext();
        var sut = CreateSut(httpContext: httpContext);

        var result = sut.Login();

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Contain("&state=");
    }

    [Fact]
    public async Task Callback_WhenStateCookieMissing_ReturnsBadRequest()
    {
        var sut = CreateSut(); // no cookie set

        var result = await sut.Callback("dummy_code_for_user1", "some-state");

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("Authentication failed: invalid state.");
    }

    [Fact]
    public async Task Callback_WhenStateParamMissing_ReturnsBadRequest()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var sut = CreateSut(httpContext: httpContext, dataProtectionProvider: CreateProviderMock(protectorMock));

        var result = await sut.Callback("dummy_code_for_user1", state: null);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("Authentication failed: invalid state.");
    }

    [Fact]
    public async Task Callback_WhenStateCookieTampered_ReturnsBadRequest()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Cookie"] = "oauth_state=tampered!!not-valid-base64@@";
        var sut = CreateSut(httpContext: httpContext);

        var result = await sut.Callback("dummy_code_for_user1", "some-state");

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("Authentication failed: invalid state.");
    }

    [Fact]
    public async Task Callback_WhenStateMismatch_ReturnsBadRequest()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "expected-state");
        var sut = CreateSut(httpContext: httpContext, dataProtectionProvider: CreateProviderMock(protectorMock));

        var result = await sut.Callback("dummy_code_for_user1", "wrong-state");

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("Authentication failed: invalid state.");
    }

    [Fact]
    public async Task Callback_WhenStateValid_DeletesStateCookie()
    {
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var sut = CreateSut(httpContext: httpContext, dataProtectionProvider: CreateProviderMock(protectorMock));

        await sut.Callback("dummy_code_for_user1", "test-state");

        var setCookieHeaders = httpContext.Response.Headers["Set-Cookie"].ToString();
        setCookieHeaders.Should().Contain("oauth_state=");
        setCookieHeaders.Should().Match(h => h.Contains("expires=", StringComparison.OrdinalIgnoreCase) || h.Contains("max-age=0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Exchange_ValidCode_ReturnsToken()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        const string knownCode = "valid-exchange-code";
        const string knownToken = "a.jwt.token";
        cache.Set(knownCode, knownToken, TimeSpan.FromSeconds(60));
        var sut = CreateSut(memoryCache: cache);

        // Act
        var result = sut.Exchange(new ExchangeCodeRequest(knownCode));

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain(knownToken);
        json.Should().Contain("\"token\":");
    }

    [Fact]
    public void Exchange_InvalidCode_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.Exchange(new ExchangeCodeRequest("nonexistent-code"));

        // Assert
        result.Should().BeOfType<BadRequestResult>();
    }

    [Fact]
    public void Exchange_AlreadyRedeemedCode_ReturnsBadRequest()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        const string knownCode = "one-time-code";
        cache.Set(knownCode, "some.jwt.token", TimeSpan.FromSeconds(60));
        var sut = CreateSut(memoryCache: cache);

        // Act — first call should succeed, second should return BadRequest
        sut.Exchange(new ExchangeCodeRequest(knownCode));
        var result = sut.Exchange(new ExchangeCodeRequest(knownCode));

        // Assert
        result.Should().BeOfType<BadRequestResult>();
    }

    [Fact]
    public void Exchange_EmptyCode_ReturnsBadRequest()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.Exchange(new ExchangeCodeRequest(string.Empty));

        // Assert
        result.Should().BeOfType<BadRequestResult>();
    }

    [Fact]
    public async Task GenerateJwt_Expiry_IsUtcBased()
    {
        // Arrange — share a MemoryCache so Callback can populate it and Exchange can read it
        var cache = new MemoryCache(new MemoryCacheOptions());
        var protectorMock = CreateProtectorMock();
        var httpContext = new DefaultHttpContext();
        SetValidStateCookie(httpContext, protectorMock.Object, "test-state");
        var sut = CreateSut(
            httpContext: httpContext,
            dataProtectionProvider: CreateProviderMock(protectorMock),
            memoryCache: cache);

        // Act 1 — drive Callback so it generates and caches the JWT under a one-time code
        var callbackResult = await sut.Callback("dummy_code_for_user1", "test-state");
        var redirect = callbackResult.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().StartWith("https://frontend.test/login-success?code=");

        // Extract and decode the one-time code from the redirect URL
        var uri = new Uri(redirect.Url);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        var decodedCode = Uri.UnescapeDataString(query["code"].ToString());

        // Act 2 — exchange the code for the JWT
        var exchangeResult = sut.Exchange(new ExchangeCodeRequest(decodedCode));
        var ok = exchangeResult.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        var token = doc.GetProperty("token").GetString()!;

        // Assert — decode the JWT and verify expiry is exactly 365 days from the injected clock, UTC
        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        decoded.ValidTo.Kind.Should().Be(DateTimeKind.Utc);
        decoded.ValidTo.Should().Be(TestNow.AddDays(365).UtcDateTime);
    }

    [Fact]
    public void RenewJwt_WithAuthenticatedPrincipal_ReturnsNewTokenForSameUser()
    {
        // Arrange
        var httpContext = new DefaultHttpContext
        {
            User = CreateAuthenticatedPrincipal("user1", "user1@example.com")
        };
        var sut = CreateSut(httpContext: httpContext);

        // Act
        var result = sut.RenewJwt();

        // Assert — token in the response body (never a URL), same claims + lifetime policy
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        var token = doc.GetProperty("token").GetString()!;
        token.Should().NotBeNullOrEmpty();

        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        decoded.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == "user1");
        decoded.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Name && c.Value == "user1@example.com");
        decoded.ValidTo.Should().Be(TestNow.AddDays(365).UtcDateTime);
    }

    [Fact]
    public void RenewJwt_WithoutNameClaim_ReturnsTokenWithoutNameClaim()
    {
        // Arrange
        var httpContext = new DefaultHttpContext
        {
            User = CreateAuthenticatedPrincipal("user1", email: null)
        };
        var sut = CreateSut(httpContext: httpContext);

        // Act
        var result = sut.RenewJwt();

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        var token = doc.GetProperty("token").GetString()!;

        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        decoded.Claims.Should().NotContain(c => c.Type == JwtRegisteredClaimNames.Name);
    }

    [Fact]
    public void RenewJwt_WhenSubClaimMissing_ReturnsUnauthorized()
    {
        // Arrange — authenticated identity but no "sub" claim (should never happen with our tokens)
        var identity = new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(JwtRegisteredClaimNames.Name, "user1@example.com") },
            authenticationType: "TestAuth");
        var httpContext = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(identity)
        };
        var sut = CreateSut(httpContext: httpContext);

        // Act
        var result = sut.RenewJwt();

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public void RenewJwt_CarriesOriginalAuthTimeThrough()
    {
        // Arrange — session started 100 days ago; renewal must NOT reset the session clock
        var originalAuthTime = TestNow.AddDays(-100).ToUnixTimeSeconds();
        var httpContext = new DefaultHttpContext
        {
            User = CreateAuthenticatedPrincipal("user1", "user1@example.com", authTime: originalAuthTime.ToString())
        };
        var sut = CreateSut(httpContext: httpContext);

        // Act
        var result = sut.RenewJwt();

        // Assert
        var token = ExtractToken(result);
        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var expectedClaimValue = originalAuthTime.ToString();
        decoded.Claims.Should().Contain(c =>
            c.Type == JwtRegisteredClaimNames.AuthTime && c.Value == expectedClaimValue);
    }

    [Fact]
    public void RenewJwt_WhenSessionAgeJustUnderCap_ReturnsOk()
    {
        // Arrange — 729 days into the default 730-day cap
        var authTime = TestNow.AddDays(-729).ToUnixTimeSeconds();
        var httpContext = new DefaultHttpContext
        {
            User = CreateAuthenticatedPrincipal("user1", "user1@example.com", authTime: authTime.ToString())
        };
        var sut = CreateSut(httpContext: httpContext);

        // Act
        var result = sut.RenewJwt();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void RenewJwt_WhenSessionAgeExceedsCap_ReturnsUnauthorized()
    {
        // Arrange — 731 days exceeds the default 730-day cap; kiosk must re-authenticate
        var authTime = TestNow.AddDays(-731).ToUnixTimeSeconds();
        var httpContext = new DefaultHttpContext
        {
            User = CreateAuthenticatedPrincipal("user1", "user1@example.com", authTime: authTime.ToString())
        };
        var sut = CreateSut(httpContext: httpContext);

        // Act
        var result = sut.RenewJwt();

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbled")]
    [InlineData("9999999999999999")] // parses as long but is beyond the DateTimeOffset range
    public void RenewJwt_WhenAuthTimeMissingOrUnreadable_GrandfathersWithFreshAuthTime(string? authTime)
    {
        // Arrange — tokens minted before auth_time existed get a fresh cap window
        var httpContext = new DefaultHttpContext
        {
            User = CreateAuthenticatedPrincipal("user1", "user1@example.com", authTime)
        };
        var sut = CreateSut(httpContext: httpContext);

        // Act
        var result = sut.RenewJwt();

        // Assert — renewed, and the new token starts a cap window at "now"
        var token = ExtractToken(result);
        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var authTimeClaim = decoded.Claims.Single(c => c.Type == JwtRegisteredClaimNames.AuthTime);
        var mintedAuthTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(authTimeClaim.Value));
        mintedAuthTime.Should().Be(TestNow);
    }

    [Fact]
    public void RenewJwt_NearCap_ClampsRenewedTokenExpiryToSessionCap()
    {
        // Arrange — 729 days into the default 730-day cap
        var authTime = TestNow.AddDays(-729);
        var httpContext = new DefaultHttpContext
        {
            User = CreateAuthenticatedPrincipal("user1", "user1@example.com",
                authTime: authTime.ToUnixTimeSeconds().ToString())
        };
        var sut = CreateSut(httpContext: httpContext);

        // Act
        var result = sut.RenewJwt();

        // Assert — the renewed token dies AT the cap (authTime + 730d = 1 day away), not
        // now + 365d; otherwise the effective leaked-token bound would be cap + lifetime
        var token = ExtractToken(result);
        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        decoded.ValidTo.Should().Be(authTime.AddDays(730).UtcDateTime);
    }

    [Fact]
    public void RenewJwt_NormalRenewal_KeepsFullTokenLifetime()
    {
        // Arrange — 100-day-old session: cap expiry is far beyond the normal lifetime
        var authTime = TestNow.AddDays(-100);
        var httpContext = new DefaultHttpContext
        {
            User = CreateAuthenticatedPrincipal("user1", "user1@example.com",
                authTime: authTime.ToUnixTimeSeconds().ToString())
        };
        var sut = CreateSut(httpContext: httpContext);

        // Act
        var result = sut.RenewJwt();

        // Assert
        var token = ExtractToken(result);
        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        decoded.ValidTo.Should().Be(TestNow.AddDays(365).UtcDateTime);
    }

    [Fact]
    public void RenewJwt_SetsCacheControlNoStoreOnResponse()
    {
        // Arrange
        var httpContext = new DefaultHttpContext
        {
            User = CreateAuthenticatedPrincipal("user1", "user1@example.com")
        };
        var sut = CreateSut(httpContext: httpContext);

        // Act
        sut.RenewJwt();

        // Assert — OAuth BCP: token responses must never be cached
        httpContext.Response.Headers.CacheControl.ToString().Should().Contain("no-store");
    }

    [Fact]
    public void RenewJwt_IsProtectedByAuthorizeAttribute()
    {
        // Arrange
        var method = typeof(AuthController).GetMethod(nameof(AuthController.RenewJwt))!;

        // Assert — renewal must only extend a LIVE session; no anonymous path
        method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Should().NotBeEmpty();
        method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: true)
            .Should().BeEmpty();
    }

    private static System.Security.Claims.ClaimsPrincipal CreateAuthenticatedPrincipal(
        string userId, string? email, string? authTime = null)
    {
        var claims = new List<System.Security.Claims.Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.UniqueName, userId)
        };
        if (!string.IsNullOrEmpty(email))
            claims.Add(new(JwtRegisteredClaimNames.Name, email));
        if (!string.IsNullOrEmpty(authTime))
            claims.Add(new(JwtRegisteredClaimNames.AuthTime, authTime));

        // MapInboundClaims=false in Program.cs keeps raw claim names ("sub"/"name") on the principal.
        return new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(claims, authenticationType: "TestAuth"));
    }

    private static TimeProvider CreateFixedTimeProvider(DateTimeOffset now)
    {
        var mock = new Mock<TimeProvider>();
        mock.Setup(t => t.GetUtcNow()).Returns(now);
        return mock.Object;
    }

    private static string ExtractToken(IActionResult result)
    {
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        return doc.GetProperty("token").GetString()!;
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

    /// <summary>
    /// Token-store mock whose per-user GetRefreshTokenAsync overload returns the given stored token
    /// (null = nothing stored), as the FHQ-87 callback guard reads it.
    /// </summary>
    private static Mock<ITokenStore> CreateTokenStoreMock(string? storedRefreshToken)
    {
        var tokenStoreMock = new Mock<ITokenStore>();
        tokenStoreMock
            .Setup(t => t.GetRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedRefreshToken);
        return tokenStoreMock;
    }

    private static Mock<IDataProtector> CreateProtectorMock()
    {
        var protectorMock = new Mock<IDataProtector>();
        // Passthrough: the string extension methods base64-encode/decode around these,
        // giving a deterministic roundtrip in tests.
        protectorMock.Setup(p => p.Protect(It.IsAny<byte[]>())).Returns((byte[] b) => b);
        protectorMock.Setup(p => p.Unprotect(It.IsAny<byte[]>())).Returns((byte[] b) => b);
        return protectorMock;
    }

    private static IDataProtectionProvider CreateProviderMock(Mock<IDataProtector> protectorMock)
    {
        var providerMock = new Mock<IDataProtectionProvider>();
        providerMock.Setup(p => p.CreateProtector(It.IsAny<string>())).Returns(protectorMock.Object);
        return providerMock.Object;
    }

    private static void SetValidStateCookie(DefaultHttpContext httpContext, IDataProtector protector, string rawState)
    {
        var cookieValue = protector.Protect(rawState);
        httpContext.Request.Headers["Cookie"] = $"oauth_state={cookieValue}";
    }

    private static AuthController CreateSut(
        ITokenStore? tokenStore = null,
        string? frontendBaseUrl = "https://frontend.test",
        bool includeRefreshToken = true,
        DefaultHttpContext? httpContext = null,
        bool webhookRegistrationEnabled = false,
        IWebhookRegistrationService? webhookRegistrationService = null,
        string? grantedScope = "openid email https://www.googleapis.com/auth/calendar",
        ICalendarSyncJobQueue? syncJobQueue = null,
        IDataProtectionProvider? dataProtectionProvider = null,
        IMemoryCache? memoryCache = null)
    {
        // Build a GoogleAuthService backed by a fake HttpMessageHandler
        var responsePayload = new Dictionary<string, object?>
        {
            ["access_token"] = "simulated_access_token",
            ["refresh_token"] = includeRefreshToken ? (object?)"simulated_refresh_token" : null,
            ["expires_in"] = 3600,
            ["token_type"] = "Bearer",
            ["id_token"] = CreateTestIdToken("user1", "user1@example.com"),
            ["scope"] = grantedScope
        };
        var responseJson = JsonSerializer.Serialize(responsePayload);

        var httpHandlerMock = new Mock<HttpMessageHandler>();
        httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson)
            });

        var options = Options.Create(new GoogleCalendarOptions
        {
            ClientId = "simulator-client",
            ClientSecret = "simulator-secret",
            AuthPromptUrl = "https://sim.test/oauth2/auth",
            AuthBaseUrl = "https://sim.test"
        });

        var idTokenValidatorMock = new Mock<IIdTokenValidator>();
        idTokenValidatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdTokenClaims("user1", "user1@example.com"));
        var authService = new GoogleAuthService(
            new HttpClient(httpHandlerMock.Object),
            options,
            new Mock<ILogger<GoogleAuthService>>().Object,
            idTokenValidatorMock.Object);

        // IConfiguration
        var configPairs = new List<KeyValuePair<string, string?>>();
        if (frontendBaseUrl != null)
            configPairs.Add(new KeyValuePair<string, string?>("FrontendBaseUrl", frontendBaseUrl));
        configPairs.Add(new KeyValuePair<string, string?>("Jwt:SigningKey", "SuperSecretDummyKeyForFamilyHqSimulatorMVF1"));

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configPairs)
            .Build();

        // IServiceScopeFactory — returns a scope that yields a sync service and hub context
        var syncServiceMock = new Mock<ICalendarSyncService>();
        var clientProxyMock = new Mock<IClientProxy>();
        var clientsMock = new Mock<IHubClients>();
        clientsMock.Setup(c => c.All).Returns(clientProxyMock.Object);
        var hubContextMock = new Mock<IHubContext<CalendarHub>>();
        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        var webhookServiceObj = webhookRegistrationService ?? new Mock<IWebhookRegistrationService>().Object;

        var scopeMock = new Mock<IServiceScope>();
        var providerMock = new Mock<IServiceProvider>();
        providerMock.Setup(p => p.GetService(typeof(ICalendarSyncService))).Returns(syncServiceMock.Object);
        providerMock.Setup(p => p.GetService(typeof(IHubContext<CalendarHub>))).Returns(hubContextMock.Object);
        providerMock.Setup(p => p.GetService(typeof(IWebhookRegistrationService))).Returns(webhookServiceObj);
        var syncJobQueueObj = syncJobQueue ?? new Mock<ICalendarSyncJobQueue>().Object;
        providerMock.Setup(p => p.GetService(typeof(ICalendarSyncJobQueue))).Returns(syncJobQueueObj);
        providerMock.Setup(p => p.GetService(typeof(ISyncJobSignal))).Returns(new Mock<ISyncJobSignal>().Object);
        scopeMock.Setup(s => s.ServiceProvider).Returns(providerMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var syncOptions = Options.Create(new SyncOptions { WebhookRegistrationEnabled = webhookRegistrationEnabled });

        // DataProtection — default to passthrough mock
        dataProtectionProvider ??= CreateProviderMock(CreateProtectorMock());

        // MemoryCache — default to real instance
        var cache = memoryCache ?? new MemoryCache(new MemoryCacheOptions());

        // Fixed clock + shared session options so cap/expiry assertions are exact
        var sessionOptions = new FamilyHQ.WebApi.Configuration.JwtSessionOptions();
        var clock = CreateFixedTimeProvider(TestNow);

        var controller = new AuthController(
            authService,
            tokenStore ?? new Mock<ITokenStore>().Object,
            scopeFactoryMock.Object,
            configuration,
            syncOptions,
            new Mock<ILogger<AuthController>>().Object,
            dataProtectionProvider,
            cache,
            new FamilyHQ.WebApi.Services.JwtTokenService(configuration, sessionOptions, clock),
            sessionOptions,
            clock)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext ?? new DefaultHttpContext()
            }
        };

        return controller;
    }
}
