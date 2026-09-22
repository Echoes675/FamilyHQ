using System.Text;
using FamilyHQ.WebApi.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Auth;

/// <summary>
/// FHQ-139. The smoke-account requirement is evaluated in the authorization pipeline, which runs
/// before MVC binds the body — so the requested user id has to be read from the raw request. It must
/// leave the body readable for model binding, and must never throw on a hostile or malformed body.
/// </summary>
public class SmokeRequestUserIdReaderTests
{
    private const string SmokeUserId = "smoke-account-google-sub";

    [Fact]
    public async Task ReadUserIdAsync_WhenTheBodyNamesAUser_ReturnsThatUserId()
    {
        var sut = new SmokeRequestUserIdReader();
        var request = JsonRequest($"{{\"userId\":\"{SmokeUserId}\"}}");

        var userId = await sut.ReadUserIdAsync(request, CancellationToken.None);

        userId.Should().Be(SmokeUserId);
    }

    [Fact]
    public async Task ReadUserIdAsync_WhenThePropertyIsPascalCased_ReturnsThatUserId()
    {
        var sut = new SmokeRequestUserIdReader();
        var request = JsonRequest($"{{\"UserId\":\"{SmokeUserId}\"}}");

        var userId = await sut.ReadUserIdAsync(request, CancellationToken.None);

        userId.Should().Be(SmokeUserId,
            "MVC binds property names case-insensitively, so the allowlist must read them the same way");
    }

    [Fact]
    public async Task ReadUserIdAsync_WhenItHasRead_LeavesTheBodyReadableForModelBinding()
    {
        var sut = new SmokeRequestUserIdReader();
        var body = $"{{\"userId\":\"{SmokeUserId}\"}}";
        var request = JsonRequest(body);

        await sut.ReadUserIdAsync(request, CancellationToken.None);

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        (await reader.ReadToEndAsync()).Should().Be(body);
    }

    [Fact]
    public async Task ReadUserIdAsync_WhenTheBodyHasNoUserIdProperty_ReturnsNull()
    {
        var sut = new SmokeRequestUserIdReader();
        var request = JsonRequest("{\"somethingElse\":\"x\"}");

        var userId = await sut.ReadUserIdAsync(request, CancellationToken.None);

        userId.Should().BeNull();
    }

    [Fact]
    public async Task ReadUserIdAsync_WhenTheUserIdIsNotAString_ReturnsNull()
    {
        var sut = new SmokeRequestUserIdReader();
        var request = JsonRequest("{\"userId\":42}");

        var userId = await sut.ReadUserIdAsync(request, CancellationToken.None);

        userId.Should().BeNull();
    }

    [Fact]
    public async Task ReadUserIdAsync_WhenTheBodyIsNotJson_ReturnsNull()
    {
        var sut = new SmokeRequestUserIdReader();
        var request = JsonRequest("not json at all");

        var userId = await sut.ReadUserIdAsync(request, CancellationToken.None);

        userId.Should().BeNull("a malformed body names nobody; it must not throw");
    }

    [Fact]
    public async Task ReadUserIdAsync_WhenTheBodyIsAJsonArray_ReturnsNull()
    {
        var sut = new SmokeRequestUserIdReader();
        var request = JsonRequest("[\"userId\"]");

        var userId = await sut.ReadUserIdAsync(request, CancellationToken.None);

        userId.Should().BeNull();
    }

    [Fact]
    public async Task ReadUserIdAsync_WhenTheBodyIsEmpty_ReturnsNull()
    {
        var sut = new SmokeRequestUserIdReader();
        var request = JsonRequest(string.Empty);

        var userId = await sut.ReadUserIdAsync(request, CancellationToken.None);

        userId.Should().BeNull();
    }

    [Fact]
    public async Task ReadUserIdAsync_WhenTheContentTypeIsNotJson_ReturnsNull()
    {
        var sut = new SmokeRequestUserIdReader();
        var request = JsonRequest($"{{\"userId\":\"{SmokeUserId}\"}}", contentType: "text/plain");

        var userId = await sut.ReadUserIdAsync(request, CancellationToken.None);

        userId.Should().BeNull();
    }

    [Fact]
    public async Task ReadUserIdAsync_WhenTheBodyIsLargerThanTheReadLimit_ReturnsNull()
    {
        var sut = new SmokeRequestUserIdReader();
        var padding = new string('x', SmokeRequestUserIdReader.MaxBodyBytes);
        var request = JsonRequest($"{{\"padding\":\"{padding}\",\"userId\":\"{SmokeUserId}\"}}");

        var userId = await sut.ReadUserIdAsync(request, CancellationToken.None);

        userId.Should().BeNull("an oversized body is refused, not buffered");
    }

    [Fact]
    public async Task ReadUserIdAsync_WhenTheContentLengthIsUnknown_ReturnsNull()
    {
        var sut = new SmokeRequestUserIdReader();
        var request = JsonRequest($"{{\"userId\":\"{SmokeUserId}\"}}");
        request.ContentLength = null;

        var userId = await sut.ReadUserIdAsync(request, CancellationToken.None);

        userId.Should().BeNull("a chunked body is not read here; the action still answers it");
    }

    private static HttpRequest JsonRequest(string body, string contentType = "application/json")
    {
        var httpContext = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(body);
        httpContext.Request.Body = new MemoryStream(bytes);
        httpContext.Request.ContentType = contentType;
        httpContext.Request.ContentLength = bytes.Length;
        httpContext.Request.Method = HttpMethods.Post;
        return httpContext.Request;
    }
}
