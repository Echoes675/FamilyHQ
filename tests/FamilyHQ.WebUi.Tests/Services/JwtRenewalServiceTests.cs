using System.Net;
using System.Text;
using FamilyHQ.WebUi.Configuration;
using FamilyHQ.WebUi.Services.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FamilyHQ.WebUi.Tests.Services;

public class JwtRenewalServiceTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);
    private const string RenewedToken = "renewed.jwt.token";
    private const string RenewalSuccessJson = $$"""{"token":"{{RenewedToken}}"}""";

    #region Options validation (fail fast)

    [Fact]
    public void Constructor_WhenThresholdNotPositive_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new JwtRenewalOptions { RenewalThresholdDays = 0 };

        // Act
        var act = () => CreateSut(CreateTokenStoreMock(null), options: options);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*RenewalThresholdDays*");
    }

    [Fact]
    public void Constructor_WhenCheckIntervalNotPositive_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new JwtRenewalOptions { CheckInterval = TimeSpan.Zero };

        // Act
        var act = () => CreateSut(CreateTokenStoreMock(null), options: options);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*CheckInterval*");
    }

    #endregion

    #region CheckAndRenewAsync — decision logic

    [Fact]
    public async Task CheckAndRenewAsync_WhenNoStoredToken_DoesNotCallApi()
    {
        // Arrange
        var tokenStore = CreateTokenStoreMock(storedToken: null);
        var handler = CreateSuccessHandler();
        var sut = CreateSut(tokenStore, handler);

        // Act
        var renewed = await sut.CheckAndRenewAsync();

        // Assert
        renewed.Should().BeFalse();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CheckAndRenewAsync_WhenRemainingLifetimeAboveThreshold_DoesNotRenew()
    {
        // Arrange — 360 days remaining, threshold 358 → still fresh enough
        var storedToken = CreateToken(TestNow.AddDays(360).ToUnixTimeSeconds());
        var tokenStore = CreateTokenStoreMock(storedToken);
        var handler = CreateSuccessHandler();
        var sut = CreateSut(tokenStore, handler);

        // Act
        var renewed = await sut.CheckAndRenewAsync();

        // Assert
        renewed.Should().BeFalse();
        handler.CallCount.Should().Be(0);
        tokenStore.Verify(s => s.SetTokenAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndRenewAsync_WhenRemainingLifetimeBelowThreshold_RenewsAndStoresNewToken()
    {
        // Arrange — 357 days remaining, threshold 358 → renew
        var storedToken = CreateToken(TestNow.AddDays(357).ToUnixTimeSeconds());
        var tokenStore = CreateTokenStoreMock(storedToken);
        var handler = CreateSuccessHandler();
        var sut = CreateSut(tokenStore, handler);

        // Act
        var renewed = await sut.CheckAndRenewAsync();

        // Assert
        renewed.Should().BeTrue();
        handler.CallCount.Should().Be(1);
        tokenStore.Verify(s => s.SetTokenAsync(RenewedToken), Times.Once);
    }

    [Fact]
    public async Task CheckAndRenewAsync_WhenExpMissing_TreatsAsExpiringAndRenews()
    {
        // Arrange
        var storedToken = CreateToken(expUnixSeconds: null);
        var tokenStore = CreateTokenStoreMock(storedToken);
        var handler = CreateSuccessHandler();
        var sut = CreateSut(tokenStore, handler);

        // Act
        var renewed = await sut.CheckAndRenewAsync();

        // Assert
        renewed.Should().BeTrue();
        handler.CallCount.Should().Be(1);
        tokenStore.Verify(s => s.SetTokenAsync(RenewedToken), Times.Once);
    }

    [Fact]
    public async Task CheckAndRenewAsync_WhenRenewalRequestThrows_KeepsOldTokenAndReturnsFalse()
    {
        // Arrange
        var storedToken = CreateToken(TestNow.AddDays(1).ToUnixTimeSeconds());
        var tokenStore = CreateTokenStoreMock(storedToken);
        var handler = FakeHttpMessageHandler.Throwing(new HttpRequestException("network down"));
        var sut = CreateSut(tokenStore, handler);

        // Act
        var renewed = await sut.CheckAndRenewAsync();

        // Assert — never sign out on a failed renewal; the old token still works
        renewed.Should().BeFalse();
        tokenStore.Verify(s => s.SetTokenAsync(It.IsAny<string>()), Times.Never);
        tokenStore.Verify(s => s.ClearTokenAsync(), Times.Never);
    }

    #endregion

    #region RenewNowAsync — HTTP behaviour

    [Fact]
    public async Task RenewNowAsync_SendsBearerAuthorizationHeaderWithStoredToken()
    {
        // Arrange
        var storedToken = CreateToken(TestNow.AddDays(300).ToUnixTimeSeconds());
        var tokenStore = CreateTokenStoreMock(storedToken);
        var handler = CreateSuccessHandler();
        var sut = CreateSut(tokenStore, handler);

        // Act
        await sut.RenewNowAsync();

        // Assert
        handler.AuthorizationHeaders.Should().ContainSingle().Which.Should().Be($"Bearer {storedToken}");
    }

    [Fact]
    public async Task RenewNowAsync_OnSuccess_StoresAndReturnsNewToken()
    {
        // Arrange
        var storedToken = CreateToken(TestNow.AddDays(300).ToUnixTimeSeconds());
        var tokenStore = CreateTokenStoreMock(storedToken);
        var sut = CreateSut(tokenStore, CreateSuccessHandler());

        // Act
        var result = await sut.RenewNowAsync();

        // Assert
        result.Should().Be(RenewedToken);
        tokenStore.Verify(s => s.SetTokenAsync(RenewedToken), Times.Once);
    }

    [Fact]
    public async Task RenewNowAsync_On401_ReturnsNullAndKeepsOldToken()
    {
        // Arrange
        var storedToken = CreateToken(TestNow.AddDays(300).ToUnixTimeSeconds());
        var tokenStore = CreateTokenStoreMock(storedToken);
        var handler = FakeHttpMessageHandler.RespondingWith(HttpStatusCode.Unauthorized, "");
        var sut = CreateSut(tokenStore, handler);

        // Act
        var result = await sut.RenewNowAsync();

        // Assert
        result.Should().BeNull();
        tokenStore.Verify(s => s.SetTokenAsync(It.IsAny<string>()), Times.Never);
        tokenStore.Verify(s => s.ClearTokenAsync(), Times.Never);
    }

    [Fact]
    public async Task RenewNowAsync_WhenResponseTokenEmpty_ReturnsNullAndDoesNotStore()
    {
        // Arrange
        var storedToken = CreateToken(TestNow.AddDays(300).ToUnixTimeSeconds());
        var tokenStore = CreateTokenStoreMock(storedToken);
        var handler = FakeHttpMessageHandler.RespondingWith(HttpStatusCode.OK, """{"token":""}""");
        var sut = CreateSut(tokenStore, handler);

        // Act
        var result = await sut.RenewNowAsync();

        // Assert
        result.Should().BeNull();
        tokenStore.Verify(s => s.SetTokenAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RenewNowAsync_WhenNoStoredToken_ReturnsNullWithoutCallingApi()
    {
        // Arrange
        var tokenStore = CreateTokenStoreMock(storedToken: null);
        var handler = CreateSuccessHandler();
        var sut = CreateSut(tokenStore, handler);

        // Act
        var result = await sut.RenewNowAsync();

        // Assert
        result.Should().BeNull();
        handler.CallCount.Should().Be(0);
    }

    #endregion

    #region InitializeAsync — startup check + daily loop

    [Fact]
    public async Task InitializeAsync_RunsAnImmediateCheck()
    {
        // Arrange — token below threshold so the startup check renews
        var storedToken = CreateToken(TestNow.AddDays(10).ToUnixTimeSeconds());
        var tokenStore = CreateTokenStoreMock(storedToken);
        var handler = CreateSuccessHandler();
        await using var sut = CreateSut(tokenStore, handler);

        // Act
        await sut.InitializeAsync();

        // Assert
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task InitializeAsync_ThenTimerTick_RunsAnotherCheck()
    {
        // Arrange
        var storedToken = CreateToken(TestNow.AddDays(10).ToUnixTimeSeconds());
        var tokenStore = CreateTokenStoreMock(storedToken);
        var handler = CreateSuccessHandler();
        var fakeTime = new FakeTimeProvider(TestNow);
        await using var sut = CreateSut(tokenStore, handler, timeProvider: fakeTime);
        await sut.InitializeAsync();

        // Act — advance past the daily interval so the PeriodicTimer ticks
        fakeTime.Advance(TimeSpan.FromDays(1));

        // Assert — bounded wait for the tick continuation to run
        for (var i = 0; i < 200 && handler.CallCount < 2; i++)
        {
            await Task.Delay(10);
        }
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task InitializeAsync_WhenStartupCheckThrows_DoesNotPropagate()
    {
        // Arrange — e.g. a JSException surfacing from localStorage must never fail app boot
        var tokenStore = new Mock<IAuthTokenStore>();
        tokenStore.Setup(s => s.GetTokenAsync())
            .ThrowsAsync(new InvalidOperationException("localStorage unavailable"));
        await using var sut = CreateSut(tokenStore);

        // Act
        var act = () => sut.InitializeAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LoopTick_WhenCheckThrows_LoopKeepsTicking()
    {
        // Arrange — every check throws; the loop must log and keep ticking, not die
        var calls = 0;
        var tokenStore = new Mock<IAuthTokenStore>();
        tokenStore.Setup(s => s.GetTokenAsync())
            .Callback(() => calls++)
            .ThrowsAsync(new InvalidOperationException("localStorage unavailable"));
        var handler = CreateSuccessHandler();
        var fakeTime = new FakeTimeProvider(TestNow);
        await using var sut = CreateSut(tokenStore, handler, timeProvider: fakeTime);
        await sut.InitializeAsync(); // startup check = call 1 (throws, swallowed)

        // Act — first tick (throws, must be caught) then a second tick (must still happen)
        fakeTime.Advance(TimeSpan.FromDays(1));
        for (var i = 0; i < 200 && calls < 2; i++)
        {
            await Task.Delay(10);
        }
        calls.Should().Be(2, "the first tick should have run its check");

        fakeTime.Advance(TimeSpan.FromDays(1));
        for (var i = 0; i < 200 && calls < 3; i++)
        {
            await Task.Delay(10);
        }

        // Assert — the loop survived the first tick's exception
        calls.Should().Be(3);
    }

    [Fact]
    public async Task DisposeAsync_AfterFailingTicks_DoesNotThrow()
    {
        // Arrange
        var calls = 0;
        var tokenStore = new Mock<IAuthTokenStore>();
        tokenStore.Setup(s => s.GetTokenAsync())
            .Callback(() => calls++)
            .ThrowsAsync(new InvalidOperationException("localStorage unavailable"));
        var fakeTime = new FakeTimeProvider(TestNow);
        var sut = CreateSut(tokenStore, CreateSuccessHandler(), timeProvider: fakeTime);
        await sut.InitializeAsync();
        fakeTime.Advance(TimeSpan.FromDays(1));
        for (var i = 0; i < 200 && calls < 2; i++)
        {
            await Task.Delay(10);
        }

        // Act
        var act = () => sut.DisposeAsync().AsTask();

        // Assert — a faulted/erroring loop must never rethrow out of dispose
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Helpers

    private static Mock<IAuthTokenStore> CreateTokenStoreMock(string? storedToken)
    {
        var mock = new Mock<IAuthTokenStore>();
        mock.Setup(s => s.GetTokenAsync()).ReturnsAsync(storedToken);
        return mock;
    }

    private static FakeHttpMessageHandler CreateSuccessHandler()
        => FakeHttpMessageHandler.RespondingWith(HttpStatusCode.OK, RenewalSuccessJson);

    private static JwtRenewalService CreateSut(
        Mock<IAuthTokenStore> tokenStore,
        FakeHttpMessageHandler? handler = null,
        JwtRenewalOptions? options = null,
        FakeTimeProvider? timeProvider = null)
    {
        var httpClient = new HttpClient(handler ?? CreateSuccessHandler(), disposeHandler: false)
        {
            BaseAddress = new Uri("https://test.local/")
        };

        return new JwtRenewalService(
            httpClient,
            tokenStore.Object,
            options ?? new JwtRenewalOptions(),
            timeProvider ?? new FakeTimeProvider(TestNow),
            NullLogger<JwtRenewalService>.Instance);
    }

    private static string CreateToken(long? expUnixSeconds)
    {
        var payload = expUnixSeconds is null
            ? """{"sub":"user-123","name":"testuser"}"""
            : $$"""{"sub":"user-123","name":"testuser","exp":{{expUnixSeconds}}}""";
        var header = Base64UrlEncode("""{"alg":"HS256","typ":"JWT"}""");
        return $"{header}.{Base64UrlEncode(payload)}.c2ln";
    }

    private static string Base64UrlEncode(string input)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(input))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;
        private readonly Exception? _throwException;

        public int CallCount { get; private set; }
        public List<string?> AuthorizationHeaders { get; } = new();

        private FakeHttpMessageHandler(HttpStatusCode status, string json, Exception? throwException)
        {
            _status = status;
            _json = json;
            _throwException = throwException;
        }

        public static FakeHttpMessageHandler RespondingWith(HttpStatusCode status, string json)
            => new(status, json, throwException: null);

        public static FakeHttpMessageHandler Throwing(Exception ex)
            => new(HttpStatusCode.OK, "{}", ex);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());

            if (_throwException is not null)
            {
                throw _throwException;
            }

            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }

    #endregion
}
