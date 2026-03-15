using FamilyHQ.Simulator.Controllers;
using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        json.Should().Contain("\"user_id\":\"alice\"");
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
        json.Should().Contain("\"user_id\":\"default_simulator_user\"");
        json.Should().Contain("simulated_default_simulator_user_");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SimContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SimContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimContext(options);
    }

    private static OAuthController CreateSut(SimContext db)
    {
        var controller = new OAuthController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private static OAuthController CreateSutWithFormData(SimContext db, Dictionary<string, string> formData)
    {
        var controller = new OAuthController(db);

        var formCollection = new FormCollection(
            formData.ToDictionary(kv => kv.Key, kv => new Microsoft.Extensions.Primitives.StringValues(kv.Value)));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Form = formCollection;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return controller;
    }
}
