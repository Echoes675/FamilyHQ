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
        version.Should().MatchRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$");
    }

    // FHQ-103: the endpoint is anonymous, so the git SHA MinVer appends to
    // AssemblyInformationalVersion must not reach the caller — it fingerprints the exact
    // deployed commit.

    [Fact]
    public void Get_Response_VersionFieldCarriesNoBuildMetadata()
    {
        var sut = CreateSut();

        var result = sut.Get();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var doc = SerializeResponse(ok);
        doc.RootElement.GetProperty("version").GetString().Should().NotContain("+");
    }

    [Theory]
    [InlineData("1.1.0+abc1234", "1.1.0")]
    [InlineData("1.1.0", "1.1.0")]
    [InlineData("1.1.0+a+b", "1.1.0")]
    public void StripBuildMetadata_WithOrWithoutMetadata_ReturnsVersionWithoutIt(
        string informationalVersion, string expected)
    {
        HealthController.StripBuildMetadata(informationalVersion).Should().Be(expected);
    }

    [Fact]
    public void StripBuildMetadata_WithPreReleaseLabel_KeepsThePreRelease()
    {
        // The WASM client compares this value against its own baked-in version and strips build
        // metadata only (VersionService.VersionsMatch). Dropping the pre-release here too would
        // make every untagged build compare unequal forever — a reload loop on every kiosk.
        HealthController.StripBuildMetadata("1.1.0-alpha.0.5+abc1234").Should().Be("1.1.0-alpha.0.5");
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
