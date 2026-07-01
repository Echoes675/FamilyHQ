using FamilyHQ.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Middleware;

/// <summary>
/// The single point that maps typed domain exceptions to HTTP status codes (FHQ-39). The HTTP
/// contract no longer depends on exception message text: a <see cref="NotFoundException"/> becomes
/// 404 and a <see cref="DomainValidationException"/> becomes 400, each written as an RFC7807
/// <see cref="ProblemDetails"/>. Any other exception is left unhandled so the framework's default
/// handling surfaces it as a 500 — an unexpected <see cref="InvalidOperationException"/> is no
/// longer masked as a 4xx.
/// </summary>
public sealed class DomainExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var statusCode = exception switch
        {
            NotFoundException => StatusCodes.Status404NotFound,
            DomainValidationException => StatusCodes.Status400BadRequest,
            _ => (int?)null
        };

        if (statusCode is not { } status)
            return false; // not a domain exception → let the default pipeline produce a 500

        logger.LogWarning(
            exception,
            "Domain exception mapped to {StatusCode} for {Method} {Path}.",
            status, httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = exception switch
                {
                    NotFoundException         => "Not Found",
                    DomainValidationException => "Validation Failed",
                    _                         => "Error"
                },
                Detail = exception switch
                {
                    NotFoundException           => null,
                    DomainValidationException e => e.Message,
                    _                           => null
                }
            }
        });
    }
}
