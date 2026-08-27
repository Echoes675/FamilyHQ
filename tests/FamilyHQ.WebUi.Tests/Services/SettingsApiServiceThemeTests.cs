using System.Net;
using FamilyHQ.WebUi.Services;
using FluentAssertions;
using Moq;
using Moq.Protected;

namespace FamilyHQ.WebUi.Tests.Services;

/// <summary>
/// FHQ-177: <c>GET /api/daytheme/today</c> answers <c>204 No Content</c> for a kiosk with no saved
/// location. These pin the client's handling of that, because getting it wrong is not a cosmetic
/// failure: the settings display tab calls <c>GetTodayThemeAsync</c> unguarded, so a throw here took
/// the whole component down and no theme tiles rendered at all — leaving a kiosk with auto-theme
/// switched off unable to pick a theme manually, since the tiles it needs to tap were gone.
/// </summary>
public class SettingsApiServiceThemeTests
{
    [Fact]
    public async Task GetTodayThemeAsync_ReturnsNull_WhenTheKioskHasNoSavedLocation()
    {
        // A 204 carries an empty body, and GetFromJsonAsync throws on that rather than returning
        // null — which is exactly the trap this guards.
        var sut = CreateSut(HttpStatusCode.NoContent, "");

        var result = await sut.GetTodayThemeAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTodayThemeAsync_DoesNotThrow_OnTheEmptyBodyOfA204()
    {
        var sut = CreateSut(HttpStatusCode.NoContent, "");

        await sut.Invoking(s => s.GetTodayThemeAsync()).Should().NotThrowAsync(
            "the display tab's call is unguarded, so a throw here blanks the whole settings tab");
    }

    [Fact]
    public async Task GetTodayThemeAsync_ParsesTheBoundaries_WhenAThemeExists()
    {
        const string json = """
        {
          "date": "2026-08-27",
          "morningStart": "05:51:25",
          "daytimeStart": "06:25:16",
          "eveningStart": "19:12:22",
          "nightStart": "20:46:13",
          "ianaTimeZone": "Europe/Dublin",
          "currentPeriod": "Evening"
        }
        """;
        var sut = CreateSut(HttpStatusCode.OK, json);

        var result = await sut.GetTodayThemeAsync();

        result.Should().NotBeNull();
        result!.NightStart.Should().Be(new TimeOnly(20, 46, 13));
        result.IanaTimeZone.Should().Be("Europe/Dublin");
        result.CurrentPeriod.Should().Be("Evening");
    }

    [Fact]
    public async Task GetTodayThemeAsync_Throws_OnAFailureStatus()
    {
        // 204 is the only status that means "no theme". A 401 or 500 is a real failure and must not
        // be laundered into a silent null, which would look identical to "no location configured".
        var sut = CreateSut(HttpStatusCode.Unauthorized, "");

        await sut.Invoking(s => s.GetTodayThemeAsync()).Should().ThrowAsync<HttpRequestException>();
    }

    private static SettingsApiService CreateSut(HttpStatusCode status, string body)
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

        return new SettingsApiService(new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://test.local/")
        });
    }
}
