using System.Globalization;
using FamilyHQ.Core.Exceptions;
using FamilyHQ.Services.Auth;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Middleware;

/// <summary>
/// The single point that maps typed domain exceptions to HTTP status codes (FHQ-39). The HTTP
/// contract no longer depends on exception message text. Any exception this handler does not
/// recognise is declined so the framework's default handling surfaces it as a 500.
/// </summary>
public sealed class DomainExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>A resolved exception → response mapping. Extensions/RetryAfter are optional.</summary>
    private sealed record Mapping(
        int Status,
        string Title,
        string? Detail,
        int? RetryAfterSeconds = null,
        IReadOnlyDictionary<string, object?>? Extensions = null);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (Map(exception) is not { } mapping)
            return false; // not a domain exception → let the default pipeline produce a 500

        logger.LogWarning(
            exception,
            "Domain exception mapped to {StatusCode} for {Method} {Path}.",
            mapping.Status, httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = mapping.Status;

        if (mapping.RetryAfterSeconds is { } seconds)
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

        var problemDetails = new ProblemDetails
        {
            Status = mapping.Status,
            Title = mapping.Title,
            Detail = mapping.Detail
        };

        if (mapping.Extensions is { } extensions)
            foreach (var (key, value) in extensions)
                problemDetails.Extensions[key] = value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }

    private static Mapping? Map(Exception exception) => exception switch
    {
        NotFoundException =>
            new Mapping(StatusCodes.Status404NotFound, "Not Found", null),

        DomainValidationException e =>
            new Mapping(StatusCodes.Status400BadRequest, "Validation Failed", e.Message),

        GoogleReauthRequiredException e =>
            new Mapping(
                StatusCodes.Status409Conflict,
                "Reconnect Google Calendar",
                "Your Google connection needs to be re-authorised.",
                Extensions: new Dictionary<string, object?>
                {
                    ["code"] = "needs_reauth",
                    ["source"] = e.FailureSource == GoogleAuthFailureSource.TokenRefresh ? "token_refresh" : "calendar_api",
                    ["reconnectUrl"] = "/api/auth/login"
                }),

        GoogleApiException =>
            new Mapping(
                StatusCodes.Status502BadGateway,
                "Upstream Calendar Error",
                "The calendar provider rejected the request."),

        _ => null
    };
}
