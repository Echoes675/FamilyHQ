using FamilyHQ.Core.Constants;

namespace FamilyHQ.WebApi.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationConstants.CorrelationIdHeaderName].FirstOrDefault();

        if (string.IsNullOrEmpty(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
            context.Request.Headers[CorrelationConstants.CorrelationIdHeaderName] = correlationId;
        }

        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(CorrelationConstants.CorrelationIdHeaderName))
            {
                context.Response.Headers[CorrelationConstants.CorrelationIdHeaderName] = correlationId;
            }
            return Task.CompletedTask;
        });

        var sessionCorrelationId = context.Request.Headers[CorrelationConstants.SessionCorrelationIdHeaderName].FirstOrDefault();
        
        var scopeState = new Dictionary<string, object>
        {
            { "CorrelationId", correlationId }
        };

        if (!string.IsNullOrEmpty(sessionCorrelationId))
        {
            scopeState["SessionCorrelationId"] = sessionCorrelationId;
        }

        using (_logger.BeginScope(scopeState))
        {
            await _next(context);
        }
    }
}
