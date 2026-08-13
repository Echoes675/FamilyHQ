using FamilyHQ.WebUi.Services.Auth;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Services;

public class JwtTokenDecoderTests
{
    [Fact]
    public void Decode_ValidToken_ReturnsUserIdUsernameAndExpiry()
    {
        // Arrange
        const long expUnixSeconds = 1900000000;
        var token = CreateToken($$"""{"sub":"user-123","name":"testuser","exp":{{expUnixSeconds}}}""");

        // Act
        var claims = JwtTokenDecoder.Decode(token);

        // Assert
        claims.UserId.Should().Be("user-123");
        claims.Username.Should().Be("testuser");
        claims.ExpiresAtUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(expUnixSeconds));
    }

    [Fact]
    public void Decode_TokenWithoutExp_ReturnsNullExpiry()
    {
        // Arrange
        var token = CreateToken("""{"sub":"user-123","name":"testuser"}""");

        // Act
        var claims = JwtTokenDecoder.Decode(token);

        // Assert
        claims.UserId.Should().Be("user-123");
        claims.ExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public void Decode_TokenWithNonNumericExp_ReturnsNullExpiry()
    {
        // Arrange
        var token = CreateToken("""{"sub":"user-123","exp":"garbled"}""");

        // Act
        var claims = JwtTokenDecoder.Decode(token);

        // Assert
        claims.ExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public void Decode_TokenWithUniqueNameOnly_FallsBackToUniqueName()
    {
        // Arrange
        var token = CreateToken("""{"sub":"user-123","unique_name":"user-123"}""");

        // Act
        var claims = JwtTokenDecoder.Decode(token);

        // Assert
        claims.Username.Should().Be("user-123");
    }

    [Fact]
    public void Decode_MalformedToken_ReturnsAllNulls()
    {
        // Act
        var claims = JwtTokenDecoder.Decode("not-a-jwt");

        // Assert
        claims.UserId.Should().BeNull();
        claims.Username.Should().BeNull();
        claims.ExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public void Decode_PayloadNotValidBase64_ReturnsAllNulls()
    {
        // Act
        var claims = JwtTokenDecoder.Decode("aGVhZGVy.###notbase64###.c2ln");

        // Assert
        claims.UserId.Should().BeNull();
        claims.Username.Should().BeNull();
        claims.ExpiresAtUtc.Should().BeNull();
    }

    private static string CreateToken(string payloadJson)
    {
        var header = Base64UrlEncode("""{"alg":"HS256","typ":"JWT"}""");
        return $"{header}.{Base64UrlEncode(payloadJson)}.c2ln";
    }

    private static string Base64UrlEncode(string input)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(input))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
