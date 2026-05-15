using System.Net;
using FluentAssertions;
using FamilyHQ.WebUi.Services;
using Moq;
using Moq.Protected;

namespace FamilyHQ.WebUi.Tests.Services;

public class DiagnosticsApiServiceTests
{
    [Fact]
    public async Task GetConnectionStatusAsync_HappyPath_ParsesPayloadIncludingCalendars()
    {
        const string json = """
        {
          "status": "needs_reauth",
          "lastError": "Token has been expired or revoked.",
          "since": "2026-05-13T18:34:00+00:00",
          "calendars": [
            { "calendarId": "11111111-1111-1111-1111-111111111111", "displayName": "Family", "lastSyncedAt": "2026-05-13T18:00:00+00:00" },
            { "calendarId": "22222222-2222-2222-2222-222222222222", "displayName": "Work",   "lastSyncedAt": null }
          ]
        }
        """;
        var sut = CreateSut(HttpStatusCode.OK, json);

        var result = await sut.GetConnectionStatusAsync(CancellationToken.None);

        result.Loaded.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Status.Should().Be("needs_reauth");
        result.Data.LastError.Should().Be("Token has been expired or revoked.");
        result.Data.Since.Should().Be(new DateTimeOffset(2026, 5, 13, 18, 34, 0, TimeSpan.Zero));
        result.Data.Calendars.Should().HaveCount(2);
        result.Data.Calendars[0].DisplayName.Should().Be("Family");
        result.Data.Calendars[0].LastSyncedAt.Should().Be(new DateTimeOffset(2026, 5, 13, 18, 0, 0, TimeSpan.Zero));
        result.Data.Calendars[1].LastSyncedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetConnectionStatusAsync_OnUnauthorized_ReturnsFailedResult()
    {
        var sut = CreateSut(HttpStatusCode.Unauthorized, "");

        var result = await sut.GetConnectionStatusAsync(CancellationToken.None);

        result.Loaded.Should().BeFalse();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetConnectionStatusAsync_OnHttpRequestException_ReturnsFailedResult()
    {
        var sut = CreateSutThatThrows(new HttpRequestException("network down"));

        var result = await sut.GetConnectionStatusAsync(CancellationToken.None);

        result.Loaded.Should().BeFalse();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetSyncFailuresAsync_HappyPath_ParsesList()
    {
        const string json = """
        [
          {
            "id": "33333333-3333-3333-3333-333333333333",
            "calendarInfoId": "11111111-1111-1111-1111-111111111111",
            "googleEventId": "gid-1",
            "eventTitle": "Soccer practice",
            "failureReason": "Invalid recurrence rule",
            "exceptionType": "FormatException",
            "failedAt": "2026-05-13T10:00:00+00:00",
            "resolved": false
          }
        ]
        """;
        var sut = CreateSut(HttpStatusCode.OK, json);

        var result = await sut.GetSyncFailuresAsync(100, CancellationToken.None);

        result.Loaded.Should().BeTrue();
        result.Data.Should().NotBeNull().And.HaveCount(1);
        result.Data![0].GoogleEventId.Should().Be("gid-1");
        result.Data[0].EventTitle.Should().Be("Soccer practice");
        result.Data[0].FailureReason.Should().Be("Invalid recurrence rule");
        result.Data[0].ExceptionType.Should().Be("FormatException");
    }

    [Fact]
    public async Task GetSyncFailuresAsync_OnFailure_ReturnsFailedResult()
    {
        var sut = CreateSut(HttpStatusCode.InternalServerError, "");

        var result = await sut.GetSyncFailuresAsync(100, CancellationToken.None);

        result.Loaded.Should().BeFalse();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetSyncFailuresAsync_OnHttpRequestException_ReturnsFailedResult()
    {
        var sut = CreateSutThatThrows(new HttpRequestException("network down"));

        var result = await sut.GetSyncFailuresAsync(100, CancellationToken.None);

        result.Loaded.Should().BeFalse();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetSyncFailuresAsync_PassesLimitAsQueryParameter()
    {
        HttpRequestMessage? capturedRequest = null;

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            });

        var sut = new DiagnosticsApiService(new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://test.local/")
        });

        await sut.GetSyncFailuresAsync(42, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.PathAndQuery.Should().Be("/api/diagnostics/sync-failures?limit=42");
    }

    private static DiagnosticsApiService CreateSut(HttpStatusCode status, string body)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = status,
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });

        return new DiagnosticsApiService(new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://test.local/")
        });
    }

    private static DiagnosticsApiService CreateSutThatThrows(Exception ex)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(ex);

        return new DiagnosticsApiService(new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://test.local/")
        });
    }
}
