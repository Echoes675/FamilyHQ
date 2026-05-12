using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using FamilyHQ.WebApi.Auth;
using FamilyHQ.WebApi.Controllers;
using FamilyHQ.WebApi.Models;
using FamilyHQ.WebApi.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        var result = await sut.IssueToken(new IssueTokenRequest { UserId = SeededUserId });

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
        var result = await sut.IssueToken(new IssueTokenRequest { UserId = "unknown-sub" });

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
        var result = await sut.IssueToken(new IssueTokenRequest { UserId = SeededUserId });

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
        var result = await sut.IssueToken(new IssueTokenRequest { UserId = SeededUserId });

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
        var result = await sut.IssueToken(new IssueTokenRequest { UserId = "" });

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
        var result = await sut.IssueToken(new IssueTokenRequest { UserId = SeededUserId });

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

    private static void SetAuthorizationHeader(AuthController controller, string headerValue)
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

    private static AuthController CreateSut(FamilyHqDbContext db, bool enabled)
    {
        var options = Options.Create(new IssueTokenEndpointOptions
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

        // The remaining AuthController dependencies are unused by IssueToken.
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var googleOptions = Options.Create(new GoogleCalendarOptions
        {
            ClientId = "x",
            ClientSecret = "x",
            AuthPromptUrl = "https://sim.test/oauth2/auth",
            AuthBaseUrl = "https://sim.test",
        });
        var authService = new GoogleAuthService(
            new HttpClient(),
            googleOptions,
            new Mock<ILogger<GoogleAuthService>>().Object);

        var controller = new AuthController(
            authService,
            new Mock<ITokenStore>().Object,
            scopeFactoryMock.Object,
            configuration,
            Options.Create(new SyncOptions()),
            jwtIssuer,
            new Mock<ILogger<AuthController>>().Object,
            db,
            options)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        return controller;
    }
}
