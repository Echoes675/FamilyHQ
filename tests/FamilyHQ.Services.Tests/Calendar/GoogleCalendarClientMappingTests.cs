using System.Net;
using System.Text.Json;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using FamilyHQ.Core.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class GoogleCalendarClientMappingTests
{
    [Fact]
    public async Task GetEventAsync_MapsOrganizerAndAttendeesToGoogleEventDetail()
    {
        // Arrange
        var (http, tokenStore, sut) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");
        SetupAuthResponse(http);

        var json = JsonSerializer.Serialize(new
        {
            id = "evt-123",
            status = "confirmed",
            summary = "Team Meeting",
            start = new { dateTime = "2026-03-01T10:00:00Z" },
            end   = new { dateTime = "2026-03-01T11:00:00Z" },
            organizer = new { email = "org@calendar.google.com", self = true },
            attendees = new[] {
                new { email = "att1@calendar.google.com", responseStatus = "accepted" },
                new { email = "att2@calendar.google.com", responseStatus = "accepted" }
            }
        });

        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("events/evt-123")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json)
            });

        // Act
        var result = await sut.GetEventAsync("org@calendar.google.com", "evt-123", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("evt-123");
        result.OrganizerEmail.Should().Be("org@calendar.google.com");
        result.AttendeeEmails.Should().BeEquivalentTo(new[] { "att1@calendar.google.com", "att2@calendar.google.com" });
    }

    [Fact]
    public async Task GetEventAsync_ReturnsNullOn404()
    {
        // Arrange
        var (http, tokenStore, sut) = CreateSut();
        tokenStore.Setup(s => s.GetRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-refresh-token");
        SetupAuthResponse(http);

        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("events/no-such")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.NotFound });

        // Act
        var result = await sut.GetEventAsync("org@calendar.google.com", "no-such", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    private static void SetupAuthResponse(Mock<HttpMessageHandler> http)
    {
        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("auth.test.com")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                    { access_token = "new-access", expires_in = 3600, token_type = "Bearer" }))
            });
    }

    private static (Mock<HttpMessageHandler> HttpMock, Mock<ITokenStore> TokenMock, GoogleCalendarClient Sut) CreateSut()
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
        var authService = new GoogleAuthService(httpClient, options, new Mock<ILogger<GoogleAuthService>>().Object);
        var sut = new GoogleCalendarClient(
            httpClient, authService,
            tokenStoreMock.Object,
            new Mock<IAccessTokenProvider>().Object,
            options,
            new Mock<ILogger<GoogleCalendarClient>>().Object);
        return (httpMessageHandlerMock, tokenStoreMock, sut);
    }
}
