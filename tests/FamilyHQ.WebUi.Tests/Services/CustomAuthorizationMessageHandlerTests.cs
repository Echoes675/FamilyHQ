using System.Net;
using FamilyHQ.WebUi.Services.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FamilyHQ.WebUi.Tests.Services;

public class CustomAuthorizationMessageHandlerTests
{
    private const string StoredToken = "stored.jwt.token";
    private const string RenewedToken = "renewed.jwt.token";

    [Fact]
    public async Task SendAsync_WithStoredToken_AttachesBearerHeader()
    {
        // Arrange
        var tokenStore = CreateTokenStoreMock(StoredToken);
        var renewal = new Mock<IJwtRenewalService>();
        var inner = new ScriptedHandler(HttpStatusCode.OK);
        var (invoker, _) = CreateSut(tokenStore, renewal, inner);

        // Act
        using var response = await SendRequestAsync(invoker);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.AuthorizationHeaders.Should().ContainSingle().Which.Should().Be($"Bearer {StoredToken}");
    }

    [Fact]
    public async Task SendAsync_WithoutToken_On401_DoesNotRenewOrRedirect()
    {
        // Arrange — no token stored (e.g. pre-login); a 401 is passed through untouched
        var tokenStore = CreateTokenStoreMock(storedToken: null);
        var renewal = new Mock<IJwtRenewalService>();
        var inner = new ScriptedHandler(HttpStatusCode.Unauthorized);
        var (invoker, nav) = CreateSut(tokenStore, renewal, inner);

        // Act
        using var response = await SendRequestAsync(invoker);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        renewal.Verify(r => r.RenewNowAsync(It.IsAny<CancellationToken>()), Times.Never);
        tokenStore.Verify(s => s.ClearTokenAsync(), Times.Never);
        nav.Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_On401_RenewsAndRetriesOnce_UsingNewToken()
    {
        // Arrange — first attempt 401, renewal succeeds, retry succeeds
        var tokenStore = CreateTokenStoreMock(StoredToken);
        var renewal = new Mock<IJwtRenewalService>();
        renewal.Setup(r => r.RenewNowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(RenewedToken);
        var inner = new ScriptedHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        var (invoker, nav) = CreateSut(tokenStore, renewal, inner);

        // Act
        using var response = await SendRequestAsync(invoker);

        // Assert — request retried once with the renewed token; no sign-out
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.AuthorizationHeaders.Should().HaveCount(2);
        inner.AuthorizationHeaders[0].Should().Be($"Bearer {StoredToken}");
        inner.AuthorizationHeaders[1].Should().Be($"Bearer {RenewedToken}");
        renewal.Verify(r => r.RenewNowAsync(It.IsAny<CancellationToken>()), Times.Once);
        tokenStore.Verify(s => s.ClearTokenAsync(), Times.Never);
        nav.Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_On401_WhenRenewalFails_ClearsTokenAndRedirects()
    {
        // Arrange — renewal returns null (renewal call itself failed/401'd)
        var tokenStore = CreateTokenStoreMock(StoredToken);
        var renewal = new Mock<IJwtRenewalService>();
        renewal.Setup(r => r.RenewNowAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        var inner = new ScriptedHandler(HttpStatusCode.Unauthorized);
        var (invoker, nav) = CreateSut(tokenStore, renewal, inner);

        // Act
        using var response = await SendRequestAsync(invoker);

        // Assert — falls through to the existing clear + redirect behaviour
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        inner.AuthorizationHeaders.Should().HaveCount(1);
        tokenStore.Verify(s => s.ClearTokenAsync(), Times.Once);
        nav.Navigations.Should().ContainSingle();
    }

    [Fact]
    public async Task SendAsync_On401_WhenRetryAlso401_ClearsTokenAndRedirects_WithoutSecondRenewal()
    {
        // Arrange — renewal succeeds but the retried request still 401s
        var tokenStore = CreateTokenStoreMock(StoredToken);
        var renewal = new Mock<IJwtRenewalService>();
        renewal.Setup(r => r.RenewNowAsync(It.IsAny<CancellationToken>())).ReturnsAsync(RenewedToken);
        var inner = new ScriptedHandler(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized);
        var (invoker, nav) = CreateSut(tokenStore, renewal, inner);

        // Act
        using var response = await SendRequestAsync(invoker);

        // Assert — exactly ONE renewal attempt and ONE retry; then sign-out (no loop)
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        inner.AuthorizationHeaders.Should().HaveCount(2);
        renewal.Verify(r => r.RenewNowAsync(It.IsAny<CancellationToken>()), Times.Once);
        tokenStore.Verify(s => s.ClearTokenAsync(), Times.Once);
        nav.Navigations.Should().ContainSingle();
    }

    #region Helpers

    private static Mock<IAuthTokenStore> CreateTokenStoreMock(string? storedToken)
    {
        var mock = new Mock<IAuthTokenStore>();
        mock.Setup(s => s.GetTokenAsync()).ReturnsAsync(storedToken);
        return mock;
    }

    private static (HttpMessageInvoker invoker, TestNavigationManager nav) CreateSut(
        Mock<IAuthTokenStore> tokenStore,
        Mock<IJwtRenewalService> renewal,
        ScriptedHandler inner)
    {
        var nav = new TestNavigationManager();
        var handler = new CustomAuthorizationMessageHandler(
            tokenStore.Object,
            renewal.Object,
            nav,
            NullLogger<CustomAuthorizationMessageHandler>.Instance)
        {
            InnerHandler = inner
        };
        return (new HttpMessageInvoker(handler), nav);
    }

    private static async Task<HttpResponseMessage> SendRequestAsync(HttpMessageInvoker invoker)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://test.local/api/events");
        return await invoker.SendAsync(request, CancellationToken.None);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public List<string> Navigations { get; } = new();

        public TestNavigationManager() => Initialize("https://kiosk.test/", "https://kiosk.test/");

        protected override void NavigateToCore(string uri, bool forceLoad) => Navigations.Add(uri);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _responses;

        public List<string?> AuthorizationHeaders { get; } = new();

        public ScriptedHandler(params HttpStatusCode[] responses) => _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());
            var status = _responses.Count > 0 ? _responses.Dequeue() : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request });
        }
    }

    #endregion
}
