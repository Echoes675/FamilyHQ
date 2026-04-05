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
    // ── GET /oauth2/auth ──────────────────────────────────────────────────────

    [Fact]
    public async Task AuthPrompt_WithUsersInDb_ReturnsHtmlContainingUserOptions()
    {
        // Arrange
        using var db = CreateDb();
        db.Users.AddRange(
            new SimulatedUser { Id = "user-a", Username = "Alice" },
            new SimulatedUser { Id = "user-b", Username = "Bob" });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.AuthPrompt("https://api/callback", "client-id");

        // Assert
        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("text/html");
        content.Content.Should().Contain("Alice");
        content.Content.Should().Contain("Bob");
        content.Content.Should().Contain("https://api/callback");
    }

    [Fact]
    public async Task AuthPrompt_WithNoUsers_ReturnsHtmlWithEmptyDropdown()
    {
        // Arrange
        using var db = CreateDb();
        var sut = CreateSut(db);

        // Act
        var result = await sut.AuthPrompt("https://api/callback", "client-id");

        // Assert
        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("text/html");
        content.Content.Should().Contain("<select");
    }

    [Fact]
    public async Task AuthPrompt_WithPathBase_IncludesPathBaseInFormAction()
    {
        // Arrange
        using var db = CreateDb();
        db.Users.Add(new SimulatedUser { Id = "user-a", Username = "Alice" });
        await db.SaveChangesAsync();

        var sut = CreateSut(db, pathBase: "/simulator");

        // Act
        var result = await sut.AuthPrompt("https://api/callback", "client-id");

        // Assert
        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Contain("action=\"/simulator/oauth2/auth/consent\"");
    }

    // ── POST /oauth2/auth/consent ─────────────────────────────────────────────

    [Fact]
    public async Task Consent_WithKnownUser_RedirectsWithDummyCode()
    {
        // Arrange
        using var db = CreateDb();
        db.Users.Add(new SimulatedUser { Id = "user-a", Username = "Alice" });
        await db.SaveChangesAsync();

        var sut = CreateSut(db);

        // Act
        var result = await sut.Consent("user-a", "https://api/callback");

        // Assert
        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be("https://api/callback?code=dummy_code_for_user-a");
    }

    [Fact]
    public async Task Consent_WithUnknownUser_ReturnsBadRequest()
    {
        // Arrange
        using var db = CreateDb();
        var sut = CreateSut(db);

        // Act
        var result = await sut.Consent("nonexistent-user", "https://api/callback");

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── POST /token ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Token_WithUserSpecificCode_EmbeddsUserIdInAccessToken()
    {
        // Arrange
        using var db = CreateDb();
        var sut = CreateSutWithFormData(db, formData: new Dictionary<string, string>
        {
            ["code"] = "dummy_code_for_alice"
        });

        // Act
        var result = await sut.Token();

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("simulated_alice_");
        json.Should().NotContain("user_id");
        var idToken = ExtractIdTokenSub(json);
        idToken.Should().Be("alice");
        ExtractIdTokenEmail(json).Should().Be("alice");
    }

    [Fact]
    public async Task Token_WithUnrecognizedCode_UsesDefaultUserId()
    {
        // Arrange
        using var db = CreateDb();
        var sut = CreateSutWithFormData(db, formData: new Dictionary<string, string>
        {
            ["code"] = "some_other_code"
        });

        // Act
        var result = await sut.Token();

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("simulated_default_simulator_user_");
        json.Should().NotContain("user_id");
        var idToken = ExtractIdTokenSub(json);
        idToken.Should().Be("default_simulator_user");
        ExtractIdTokenEmail(json).Should().Be("default_simulator_user");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Decodes the id_token from a serialised token response and returns the sub claim.</summary>
    private static string? ExtractIdTokenSub(string responseJson)
        => ExtractIdTokenClaim(responseJson, "sub");

    /// <summary>Decodes the id_token from a serialised token response and returns the email claim.</summary>
    private static string? ExtractIdTokenEmail(string responseJson)
        => ExtractIdTokenClaim(responseJson, "email");

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
        return payloadDoc.RootElement.TryGetProperty(claimName, out var claim) ? claim.GetString() : null;
    }

    private static SimContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SimContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimContext(options);
    }

    private static IConfiguration CreateConfiguration(string pathBase = "")
    {
        var settings = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(pathBase))
            settings["PathBase"] = pathBase;

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    private static OAuthController CreateSut(SimContext db, string pathBase = "")
    {
        var controller = new OAuthController(db, CreateConfiguration(pathBase));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private static OAuthController CreateSutWithFormData(SimContext db, Dictionary<string, string> formData, string pathBase = "")
    {
        var controller = new OAuthController(db, CreateConfiguration(pathBase));

        var formCollection = new FormCollection(
            formData.ToDictionary(kv => kv.Key, kv => new Microsoft.Extensions.Primitives.StringValues(kv.Value)));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Form = formCollection;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return controller;
    }
}
