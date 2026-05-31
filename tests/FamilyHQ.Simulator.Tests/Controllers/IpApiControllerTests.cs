using System.Text.Json;
using FamilyHQ.Simulator.Controllers;
using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FamilyHQ.Simulator.Tests.Controllers;

public class IpApiControllerTests
{
    // FHQ-43: the WebApi's geolocation auto-detect calls this mock in dev/staging and reads the
    // `timezone` field to resolve a user's IANA zone when none is configured. The mock previously
    // returned no timezone, so auto-detect resolved to UTC.

    [Fact]
    public async Task Get_WithNoOverride_ReturnsEdinburghEuropeLondonDefaultIncludingTimezone()
    {
        using var db = CreateDb();
        var sut = new IpApiController(db);

        var result = await sut.Get(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("success");
        root.GetProperty("city").GetString().Should().Be("Edinburgh");
        root.GetProperty("timezone").GetString().Should().Be("Europe/London");
    }

    [Fact]
    public async Task Get_WithConfiguredOverride_ReturnsConfiguredTimezoneAndLocation()
    {
        using var db = CreateDb();
        db.IpApiResponses.Add(new SimulatedIpApiResponse
        {
            Id = 1,
            City = "New York",
            RegionName = "New York",
            Latitude = 40.7128,
            Longitude = -74.0060,
            Timezone = "America/New_York"
        });
        await db.SaveChangesAsync();

        var sut = new IpApiController(db);

        var result = await sut.Get(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = doc.RootElement;
        root.GetProperty("city").GetString().Should().Be("New York");
        root.GetProperty("timezone").GetString().Should().Be("America/New_York");
        root.GetProperty("lat").GetDouble().Should().Be(40.7128);
    }

    [Fact]
    public async Task SetThenClear_ConfiguresThenRevertsToDefault()
    {
        using var db = CreateDb();
        var backdoor = new BackdoorIpApiController(db);
        var ipApi = new IpApiController(db);

        await backdoor.SetResponse(
            new SetIpApiResponseRequest("Europe/Paris", "Paris", "Île-de-France", 48.8566, 2.3522),
            CancellationToken.None);

        var configured = await ReadTimezoneAsync(ipApi);
        configured.Should().Be("Europe/Paris");

        await backdoor.ClearResponse(CancellationToken.None);

        var reverted = await ReadTimezoneAsync(ipApi);
        reverted.Should().Be("Europe/London");
    }

    [Fact]
    public async Task Set_WithoutTimezone_ReturnsBadRequest()
    {
        using var db = CreateDb();
        var backdoor = new BackdoorIpApiController(db);

        var result = await backdoor.SetResponse(
            new SetIpApiResponseRequest("", null, null, null, null), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    private static async Task<string?> ReadTimezoneAsync(IpApiController sut)
    {
        var ok = (await sut.Get(CancellationToken.None)).Should().BeOfType<OkObjectResult>().Subject;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        return doc.RootElement.GetProperty("timezone").GetString();
    }

    private static SimContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SimContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimContext(options);
    }
}
