using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using FamilyHQ.WebApi.Auth;
using FamilyHQ.WebApi.Controllers;
using FamilyHQ.WebApi.Models;
using FamilyHQ.WebApi.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class IssueTokenEndpointTests
{
    private const string SeededUserId = "test-google-sub-123";
    private const string ConfiguredSecret = "very-secret-test-token";
    private const string TestSigningKey = "SuperSecretDummyKeyForFamilyHqSimulatorMVF1";

    [Fact]
    public async Task IssueToken_ValidSecret_AndKnownUserId_Returns200WithJwt()
    {
        // Arrange
        await using var db = CreateDb(seedUserToken: true);
        var sut = CreateSut(db, enabled: true);
        SetAuthorizationHeader(sut, $"Bearer {ConfiguredSecret}");

        // Act
        var result = await sut.IssueToken(new IssueTokenRequest(SeededUserId));

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<IssueTokenResponse>().Subject;
        body.Token.Should().NotBeNullOrEmpty();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.Token);
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == SeededUserId);
        body.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task IssueToken_ValidSecret_UnknownUserId_Returns404()
    {
        // Arrange
        await using var db = CreateDb(seedUserToken: true);
        var sut = CreateSut(db, enabled: true);
        SetAuthorizationHeader(sut, $"Bearer {ConfiguredSecret}");

        // Act
        var result = await sut.IssueToken(new IssueTokenRequest("unknown-sub"));

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task IssueToken_WrongSecret_Returns404()
    {
        // Arrange
        await using var db = CreateDb(seedUserToken: true);
        var sut = CreateSut(db, enabled: true);
        SetAuthorizationHeader(sut, "Bearer wrong-secret");

        // Act
        var result = await sut.IssueToken(new IssueTokenRequest(SeededUserId));

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task IssueToken_MissingSecret_Returns404()
    {
        // Arrange
        await using var db = CreateDb(seedUserToken: true);
        var sut = CreateSut(db, enabled: true);
        // No Authorization header set.

        // Act
        var result = await sut.IssueToken(new IssueTokenRequest(SeededUserId));

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task IssueToken_MissingUserId_Returns400()
    {
        // Arrange
        await using var db = CreateDb(seedUserToken: true);
        var sut = CreateSut(db, enabled: true);
        SetAuthorizationHeader(sut, $"Bearer {ConfiguredSecret}");

        // Act
        var result = await sut.IssueToken(new IssueTokenRequest(""));

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task IssueToken_EndpointDisabled_Returns404()
    {
        // Arrange
        await using var db = CreateDb(seedUserToken: true);
        var sut = CreateSut(db, enabled: false);
        SetAuthorizationHeader(sut, $"Bearer {ConfiguredSecret}");

        // Act
        var result = await sut.IssueToken(new IssueTokenRequest(SeededUserId));

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task IssueToken_NullRequestBody_Returns400()
    {
        // Arrange
        await using var db = CreateDb(seedUserToken: true);
        var sut = CreateSut(db, enabled: true);
        SetAuthorizationHeader(sut, $"Bearer {ConfiguredSecret}");

        // Act
        var result = await sut.IssueToken(null!);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // Regression test for the spec-compliance fix in this dispatch. Body validation must run
    // before authentication so callers with a malformed body get 400 regardless of whether
    // their bearer secret happens to be correct. Otherwise the response code becomes a clean
    // oracle for secret correctness: 400 ↔ secret correct, 404 ↔ secret wrong.
    [Fact]
    public async Task WrongSecret_WithMalformedBody_Returns400()
    {
        // Arrange
        await using var db = CreateDb(seedUserToken: true);
        var sut = CreateSut(db, enabled: true);
        SetAuthorizationHeader(sut, "Bearer definitely-not-the-secret");

        // Act
        var result = await sut.IssueToken(new IssueTokenRequest(""));

        // Assert
        // Body validation precedes auth, so an empty userId returns 400 even when the
        // caller's secret is wrong. This proves bad-body responses are publicly observable
        // (acceptable: malformed body is a structural error) while preserving secret
        // confidentiality (the secret-correct vs secret-wrong response is identical here).
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    private static void SetAuthorizationHeader(IssueTokenController controller, string headerValue)
    {
        controller.ControllerContext.HttpContext.Request.Headers["Authorization"] = headerValue;
    }

    private static FamilyHqDbContext CreateDb(bool seedUserToken)
    {
        var options = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseInMemoryDatabase(databaseName: $"issue-token-tests-{Guid.NewGuid()}")
            .Options;
        var db = new FamilyHqDbContext(options);

        if (seedUserToken)
        {
            db.UserTokens.Add(new UserToken
            {
                Id = Guid.NewGuid(),
                UserId = SeededUserId,
                Provider = "Google",
                RefreshToken = "encrypted-refresh-token-blob",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        }

        return db;
    }

    private static IssueTokenController CreateSut(FamilyHqDbContext db, bool enabled)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new IssueTokenEndpointOptions
        {
            Enabled = enabled,
            Secret = ConfiguredSecret,
        });

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new List<KeyValuePair<string, string?>>
            {
                new("Jwt:SigningKey", TestSigningKey),
            })
            .Build();

        var jwtIssuer = new JwtIssuer(configuration);

        var controller = new IssueTokenController(
            options,
            db,
            jwtIssuer,
            new Mock<ILogger<IssueTokenController>>().Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        return controller;
    }
}
