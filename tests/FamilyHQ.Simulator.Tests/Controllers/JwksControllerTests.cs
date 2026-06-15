using FamilyHQ.Simulator.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FamilyHQ.Simulator.Tests.Controllers;

public class JwksControllerTests
{
    [Fact]
    public void GetJwks_ReturnsJsonWebKeySetWithRsaPublicKey()
    {
        var controller = new JwksController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = controller.GetJwks();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var keys = doc.RootElement.GetProperty("keys");
        keys.GetArrayLength().Should().Be(1);
        var key = keys[0];
        key.GetProperty("kty").GetString().Should().Be("RSA");
        key.GetProperty("use").GetString().Should().Be("sig");
        key.GetProperty("alg").GetString().Should().Be("RS256");
        key.GetProperty("n").GetString().Should().NotBeNullOrEmpty("modulus must be present");
        key.GetProperty("e").GetString().Should().NotBeNullOrEmpty("exponent must be present");
    }

    [Fact]
    public void GetJwks_ReturnedPublicKeyMatchesSimulatorRsaKeyInstance()
    {
        var controller = new JwksController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = controller.GetJwks();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var key = doc.RootElement.GetProperty("keys")[0];

        // The returned modulus must match what we get directly from the RSA key
        var expectedParams = FamilyHQ.Simulator.Auth.SimulatorRsaKey.Instance.ExportParameters(false);
        var expectedN = Convert.ToBase64String(expectedParams.Modulus!)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        key.GetProperty("n").GetString().Should().Be(expectedN);
    }
}
