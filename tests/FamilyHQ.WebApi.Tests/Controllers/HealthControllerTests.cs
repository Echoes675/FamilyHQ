using System.Text.Json;
using FamilyHQ.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class HealthControllerTests
{
    [Fact]
    public void Get_ReturnsOk_WithExistingStatusAndServiceFields()
    {
        var sut = CreateSut();

        var result = sut.Get();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var doc = SerializeResponse(ok);
        doc.RootElement.GetProperty("status").GetString().Should().Be("healthy");
        doc.RootElement.GetProperty("service").GetString().Should().Be("webapi");
    }

    [Fact]
    public void Get_Response_IncludesNonEmptyVersionField()
    {
        var sut = CreateSut();

        var result = sut.Get();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var doc = SerializeResponse(ok);
        doc.RootElement.TryGetProperty("version", out var version).Should().BeTrue();
        version.GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Get_Response_VersionFieldMatchesSemVerShape()
    {
        var sut = CreateSut();

        var result = sut.Get();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var doc = SerializeResponse(ok);
        var version = doc.RootElement.GetProperty("version").GetString();
        version.Should().MatchRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.+-]+)?(\+[0-9A-Za-z.-]+)?$");
    }

    [Fact]
    public void Get_SetsCacheControlNoStoreHeader()
    {
        var sut = CreateSut();

        sut.Get();

        sut.Response.Headers.CacheControl.ToString().Should().Contain("no-store");
    }

    private static HealthController CreateSut()
    {
        var sut = new HealthController();
        sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return sut;
    }

    private static JsonDocument SerializeResponse(OkObjectResult ok)
    {
        var json = JsonSerializer.Serialize(ok.Value);
        return JsonDocument.Parse(json);
    }
}
