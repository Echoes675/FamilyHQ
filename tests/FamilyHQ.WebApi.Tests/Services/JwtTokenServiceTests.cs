using FamilyHQ.WebApi.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Services;

public class JwtTokenServiceTests
{
    private const string TestSigningKey = "SuperSecretDummyKeyForFamilyHqSimulatorMVF1";

    [Fact]
    public void GenerateToken_WithEmail_IncludesSubUniqueNameAndNameClaims()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var token = sut.GenerateToken("user1", "user1@example.com");

        // Assert
        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        decoded.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == "user1");
        decoded.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.UniqueName && c.Value == "user1");
        decoded.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Name && c.Value == "user1@example.com");
    }

    [Fact]
    public void GenerateToken_WithoutEmail_OmitsNameClaim()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var token = sut.GenerateToken("user1", email: null);

        // Assert
        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        decoded.Claims.Should().NotContain(c => c.Type == JwtRegisteredClaimNames.Name);
    }

    [Fact]
    public void GenerateToken_UsesFamilyHqIssuerAndAudience()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var token = sut.GenerateToken("user1", "user1@example.com");

        // Assert — must remain verifiable by the JwtBearer middleware configured in Program.cs
        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        decoded.Issuer.Should().Be("FamilyHQ");
        decoded.Audiences.Should().ContainSingle(a => a == "FamilyHQ");
    }

    [Fact]
    public void GenerateToken_Expiry_Is365DaysFromNowUtc()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var token = sut.GenerateToken("user1", "user1@example.com");

        // Assert — same lifetime policy as the original AuthController mint (FHQ-126 scope guard)
        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        decoded.ValidTo.Kind.Should().Be(DateTimeKind.Utc);
        decoded.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddDays(365), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void GenerateToken_WhenSigningKeyMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut(signingKey: null);

        // Act
        var act = () => sut.GenerateToken("user1", "user1@example.com");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*signing key*");
    }

    private static JwtTokenService CreateSut(string? signingKey = TestSigningKey)
    {
        var configPairs = new List<KeyValuePair<string, string?>>();
        if (signingKey != null)
            configPairs.Add(new KeyValuePair<string, string?>("Jwt:SigningKey", signingKey));

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configPairs)
            .Build();

        return new JwtTokenService(configuration);
    }
}
