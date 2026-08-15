using System.Text;
using FamilyHQ.WebApi.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Middleware;

/// <summary>
/// FHQ-100: the outermost catch-all only writes its 500 JSON body when the response has NOT
/// started. Once the response has started (UseExceptionHandler rethrows in exactly that case)
/// the middleware must log and rethrow the original exception without touching the response —
/// mutating a started response throws or corrupts the already-sent body.
/// </summary>
public class GlobalExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NoException_PassesThroughWithoutLoggingOrResponseMutation()
    {
        var logger = CreateLogger();
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var sut = new GlobalExceptionMiddleware(next, logger.Object, CreateEnvironment("Production"));
        var context = CreateContext();

        await sut.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        ((MemoryStream)context.Response.Body).Length.Should().Be(0);
        VerifyErrorLogCount(logger, Times.Never());
    }

    [Fact]
    public async Task InvokeAsync_ExceptionBeforeResponseStarted_Writes500JsonErrorBody()
    {
        var logger = CreateLogger();
        RequestDelegate next = _ => throw new InvalidOperationException("boom");
        var sut = new GlobalExceptionMiddleware(next, logger.Object, CreateEnvironment("Production"));
        var context = CreateContext();

        await sut.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.ContentType.Should().Be("application/json");
        ReadBody(context).Should().Contain("An internal server error occurred.");
    }

    [Fact]
    public async Task InvokeAsync_ExceptionBeforeResponseStartedInProduction_OmitsExceptionMessageFromBody()
    {
        var logger = CreateLogger();
        RequestDelegate next = _ => throw new InvalidOperationException("secret-internal-detail");
        var sut = new GlobalExceptionMiddleware(next, logger.Object, CreateEnvironment("Production"));
        var context = CreateContext();

        await sut.InvokeAsync(context);

        ReadBody(context).Should().NotContain("secret-internal-detail");
    }

    [Fact]
    public async Task InvokeAsync_ExceptionBeforeResponseStartedInDevelopment_IncludesExceptionMessageInDetails()
    {
        var logger = CreateLogger();
        RequestDelegate next = _ => throw new InvalidOperationException("dev-diagnostic-detail");
        var sut = new GlobalExceptionMiddleware(next, logger.Object, CreateEnvironment("Development"));
        var context = CreateContext();

        await sut.InvokeAsync(context);

        ReadBody(context).Should().Contain("dev-diagnostic-detail");
    }

    [Fact]
    public async Task InvokeAsync_ExceptionBeforeResponseStarted_LogsError()
    {
        var logger = CreateLogger();
        RequestDelegate next = _ => throw new InvalidOperationException("boom");
        var sut = new GlobalExceptionMiddleware(next, logger.Object, CreateEnvironment("Production"));
        var context = CreateContext();

        await sut.InvokeAsync(context);

        VerifyErrorLogCount(logger, Times.Once());
    }

    [Fact]
    public async Task InvokeAsync_ExceptionAfterResponseStarted_RethrowsOriginalException()
    {
        var logger = CreateLogger();
        var original = new InvalidOperationException("mid-stream boom");
        RequestDelegate next = _ => throw original;
        var sut = new GlobalExceptionMiddleware(next, logger.Object, CreateEnvironment("Production"));
        var context = CreateContext();
        SetResponseStarted(context);

        var act = async () => await sut.InvokeAsync(context);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(original);
    }

    [Fact]
    public async Task InvokeAsync_ExceptionAfterResponseStarted_DoesNotMutateResponse()
    {
        var logger = CreateLogger();
        RequestDelegate next = _ => throw new InvalidOperationException("mid-stream boom");
        var sut = new GlobalExceptionMiddleware(next, logger.Object, CreateEnvironment("Production"));
        var context = CreateContext();
        var responseFeature = SetResponseStarted(context);

        var act = async () => await sut.InvokeAsync(context);

        await act.Should().ThrowAsync<InvalidOperationException>();
        responseFeature.VerifySet(f => f.StatusCode = It.IsAny<int>(), Times.Never());
        responseFeature.Object.Headers.Should().BeEmpty();
        ((MemoryStream)context.Response.Body).Length.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_ExceptionAfterResponseStarted_LogsError()
    {
        var logger = CreateLogger();
        RequestDelegate next = _ => throw new InvalidOperationException("mid-stream boom");
        var sut = new GlobalExceptionMiddleware(next, logger.Object, CreateEnvironment("Production"));
        var context = CreateContext();
        SetResponseStarted(context);

        var act = async () => await sut.InvokeAsync(context);

        await act.Should().ThrowAsync<InvalidOperationException>();
        VerifyErrorLogCount(logger, Times.Once());
    }

    private static Mock<ILogger<GlobalExceptionMiddleware>> CreateLogger() => new();

    private static IHostEnvironment CreateEnvironment(string environmentName)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(environmentName);
        return environment.Object;
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/calendars/events";
        context.Response.Body = new MemoryStream();
        return context;
    }

    /// <summary>
    /// Swaps in a fake <see cref="IHttpResponseFeature"/> whose HasStarted is true, mirroring a
    /// response whose headers/body have already been flushed to the client. Mutations against the
    /// fake are recorded (not throwing) so the assertions can prove none were attempted.
    /// </summary>
    private static Mock<IHttpResponseFeature> SetResponseStarted(DefaultHttpContext context)
    {
        var responseFeature = new Mock<IHttpResponseFeature>();
        responseFeature.SetupGet(f => f.HasStarted).Returns(true);
        responseFeature.SetupGet(f => f.Headers).Returns(new HeaderDictionary());
        responseFeature.SetupProperty(f => f.StatusCode, StatusCodes.Status200OK);
        context.Features.Set(responseFeature.Object);
        return responseFeature;
    }

    private static string ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
    }

    private static void VerifyErrorLogCount(Mock<ILogger<GlobalExceptionMiddleware>> logger, Times times) =>
        logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsNotNull<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);
}
