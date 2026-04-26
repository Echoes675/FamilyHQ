using System.Net;
using System.Text.Json;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class GoogleCalendarClientWatchTests
{
    [Fact]
    public async Task WatchEventsAsync_SendsCorrectRequest()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var (http, tokenStore, systemUnderTest) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");

        SetupAuthResponse(http);

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains("events/watch")),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedRequest = req;
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id = "channel-123",
                    resourceId = "resource-456",
                    expiration = 1700000000000L
                }))
            });

        // Act
        await systemUnderTest.WatchEventsAsync("test-calendar-id", "channel-123", "https://example.com/webhook");

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString()
            .Should().Be("https://calendar.test.com/calendars/test-calendar-id/events/watch");

        capturedBody.Should().NotBeNull();
        var bodyDoc = JsonDocument.Parse(capturedBody!);
        bodyDoc.RootElement.GetProperty("id").GetString().Should().Be("channel-123");
        bodyDoc.RootElement.GetProperty("type").GetString().Should().Be("web_hook");
        bodyDoc.RootElement.GetProperty("address").GetString().Should().Be("https://example.com/webhook");
    }

    [Fact]
    public async Task WatchEventsAsync_ReturnsWatchChannelResponse()
    {
        // Arrange
        var (http, tokenStore, systemUnderTest) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");

        SetupAuthResponse(http);

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains("events/watch")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id = "channel-abc",
                    resourceId = "resource-xyz",
                    expiration = 1700000000000L
                }))
            });

        // Act
        var result = await systemUnderTest.WatchEventsAsync("cal-1", "channel-abc", "https://example.com/hook");

        // Assert
        result.ChannelId.Should().Be("channel-abc");
        result.ResourceId.Should().Be("resource-xyz");
        result.Expiration.Should().Be(1700000000000L);
    }

    [Fact]
    public async Task StopChannelAsync_SendsCorrectRequest()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var (http, tokenStore, systemUnderTest) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");

        SetupAuthResponse(http);

        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains("channels/stop")),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedRequest = req;
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NoContent
            });

        // Act
        await systemUnderTest.StopChannelAsync("channel-123", "resource-456");

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString()
            .Should().Be("https://calendar.test.com/channels/stop");

        capturedBody.Should().NotBeNull();
        var bodyDoc = JsonDocument.Parse(capturedBody!);
        bodyDoc.RootElement.GetProperty("id").GetString().Should().Be("channel-123");
        bodyDoc.RootElement.GetProperty("resourceId").GetString().Should().Be("resource-456");
    }

    private static void SetupAuthResponse(Mock<HttpMessageHandler> http)
    {
        http.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("auth.test.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    access_token = "new-access",
                    expires_in = 3600,
                    token_type = "Bearer"
                }))
            });
    }

    private static (Mock<HttpMessageHandler> HttpMock, Mock<ITokenStore> TokenMock, GoogleCalendarClient systemUnderTest) CreateSut()
    {
        var httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(httpMessageHandlerMock.Object);

        var tokenStoreMock = new Mock<ITokenStore>();

        var options = Microsoft.Extensions.Options.Options.Create(new GoogleCalendarOptions
        {
            CalendarApiBaseUrl = "https://calendar.test.com",
            ClientId = "test-client",
            ClientSecret = "test-secret",
            AuthBaseUrl = "https://auth.test.com"
        });

        var authLoggerMock = new Mock<ILogger<GoogleAuthService>>();
        var authService = new GoogleAuthService(httpClient, options, authLoggerMock.Object);

        var loggerMock = new Mock<ILogger<GoogleCalendarClient>>();
        var accessTokenProviderMock = new Mock<IAccessTokenProvider>();

        var systemUnderTest = new GoogleCalendarClient(
            httpClient,
            authService,
            tokenStoreMock.Object,
            accessTokenProviderMock.Object,
            options,
            loggerMock.Object);

        return (httpMessageHandlerMock, tokenStoreMock, systemUnderTest);
    }
}
