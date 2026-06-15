using FamilyHQ.Simulator.Controllers;
using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FamilyHQ.Simulator.Tests.Controllers;

public class OAuthControllerTests
{
    // ── GET /oauth2/auth ────────────────────────────────────────────────────

    [Fact]
    public async Task AuthPrompt_WithUsersInDb_ReturnsHtmlContainingUserOptions()
    {
        using var db = CreateDb();
        db.Users.AddRange(
            new SimulatedUser { Id = "user-a", Username = "Alice" },
            new SimulatedUser { Id = "user-b", Username = "Bob" });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.AuthPrompt("https://api/callback", "client-id");

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("text/html");
        content.Content.Should().Contain("Alice");
        content.Content.Should().Contain("Bob");
        content.Content.Should().Contain("https://api/callback");
    }

    [Fact]
    public async Task AuthPrompt_WithNoUsers_ReturnsHtmlWithEmptyDropdown()
    {
        using var db = CreateDb();
        var sut = CreateSut(db);

        var result = await sut.AuthPrompt("https://api/callback", "client-id");

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("text/html");
        content.Content.Should().Contain("<select");
    }

    [Fact]
    public async Task AuthPrompt_WithPathBase_IncludesPathBaseInFormAction()
    {
        using var db = CreateDb();
        db.Users.Add(new SimulatedUser { Id = "user-a", Username = "Alice" });
        await db.SaveChangesAsync();
        var sut = CreateSut(db, pathBase: "/simulator");

        var result = await sut.AuthPrompt("https://api/callback", "client-id");

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Contain("action=\"/simulator/oauth2/auth/consent\"");
    }

    // ── POST /oauth2/auth/consent ───────────────────────────────────────────

    [Fact]
    public async Task Consent_WithKnownUser_RedirectsWithDummyCode()
    {
        using var db = CreateDb();
        db.Users.Add(new SimulatedUser { Id = "user-a", Username = "Alice" });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.Consent("user-a", "https://api/callback");

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be("https://api/callback?code=dummy_code_for_user-a");
    }

    [Fact]
    public async Task Consent_WithUnknownUser_ReturnsBadRequest()
    {
        using var db = CreateDb();
        var sut = CreateSut(db);

        var result = await sut.Consent("nonexistent-user", "https://api/callback");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── POST /token ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Token_WithUserSpecificCode_EmbeddsUserIdInAccessToken()
    {
        using var db = CreateDb();
        var sut = CreateSutWithFormData(db, formData: new Dictionary<string, string>
        {
            ["code"] = "dummy_code_for_alice",
            ["client_id"] = "test-client-id"
        });

        var result = await sut.Token();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("simulated_alice_");
        ExtractIdTokenClaim(json, "sub").Should().Be("alice");
        ExtractIdTokenClaim(json, "email").Should().Be("alice");
    }

    [Fact]
    public async Task Token_WithUnrecognizedCode_UsesDefaultUserId()
    {
        using var db = CreateDb();
        var sut = CreateSutWithFormData(db, formData: new Dictionary<string, string>
        {
            ["code"] = "some_other_code",
            ["client_id"] = "test-client-id"
        });

        var result = await sut.Token();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("simulated_default_simulator_user_");
        ExtractIdTokenClaim(json, "sub").Should().Be("default_simulator_user");
    }

    [Fact]
    public async Task Token_IdTokenContainsIssuerAudienceAndExpiry()
    {
        using var db = CreateDb();
        db.Users.Add(new SimulatedUser { Id = "alice", Username = "Alice" });
        await db.SaveChangesAsync();
        var sut = CreateSutWithFormData(db,
            formData: new Dictionary<string, string>
            {
                ["code"] = "dummy_code_for_alice",
                ["client_id"] = "my-client-id"
            },
            issuer: "https://sim.example");

        var result = await sut.Token();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        ExtractIdTokenClaim(json, "iss").Should().Be("https://sim.example");
        ExtractIdTokenClaim(json, "aud").Should().Be("my-client-id");
        ExtractIdTokenClaim(json, "exp").Should().NotBeNullOrEmpty();
        ExtractIdTokenClaim(json, "iat").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Token_IdTokenSignatureCanBeVerifiedWithSimulatorPublicKey()
    {
        using var db = CreateDb();
        db.Users.Add(new SimulatedUser { Id = "alice", Username = "Alice" });
        await db.SaveChangesAsync();
        var sut = CreateSutWithFormData(db,
            formData: new Dictionary<string, string>
            {
                ["code"] = "dummy_code_for_alice",
                ["client_id"] = "test-client-id"
            },
            issuer: "https://sim.example");

        var result = await sut.Token();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var idToken = doc.RootElement.GetProperty("id_token").GetString()!;

        // Verify the RS256 signature using the Simulator's own public key
        var parts = idToken.Split('.');
        parts.Length.Should().Be(3, "id_token must be a three-part JWT");
        var toVerify = System.Text.Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        var sig = Convert.FromBase64String(parts[2].Replace('-', '+').Replace('_', '/') +
            new string('=', (4 - parts[2].Length % 4) % 4));
        var valid = FamilyHQ.Simulator.Auth.SimulatorRsaKey.Instance.VerifyData(
            toVerify,
            sig,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        valid.Should().BeTrue("signature must be valid RS256 from the Simulator's RSA key");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string? ExtractIdTokenClaim(string responseJson, string claimName)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
        if (!doc.RootElement.TryGetProperty("id_token", out var idTokenEl)) return null;
        var parts = idTokenEl.GetString()?.Split('.');
        if (parts == null || parts.Length < 2) return null;
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        using var payloadDoc = System.Text.Json.JsonDocument.Parse(json);
        return payloadDoc.RootElement.TryGetProperty(claimName, out var claim) ? claim.ToString() : null;
    }

    private static SimContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SimContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimContext(options);
    }

    private static IConfiguration CreateConfiguration(string pathBase = "", string issuer = "https://sim.test")
    {
        var settings = new Dictionary<string, string?>
        {
            ["Simulator:Issuer"] = issuer
        };
        if (!string.IsNullOrEmpty(pathBase))
            settings["PathBase"] = pathBase;
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static OAuthController CreateSut(SimContext db, string pathBase = "", string issuer = "https://sim.test")
    {
        var controller = new OAuthController(db, CreateConfiguration(pathBase, issuer), new FamilyHQ.Simulator.State.SyncFailureModeStore());
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static OAuthController CreateSutWithFormData(SimContext db, Dictionary<string, string> formData,
        string pathBase = "", string issuer = "https://sim.test")
    {
        var controller = new OAuthController(db, CreateConfiguration(pathBase, issuer), new FamilyHQ.Simulator.State.SyncFailureModeStore());
        var formCollection = new FormCollection(
            formData.ToDictionary(kv => kv.Key, kv => new Microsoft.Extensions.Primitives.StringValues(kv.Value)));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Form = formCollection;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }
}
