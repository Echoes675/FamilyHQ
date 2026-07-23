using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Middleware;

/// <summary>
/// Logs method, path, status and elapsed ms for API requests so request-path latency
/// (e.g. the intermittent slow /api/calendars/events during a sync, FHQ-44) is visible
/// in production logs. Scoped to /api to avoid noise from SignalR / static assets.
/// </summary>
public class RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await next(context);
            sw.Stop();
            logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs} ms",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(
                ex,
                "HTTP {Method} {Path} faulted after {ElapsedMs} ms (unhandled exception; final status set by an outer handler)",
                context.Request.Method,
                context.Request.Path.Value,
                sw.ElapsedMilliseconds);
            throw;
        }
    }
}
