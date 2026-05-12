using FamilyHQ.WebApi.Auth;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Auth;

public class JwtIssuerTests
{
    private const string TestSigningKey = "SuperSecretDummyKeyForFamilyHqSimulatorMVF1";

    [Fact]
    public void Issue_WithUserIdOnly_ReturnsTokenContainingSubAndUniqueNameClaims()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var (token, expiresAt) = sut.Issue("user-123");

        // Assert
        token.Should().NotBeNullOrEmpty();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == "user-123");
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.UniqueName && c.Value == "user-123");
        jwt.Claims.Should().NotContain(c => c.Type == JwtRegisteredClaimNames.Name);
        expiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Issue_WithEmail_AddsNameClaim()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var (token, _) = sut.Issue("user-123", "user@example.com");

        // Assert
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Name && c.Value == "user@example.com");
    }

    [Fact]
    public void Issue_WhenSigningKeyMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();
        var sut = new JwtIssuer(configuration);

        // Act
        var act = () => sut.Issue("user-123");

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*JWT signing key*");
    }

    [Fact]
    public void Issue_TokenExpiresApproximately24HoursFromNow()
    {
        // Arrange
        var sut = CreateSut();
        var before = DateTimeOffset.UtcNow;

        // Act
        var (_, expiresAt) = sut.Issue("user-123");

        // Assert
        var after = DateTimeOffset.UtcNow;
        expiresAt.Should().BeOnOrAfter(before.AddDays(1).AddSeconds(-5));
        expiresAt.Should().BeOnOrBefore(after.AddDays(1).AddSeconds(5));
    }

    private static JwtIssuer CreateSut()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new List<KeyValuePair<string, string?>>
            {
                new("Jwt:SigningKey", TestSigningKey),
            })
            .Build();

        return new JwtIssuer(configuration);
    }
}
