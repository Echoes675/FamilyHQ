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
}
