using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using FamilyHQ.WebApi.Controllers;
using FamilyHQ.WebApi.Hubs;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class AuthControllerTests
{
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
        // Arrange
        var tokenStoreMock = new Mock<ITokenStore>();
        var sut = CreateSut(tokenStore: tokenStoreMock.Object);

        // Act
        await sut.Callback("dummy_code_for_user1");

        // Assert
        tokenStoreMock.Verify(t => t.SaveRefreshTokenAsync("simulated_refresh_token", "user1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Callback_WhenCodeExchangeSucceeds_RedirectsToFrontendLoginSuccess()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.Callback("dummy_code_for_user1");

        // Assert
        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().StartWith("https://frontend.test/login-success?token=");
    }

    [Fact]
    public async Task Callback_WhenFrontendBaseUrlIsNotConfigured_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut(frontendBaseUrl: null);

        // Act
        var act = () => sut.Callback("dummy_code_for_user1");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*FrontendBaseUrl*");
    }

    [Fact]
    public async Task Callback_WhenRefreshTokenIsNull_DoesNotSaveToTokenStore()
    {
        // Arrange
        var tokenStoreMock = new Mock<ITokenStore>();
        var sut = CreateSut(tokenStore: tokenStoreMock.Object, includeRefreshToken: false);

        // Act
        await sut.Callback("dummy_code_for_user1");

        // Assert
        tokenStoreMock.Verify(t => t.SaveRefreshTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static string CreateTestIdToken(string sub)
    {
        var header = Base64UrlEncode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncode($"{{\"sub\":\"{sub}\"}}");
        return $"{header}.{payload}.";
    }

    private static string Base64UrlEncode(string input)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(input))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static AuthController CreateSut(
        ITokenStore? tokenStore = null,
        string? frontendBaseUrl = "https://frontend.test",
        bool includeRefreshToken = true,
        DefaultHttpContext? httpContext = null)
    {
        // Build a GoogleAuthService backed by a fake HttpMessageHandler
        var responsePayload = new Dictionary<string, object?>
        {
            ["access_token"] = "simulated_access_token",
            ["refresh_token"] = includeRefreshToken ? (object?)"simulated_refresh_token" : null,
            ["expires_in"] = 3600,
            ["token_type"] = "Bearer",
            ["id_token"] = CreateTestIdToken("user1")
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

        var authService = new GoogleAuthService(
            new HttpClient(httpHandlerMock.Object),
            options,
            new Mock<ILogger<GoogleAuthService>>().Object);

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

        var accessTokenProviderMock = new Mock<IAccessTokenProvider>();

        var scopeMock = new Mock<IServiceScope>();
        var providerMock = new Mock<IServiceProvider>();
        providerMock.Setup(p => p.GetService(typeof(IAccessTokenProvider))).Returns(accessTokenProviderMock.Object);
        providerMock.Setup(p => p.GetService(typeof(ICalendarSyncService))).Returns(syncServiceMock.Object);
        providerMock.Setup(p => p.GetService(typeof(IHubContext<CalendarHub>))).Returns(hubContextMock.Object);
        scopeMock.Setup(s => s.ServiceProvider).Returns(providerMock.Object);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var controller = new AuthController(
            authService,
            tokenStore ?? new Mock<ITokenStore>().Object,
            scopeFactoryMock.Object,
            configuration)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext ?? new DefaultHttpContext()
            }
        };

        return controller;
    }
}
