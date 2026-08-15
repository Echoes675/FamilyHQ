using System.Globalization;
using System.Net;
using FamilyHQ.Core.Exceptions;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Auth;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Middleware;

/// <summary>
/// The single point that maps typed domain exceptions to HTTP status codes (FHQ-39). The HTTP
/// contract no longer depends on exception message text. Also maps an exhausted foreground HTTP
/// timeout (TaskCanceledException wrapping TimeoutException, the FHQ-91 per-attempt timeout after
/// FHQ-154 retries ran out) to a 504 — unless the request was aborted by the client, in which case
/// cancellation is declined. Any exception this handler does not recognise is declined so the
/// framework's default handling surfaces it as a 500.
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
        // A cancellation while the client already aborted is not an upstream timeout — decline it so
        // the framework's aborted-request handling applies instead of a 504 nobody will read.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            return false;

        if (Map(exception) is not { } mapping)
            return false; // not a domain exception → let the default pipeline produce a 500

        logger.LogWarning(
            exception,
            "Domain exception mapped to {StatusCode} for {Method} {Path}.",
            mapping.Status, httpContext.Request.Method, httpContext.Request.Path);

        // FHQ-85: this is the single point every foreground reauth-required request passes
        // through — persist NeedsReauth here so the kiosk reconnect banner appears without
        // waiting for a background sync to also fail. Idempotent: the token store skips the
        // write/broadcast when the user is already flagged.
        if (exception is GoogleReauthRequiredException reauth)
            await TryMarkNeedsReauthAsync(httpContext, reauth);

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

    /// <summary>
    /// Persists NeedsReauth for the user carried on the exception. Persistence failure must never
    /// mask the 409 contract, so any error is logged and swallowed — the background sync path
    /// re-attempts the mark on its next failure. The store call deliberately uses
    /// <see cref="CancellationToken.None"/>: once reauth is detected, a client abort must not
    /// cancel the mark (matching the AuthController webhook-registration precedent).
    /// </summary>
    private async Task TryMarkNeedsReauthAsync(HttpContext httpContext, GoogleReauthRequiredException exception)
    {
        if (exception.UserId is not { } userId)
        {
            logger.LogWarning(
                "Reauth-required exception from {Source} carried no user id; NeedsReauth not persisted for this request.",
                exception.FailureSource);
            return;
        }

        try
        {
            var tokenStore = httpContext.RequestServices.GetRequiredService<ITokenStore>();
            await tokenStore.MarkNeedsReauthAsync(userId, exception.ErrorDescription, CancellationToken.None);
        }
        catch (OperationCanceledException ex)
        {
            // Benign shutdown-race cancellation (the request token is never passed in).
            logger.LogDebug(ex, "NeedsReauth persistence cancelled for user {UserId}.", userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist NeedsReauth for user {UserId}.", userId);
        }
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

        GoogleApiException e => MapGoogleApi(e),

        // FHQ-91: an HttpClient per-attempt timeout is a TaskCanceledException wrapping a
        // TimeoutException — after the FHQ-154 retries are exhausted it reaches here. The title is
        // provider-neutral because every typed HttpClient (Google, location, geocoding) shares this
        // signature. No Retry-After: that header belongs to 503/429 responses.
        TaskCanceledException { InnerException: TimeoutException } =>
            new Mapping(
                StatusCodes.Status504GatewayTimeout,
                "Upstream Timeout",
                "An upstream service did not respond in time. Please retry shortly."),

        _ => null
    };

    /// <summary>
    /// Google 429 or rate-limit 403 → 503 + Retry-After when Google supplied a delay (post-FHQ-83, a
    /// GoogleApiException carrying 403 is always a rate-limit — auth-403 became
    /// <see cref="GoogleReauthRequiredException"/>); every other Google status (incl. 400) → 502.
    /// </summary>
    private static Mapping MapGoogleApi(GoogleApiException exception) =>
        exception.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden
            ? new Mapping(
                StatusCodes.Status503ServiceUnavailable,
                "Calendar Provider Unavailable",
                "The calendar provider is rate-limiting requests. Please retry shortly.",
                RetryAfterSeconds: exception.RetryAfter is { } ra && ra > TimeSpan.Zero
                    ? (int)Math.Ceiling(ra.TotalSeconds)
                    : null)
            : new Mapping(
                StatusCodes.Status502BadGateway,
                "Upstream Calendar Error",
                "The calendar provider rejected the request.");
}
