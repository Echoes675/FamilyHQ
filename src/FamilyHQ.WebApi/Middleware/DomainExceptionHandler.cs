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
/// <remarks>
/// FHQ-175: a mapping may also carry a <b>user message</b>, emitted as the
/// <see cref="UserMessageExtension"/> ProblemDetails extension. It is the only server text the
/// kiosk renders verbatim, so it is opt-in per exception: <see cref="DomainException.UserMessage"/>
/// for domain types, and fixed strings here for the non-domain mappings (reauth, Google API,
/// timeout). <c>Detail</c> is unchanged and stays the log/API-client string. Absent extension, the
/// kiosk shows its generic fallback — so a new exception without a vetted message is safe by default.
/// </remarks>
public sealed class DomainExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>ProblemDetails extension key carrying the kiosk-safe text (FHQ-175).</summary>
    public const string UserMessageExtension = "userMessage";

    /// <summary>ProblemDetails extension key carrying a machine-readable failure code.</summary>
    public const string CodeExtension = "code";

    /// <summary>The <see cref="CodeExtension"/> value for a reauth-required 409.</summary>
    public const string NeedsReauthCode = "needs_reauth";

    // Kiosk wording for the non-domain mappings. Each is written for a 7" touchscreen and says what
    // to do next, so the kiosk adds no retry hint of its own: say "try again" here only when a
    // retry can actually succeed. No "upstream", no "rate-limiting", no provider internals.
    private const string ReauthUserMessage = "Your Google connection needs to be re-authorised.";
    private const string RateLimitedUserMessage = "Google Calendar is busy right now. Please try again in a moment.";
    // Deliberately advice-free: this arm covers every non-rate-limit rejection, including a Google
    // 400 — a malformed-payload FamilyHQ bug that fails identically on every retry. A vetted "please
    // try again" there would be authoritative wrong advice; retry wording is reserved for the arms
    // that are genuinely transient (rate-limit, timeout).
    private const string ProviderRejectedUserMessage = "Google Calendar couldn't apply this change.";
    private const string TimeoutUserMessage = "The request timed out. Please try again in a moment.";

    /// <summary>A resolved exception → response mapping. Extensions/RetryAfter/UserMessage are optional.</summary>
    private sealed record Mapping(
        int Status,
        string Title,
        string? Detail,
        int? RetryAfterSeconds = null,
        IReadOnlyDictionary<string, object?>? Extensions = null,
        string? UserMessage = null);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // Belt-and-braces for the abort race: the framework normally 499s aborted requests before
        // handlers run, so this guard is only reachable when the abort lands mid-flight — decline
        // rather than dress a dead-socket cancellation up as an upstream timeout.
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

        // Emitted only when present: an absent key is the kiosk's "show the generic fallback"
        // signal, and a null-valued key would be one more shape for a client to reason about.
        if (mapping.UserMessage is { } userMessage)
            problemDetails.Extensions[UserMessageExtension] = userMessage;

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
        NotFoundException e =>
            new Mapping(StatusCodes.Status404NotFound, "Not Found", null, UserMessage: e.UserMessage),

        // FHQ-172/FHQ-175: not the caller's fault and not a server fault — an upstream-state
        // precondition that blocked the write with nothing written. See the type's remarks for why
        // 409 rather than 400/422/500. Listed before the validation family because it no longer
        // belongs to it.
        SeriesOriginUnresolvedException e =>
            new Mapping(
                StatusCodes.Status409Conflict,
                SeriesOriginUnresolvedException.Title,
                e.Message,
                UserMessage: e.UserMessage),

        DomainValidationException e =>
            new Mapping(StatusCodes.Status400BadRequest, "Validation Failed", e.Message, UserMessage: e.UserMessage),

        GoogleReauthRequiredException e =>
            new Mapping(
                StatusCodes.Status409Conflict,
                "Reconnect Google Calendar",
                ReauthUserMessage,
                Extensions: new Dictionary<string, object?>
                {
                    [CodeExtension] = NeedsReauthCode,
                    ["source"] = e.FailureSource == GoogleAuthFailureSource.TokenRefresh ? "token_refresh" : "calendar_api",
                    ["reconnectUrl"] = "/api/auth/login"
                },
                UserMessage: ReauthUserMessage),

        GoogleApiException e => MapGoogleApi(e),

        // FHQ-91: an HttpClient per-attempt timeout is a TaskCanceledException wrapping a
        // TimeoutException — after the FHQ-154 retries are exhausted it reaches here. The title is
        // provider-neutral because every typed HttpClient (Google, location, geocoding) shares this
        // signature. No Retry-After: that header belongs to 503/429 responses.
        TaskCanceledException { InnerException: TimeoutException } =>
            new Mapping(
                StatusCodes.Status504GatewayTimeout,
                "Upstream Timeout",
                "An upstream service did not respond in time. Please retry shortly.",
                UserMessage: TimeoutUserMessage),

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
                    : null,
                UserMessage: RateLimitedUserMessage)
            : new Mapping(
                StatusCodes.Status502BadGateway,
                "Upstream Calendar Error",
                "The calendar provider rejected the request.",
                UserMessage: ProviderRejectedUserMessage);
}
