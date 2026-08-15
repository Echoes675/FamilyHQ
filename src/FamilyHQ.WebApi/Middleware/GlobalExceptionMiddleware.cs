using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Middleware;

/// <summary>
/// Outermost catch-all (FHQ-100). Ordinary endpoint exceptions never reach this catch block:
/// UseExceptionHandler terminates them itself (domain exceptions → 4xx via DomainExceptionHandler,
/// everything else → framework ProblemDetails 500). This middleware only sees exceptions
/// UseExceptionHandler rethrows — a response that has already started, a failure inside
/// exception handling itself, or a mapped 404 whose ProblemDetails write was declined by content
/// negotiation (the framework's 404 guard rethrows). A started response can no longer be
/// rewritten, so that case is logged and rethrown unmodified; the server then aborts the
/// connection, which is the only honest signal to the client that the body is truncated.
/// </summary>
public class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                logger.LogError(ex,
                    "Unhandled exception after the response started for {Method} {Path}; response cannot be modified, rethrowing",
                    context.Request.Method, context.Request.Path);
                throw;
            }

            logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            var details = environment.IsDevelopment() ? ex.Message : null;
            var payload = new { error = "An internal server error occurred.", details };

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
        }
    }
}
