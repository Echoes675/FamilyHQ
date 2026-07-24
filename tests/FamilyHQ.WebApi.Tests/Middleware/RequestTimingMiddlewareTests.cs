using System.Diagnostics;
using FamilyHQ.WebApi.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Middleware;

public class RequestTimingMiddlewareTests
{
    [Fact]
    public async Task Invoke_ApiRequest_LogsDurationAtInformation()
    {
        var logger = new Mock<ILogger<RequestTimingMiddleware>>();
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var sut = new RequestTimingMiddleware(next, logger.Object);

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/api/calendars/events";
        ctx.Response.StatusCode = 200;

        await sut.InvokeAsync(ctx);

        nextCalled.Should().BeTrue();
        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("/api/calendars/events")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task Invoke_NonApiRequest_DoesNotLog()
    {
        var logger = new Mock<ILogger<RequestTimingMiddleware>>();
        RequestDelegate next = _ => Task.CompletedTask;
        var sut = new RequestTimingMiddleware(next, logger.Object);

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/hubs/calendar";

        await sut.InvokeAsync(ctx);

        logger.Verify(l => l.Log(LogLevel.Information, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    [Fact]
    public async Task Invoke_WhenNextThrows_LogsWarningFault_AndRethrows_NotInformation()
    {
        var logger = new Mock<ILogger<RequestTimingMiddleware>>();
        RequestDelegate next = _ => throw new InvalidOperationException("boom");
        var sut = new RequestTimingMiddleware(next, logger.Object);

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "PUT";
        ctx.Request.Path = "/api/events/123";

        var act = async () => await sut.InvokeAsync(ctx);

        // the exception must still propagate to the outer handlers
        await act.Should().ThrowAsync<InvalidOperationException>();

        // faulted request → one Warning carrying the exception and a "faulted" message
        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("/api/events/123") && v.ToString()!.Contains("faulted")),
            It.Is<Exception?>(e => e is InvalidOperationException),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

        // and it must NOT log the success/Information "responded" line
        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    [Fact]
    public async Task Invoke_WhenClientAborts_LogsInformationAborted_NotWarning()
    {
        var logger = new Mock<ILogger<RequestTimingMiddleware>>();
        RequestDelegate next = _ => throw new OperationCanceledException();
        var sut = new RequestTimingMiddleware(next, logger.Object);

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/api/calendars/events";
        ctx.RequestAborted = new CancellationToken(canceled: true);

        var act = async () => await sut.InvokeAsync(ctx);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // client abort → Information "aborted by client", exactly once
        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("aborted by client")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

        // must NOT be logged at Warning
        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }
}
