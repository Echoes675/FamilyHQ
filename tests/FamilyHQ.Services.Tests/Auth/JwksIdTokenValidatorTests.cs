using System.Net;
using System.Security.Cryptography;
using System.Text;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using FluentAssertions;
using Moq;
using Moq.Protected;

namespace FamilyHQ.Services.Tests.Auth;

public class JwksIdTokenValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WithValidToken_ReturnsSubAndEmail()
    {
        var (rsa, jwksJson) = CreateKeyPairAndJwks();
        var sut = CreateSut(jwksJson);
        var token = CreateSignedJwt(rsa, sub: "user-123", email: "user@example.com",
            issuer: "https://test-issuer", audience: "test-client-id");

        var result = await sut.ValidateAsync(token);

        result.Sub.Should().Be("user-123");
        result.Email.Should().Be("user@example.com");
    }

    [Fact]
    public async Task ValidateAsync_WithTamperedSignature_ThrowsIdTokenValidationException()
    {
        var (rsa, jwksJson) = CreateKeyPairAndJwks();
        var sut = CreateSut(jwksJson);
        var token = CreateSignedJwt(rsa, "user-123", "u@e.com", "https://test-issuer", "test-client-id");
        var parts = token.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.invalidsignatureXXX";

        await sut.Invoking(s => s.ValidateAsync(tampered))
            .Should().ThrowAsync<IdTokenValidationException>();
    }

    [Fact]
    public async Task ValidateAsync_WithExpiredToken_ThrowsIdTokenValidationException()
    {
        var (rsa, jwksJson) = CreateKeyPairAndJwks();
        var sut = CreateSut(jwksJson);
        var token = CreateSignedJwt(rsa, "user-123", "u@e.com", "https://test-issuer", "test-client-id",
            expiryOffsetMinutes: -1);

        await sut.Invoking(s => s.ValidateAsync(token))
            .Should().ThrowAsync<IdTokenValidationException>();
    }

    [Fact]
    public async Task ValidateAsync_WithWrongAudience_ThrowsIdTokenValidationException()
    {
        var (rsa, jwksJson) = CreateKeyPairAndJwks();
        var sut = CreateSut(jwksJson);
        var token = CreateSignedJwt(rsa, "user-123", "u@e.com", "https://test-issuer",
            audience: "wrong-client-id");

        await sut.Invoking(s => s.ValidateAsync(token))
            .Should().ThrowAsync<IdTokenValidationException>();
    }

    [Fact]
    public async Task ValidateAsync_WithWrongIssuer_ThrowsIdTokenValidationException()
    {
        var (rsa, jwksJson) = CreateKeyPairAndJwks();
        var sut = CreateSut(jwksJson);
        var token = CreateSignedJwt(rsa, "user-123", "u@e.com",
            issuer: "https://wrong-issuer", audience: "test-client-id");

        await sut.Invoking(s => s.ValidateAsync(token))
            .Should().ThrowAsync<IdTokenValidationException>();
    }

    [Fact]
    public async Task ValidateAsync_WithNullToken_ThrowsIdTokenValidationException()
    {
        var (_, jwksJson) = CreateKeyPairAndJwks();
        var sut = CreateSut(jwksJson);

        await sut.Invoking(s => s.ValidateAsync(null!))
            .Should().ThrowAsync<IdTokenValidationException>();
    }

    [Fact]
    public async Task ValidateAsync_WithEmptyToken_ThrowsIdTokenValidationException()
    {
        var (_, jwksJson) = CreateKeyPairAndJwks();
        var sut = CreateSut(jwksJson);

        await sut.Invoking(s => s.ValidateAsync(string.Empty))
            .Should().ThrowAsync<IdTokenValidationException>();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static JwksIdTokenValidator CreateSut(string jwksJson)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jwksJson)
            });
        var httpClient = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var options = Microsoft.Extensions.Options.Options.Create(new GoogleCalendarOptions
        {
            JwksUri = "https://test-issuer/.well-known/jwks.json",
            ValidIssuer = "https://test-issuer",
            ClientId = "test-client-id"
        });

        return new JwksIdTokenValidator(factoryMock.Object, options);
    }

    private static (RSA Rsa, string JwksJson) CreateKeyPairAndJwks()
    {
        var rsa = RSA.Create(2048);
        var pub = rsa.ExportParameters(false);
        var n = Base64UrlEncode(pub.Modulus!);
        var e = Base64UrlEncode(pub.Exponent!);
        var jwksJson = $$"""{"keys":[{"kty":"RSA","use":"sig","alg":"RS256","n":"{{n}}","e":"{{e}}"}]}""";
        return (rsa, jwksJson);
    }

    private static string CreateSignedJwt(RSA rsa, string sub, string email,
        string issuer, string audience, int expiryOffsetMinutes = 60)
    {
        var now = DateTimeOffset.UtcNow;
        var header = Base64UrlEncode("{\"alg\":\"RS256\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncode(
            $"{{\"sub\":\"{sub}\",\"email\":\"{email}\"," +
            $"\"iss\":\"{issuer}\",\"aud\":\"{audience}\"," +
            $"\"iat\":{now.ToUnixTimeSeconds()}," +
            $"\"exp\":{now.AddMinutes(expiryOffsetMinutes).ToUnixTimeSeconds()}}}");
        var toSign = $"{header}.{payload}";
        var sig = Base64UrlEncode(rsa.SignData(
            Encoding.UTF8.GetBytes(toSign),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
        return $"{toSign}.{sig}";
    }

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Base64UrlEncode(string text)
        => Base64UrlEncode(Encoding.UTF8.GetBytes(text));
}
