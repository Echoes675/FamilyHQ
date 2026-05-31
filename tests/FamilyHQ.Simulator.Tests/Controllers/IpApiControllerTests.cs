using System.Text.Json;
using FamilyHQ.Simulator.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FamilyHQ.Simulator.Tests.Controllers;

public class IpApiControllerTests
{
    // FHQ-43: the WebApi's geolocation auto-detect calls this mock in dev/staging and reads the
    // `timezone` field to resolve a user's IANA zone when none is configured. The response is a
    // fixed constant (Edinburgh / Europe/London) — no shared mutable state so parallel E2E
    // scenarios cannot race on this endpoint.

    [Fact]
    public void Get_ReturnsEdinburghEuropeLondonDefault()
    {
        var sut = new IpApiController();

        var result = sut.Get();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        root.GetProperty("city").GetString().Should().Be("Edinburgh");
        root.GetProperty("timezone").GetString().Should().Be("Europe/London");
        root.GetProperty("lat").GetDouble().Should().Be(55.9533);
        root.GetProperty("lon").GetDouble().Should().Be(-3.1883);
    }
}
