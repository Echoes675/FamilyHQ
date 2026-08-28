using FamilyHQ.Core.Exceptions;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Auth;
using FamilyHQ.WebApi.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Text.Json;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Middleware;

/// <summary>
/// The single mapping point introduced by FHQ-39: typed domain exceptions become 4xx ProblemDetails,
/// every other exception is declined (TryHandleAsync returns false) so the framework surfaces a 500.
/// </summary>
public class DomainExceptionHandlerTests
{
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid CalId   = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public static IEnumerable<object[]> NotFoundExceptions() =>
    [
        [new EventNotFoundException(EventId)]
    ];

    public static IEnumerable<object[]> ValidationExceptions() =>
    [
        [new UnknownCalendarException(CalId)],
        [new NoMembersException()],
        [new NotPartOfRecurringSeriesException(EventId)],
        [new MemberScopeViolationException()],
        [new UnknownRecurrenceScopeException(99)],
        [new ContradictoryRecurrenceUpdateException()],
        [new InvalidSeriesSplitException("no occurrences left")]
    ];

    [Theory]
    [MemberData(nameof(NotFoundExceptions))]
    public async Task NotFoundException_MapsTo404(Exception exception)
    {
        var (handler, context) = CreateSut();

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Theory]
    [MemberData(nameof(ValidationExceptions))]
    public async Task DomainValidationException_MapsTo400(Exception exception)
    {
        var (handler, context) = CreateSut();

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task UnexpectedInvalidOperationException_IsDeclined_SoItSurfacesAs500()
    {
        var (handler, context) = CreateSut();

        // A server-precondition failure is a plain InvalidOperationException, NOT a DomainException.
        var handled = await handler.TryHandleAsync(
            context, new InvalidOperationException("No shared calendar configured."), CancellationToken.None);

        // Declined → the framework's default handling produces a 500, not a masked 4xx.
        handled.Should().BeFalse();
    }

    [Fact]
    public async Task UnexpectedArgumentException_IsDeclined()
    {
        var (handler, context) = CreateSut();

        var handled = await handler.TryHandleAsync(
            context, new ArgumentException("bad arg"), CancellationToken.None);

        handled.Should().BeFalse();
    }

    [Fact]
    public async Task NotFoundException_Title_IsStableNotFoundString()
    {
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(context, new EventNotFoundException(EventId), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ProblemDetails.Title.Should().Be("Not Found");
    }

    [Fact]
    public async Task NotFoundException_Detail_IsNull()
    {
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(context, new EventNotFoundException(EventId), CancellationToken.None);

        captured!.ProblemDetails.Detail.Should().BeNull();
    }

    [Fact]
    public async Task DomainValidationException_Title_IsStableValidationFailedString()
    {
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(context, new NoMembersException(), CancellationToken.None);

        captured!.ProblemDetails.Title.Should().Be("Validation Failed");
    }

    [Fact]
    public async Task DomainValidationException_Detail_ContainsExceptionMessage()
    {
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);
        var exception = new InvalidSeriesSplitException("no occurrences left");

        await handler.TryHandleAsync(context, exception, CancellationToken.None);

        captured!.ProblemDetails.Detail.Should().Be(exception.Message);
    }

    [Fact]
    public async Task GoogleApiException_MapsTo502()
    {
        var (handler, context) = CreateSut();

        var handled = await handler.TryHandleAsync(
            context,
            new GoogleApiException(HttpStatusCode.BadRequest, "PatchEventFields"),
            CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Fact]
    public async Task GoogleApiException_Title_IsUpstreamCalendarError_AndStatus502()
    {
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(
            context,
            new GoogleApiException(HttpStatusCode.BadRequest, "PatchEventFields"),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ProblemDetails.Title.Should().Be("Upstream Calendar Error");
        captured.ProblemDetails.Status.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Fact]
    public async Task GoogleApiException_Detail_IsFixedGenericMessage()
    {
        // FHQ-88: the 502 detail is a fixed generic string — no Google error text can reach it.
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(
            context,
            new GoogleApiException(HttpStatusCode.BadRequest, "PatchEventFields"),
            CancellationToken.None);

        captured!.ProblemDetails.Detail.Should().Be("The calendar provider rejected the request.");
    }

    [Fact]
    public async Task GoogleApiException_RateLimit503_Detail_IsFixedGenericMessage()
    {
        // FHQ-88: the 503 rate-limit detail is a fixed generic string — no Google text reaches it.
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(
            context,
            new GoogleApiException(HttpStatusCode.TooManyRequests, "GetCalendars", TimeSpan.FromSeconds(30)),
            CancellationToken.None);

        captured!.ProblemDetails.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        captured.ProblemDetails.Detail.Should().Be(
            "The calendar provider is rate-limiting requests. Please retry shortly.");
    }

    [Fact]
    public async Task GoogleApiException_429_MapsTo503WithRetryAfterHeader()
    {
        var (handler, context) = CreateSut();

        var handled = await handler.TryHandleAsync(
            context,
            new GoogleApiException(HttpStatusCode.TooManyRequests, "GetCalendars", TimeSpan.FromSeconds(30)),
            CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().Should().Be("30");
    }

    [Fact]
    public async Task GoogleApiException_429_WithoutRetryAfter_MapsTo503AndOmitsHeader()
    {
        var (handler, context) = CreateSut();

        var handled = await handler.TryHandleAsync(
            context,
            new GoogleApiException(HttpStatusCode.TooManyRequests, "GetCalendars"),
            CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task GoogleApiException_403RateLimit_MapsTo503WithRetryAfter()
    {
        // FHQ-154: post-FHQ-83 a GoogleApiException(403) is always a rate-limit, so it surfaces like a 429.
        var (handler, context) = CreateSut();

        var handled = await handler.TryHandleAsync(
            context,
            new GoogleApiException(HttpStatusCode.Forbidden, "GetCalendars", TimeSpan.FromSeconds(30)),
            CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().Should().Be("30");
    }

    [Fact]
    public async Task GoogleApiException_400_StaysAt502()
    {
        // Locked decision (FHQ-153): a Google 400 is an upstream integration rejection, not a client
        // error our SPA can act on (the raw body is stripped) — so it stays 502, not 400.
        var (handler, context) = CreateSut();

        var handled = await handler.TryHandleAsync(
            context,
            new GoogleApiException(HttpStatusCode.BadRequest, "PatchEventFields"),
            CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Fact]
    public async Task HttpTimeout_TaskCanceledWithTimeoutInner_MapsTo504WithoutRetryAfter()
    {
        // FHQ-100 drive-by (FHQ-91/FHQ-154 context): an exhausted foreground HTTP timeout surfaces as
        // TaskCanceledException wrapping TimeoutException — a transient gateway timeout, not a 500.
        var (handler, context) = CreateSut();

        var handled = await handler.TryHandleAsync(
            context,
            new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException()),
            CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status504GatewayTimeout);
        context.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task HttpTimeout_TitleAndDetail_AreFixedGenericStrings()
    {
        // FHQ-88 discipline: no upstream/internal exception text reaches the client payload.
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(
            context,
            new TaskCanceledException("internal-timeout-detail-SECRET", new TimeoutException()),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ProblemDetails.Title.Should().Be("Upstream Timeout");
        captured.ProblemDetails.Detail.Should().Be("An upstream service did not respond in time. Please retry shortly.");
        JsonSerializer.Serialize(captured.ProblemDetails).Should().NotContain("SECRET");
    }

    [Fact]
    public async Task TaskCanceledException_WithoutTimeoutInner_IsDeclined()
    {
        // A plain cancellation carries no TimeoutException inner — it is not an HttpClient timeout,
        // so it must not be dressed up as a gateway timeout.
        var (handler, context) = CreateSut();

        var handled = await handler.TryHandleAsync(
            context, new TaskCanceledException("cancelled"), CancellationToken.None);

        handled.Should().BeFalse();
    }

    [Fact]
    public async Task HttpTimeout_WhenRequestAborted_IsDeclined()
    {
        // Client-abort cancellation must never become a 504 — when the caller is gone the framework's
        // aborted-request handling applies, not a gateway-timeout response nobody will read.
        var (handler, context) = CreateSut();
        context.RequestAborted = new CancellationToken(canceled: true);

        var handled = await handler.TryHandleAsync(
            context,
            new TaskCanceledException("canceled", new TimeoutException()),
            CancellationToken.None);

        handled.Should().BeFalse();
    }

    [Fact]
    public async Task GoogleReauthRequiredException_MapsTo409()
    {
        var (handler, context) = CreateSut();

        var handled = await handler.TryHandleAsync(
            context,
            new GoogleReauthRequiredException(GoogleAuthFailureSource.TokenRefresh, "Token has been expired or revoked."),
            CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task GoogleReauthRequiredException_CarriesReauthExtensions_AndDoesNotLeakGoogleErrorText()
    {
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(
            context,
            new GoogleReauthRequiredException(GoogleAuthFailureSource.TokenRefresh, "google-error-description-SECRET"),
            CancellationToken.None);

        captured.Should().NotBeNull();
        var pd = captured!.ProblemDetails;
        pd.Title.Should().Be("Reconnect Google Calendar");
        pd.Detail.Should().Be("Your Google connection needs to be re-authorised.");
        pd.Extensions["code"].Should().Be("needs_reauth");
        pd.Extensions["source"].Should().Be("token_refresh");
        pd.Extensions["reconnectUrl"].Should().Be("/api/auth/login");

        // FHQ-88: the parsed Google ErrorDescription stays server-side — the whole client
        // payload (title, detail, extensions) must not contain it.
        JsonSerializer.Serialize(pd).Should().NotContain("SECRET");
    }

    [Fact]
    public async Task GoogleReauthRequiredException_CalendarApiSource_MapsSourceToCalendarApi()
    {
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(
            context,
            new GoogleReauthRequiredException(GoogleAuthFailureSource.CalendarApi, "insufficient permission"),
            CancellationToken.None);

        captured!.ProblemDetails.Extensions["source"].Should().Be("calendar_api");
    }

    [Fact]
    public async Task GoogleReauthRequiredException_WithUserId_PersistsNeedsReauthOnce()
    {
        // FHQ-85: the 409 mapping point is the single foreground seam that sees every reauth-required
        // request (event writes, manual sync, webhook registration) — it must also persist the flag
        // so the reconnect banner appears without waiting for a background sync to fail.
        var (handler, context, tokenStore) = CreateSutWithTokenStore();

        var handled = await handler.TryHandleAsync(
            context,
            new GoogleReauthRequiredException(
                GoogleAuthFailureSource.TokenRefresh, "Token has been expired or revoked.", userId: "user-1"),
            CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        tokenStore.Verify(
            t => t.MarkNeedsReauthAsync("user-1", "Token has been expired or revoked.", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GoogleReauthRequiredException_WithoutUserId_StillMaps409AndDoesNotMark()
    {
        var (handler, context, tokenStore) = CreateSutWithTokenStore();

        var handled = await handler.TryHandleAsync(
            context,
            new GoogleReauthRequiredException(GoogleAuthFailureSource.CalendarApi, "insufficient permission"),
            CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        tokenStore.Verify(
            t => t.MarkNeedsReauthAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GoogleReauthRequiredException_WhenRequestAborted_StillPersistsWithUncancellableToken()
    {
        // FHQ-85 review: once reauth is DETECTED the mark must not be cancellable — a client
        // abort mid-request would otherwise cancel the DB write and lose the flag. The store
        // call must therefore receive CancellationToken.None, never the request token.
        var (handler, context, tokenStore) = CreateSutWithTokenStore();
        var abortedRequestToken = new CancellationToken(canceled: true);

        var handled = await handler.TryHandleAsync(
            context,
            new GoogleReauthRequiredException(GoogleAuthFailureSource.TokenRefresh, "revoked", userId: "user-1"),
            abortedRequestToken);

        handled.Should().BeTrue();
        tokenStore.Verify(t => t.MarkNeedsReauthAsync("user-1", "revoked", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task GoogleReauthRequiredException_WhenMarkingFails_StillMaps409()
    {
        // Persistence failure must never mask the 409 contract — the background sync path
        // re-attempts the mark on its next failure.
        var (handler, context, tokenStore) = CreateSutWithTokenStore();
        tokenStore
            .Setup(t => t.MarkNeedsReauthAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var handled = await handler.TryHandleAsync(
            context,
            new GoogleReauthRequiredException(GoogleAuthFailureSource.TokenRefresh, "revoked", userId: "user-1"),
            CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    // ---------------------------------------------------------------------------------------------
    // FHQ-175: the userMessage extension is the only server text the kiosk renders verbatim.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// One factory per concrete <see cref="DomainException"/> subtype in FamilyHQ.Core. The
    /// dictionary is deliberately hand-written: <see cref="EveryConcreteDomainException_HasAFactoryHere"/>
    /// fails the moment a new subtype appears without an entry, so whoever adds an exception has to
    /// come here and say whether it carries a user message — it cannot opt in (or out) by accident.
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, Func<DomainException>> DomainExceptionFactories =
        new Dictionary<Type, Func<DomainException>>
        {
            [typeof(EventNotFoundException)]              = () => new EventNotFoundException(EventId),
            [typeof(UnknownCalendarException)]            = () => new UnknownCalendarException(CalId),
            [typeof(NoMembersException)]                  = () => new NoMembersException(),
            [typeof(NotPartOfRecurringSeriesException)]   = () => new NotPartOfRecurringSeriesException(EventId),
            [typeof(MemberScopeViolationException)]       = () => new MemberScopeViolationException(),
            [typeof(UnknownRecurrenceScopeException)]     = () => new UnknownRecurrenceScopeException(99),
            [typeof(ContradictoryRecurrenceUpdateException)] = () => new ContradictoryRecurrenceUpdateException(),
            [typeof(InvalidSeriesSplitException)]         = () => new InvalidSeriesSplitException("no occurrences left"),
            [typeof(SeriesOriginUnresolvedException)]     = SeriesOriginUnresolvedException.ForSeriesTimingChange
        };

    /// <summary>
    /// The opted-in set and the exact kiosk text each carries. Everything in
    /// <see cref="DomainExceptionFactories"/> but not here must emit NO userMessage.
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, string> ExpectedUserMessages =
        new Dictionary<Type, string>
        {
            [typeof(EventNotFoundException)] =
                "This event no longer exists — it may have been deleted from another device.",
            [typeof(NoMembersException)] =
                "Please select at least one calendar.",
            [typeof(MemberScopeViolationException)] =
                "Member changes apply to the whole series. Select \"All events\" to continue.",
            [typeof(SeriesOriginUnresolvedException)] =
                "This series' original start couldn't be read from Google, so its times can't be changed " +
                "for the whole series. Nothing has been changed. If the series still exists in Google " +
                "Calendar this may clear on a retry; if not, edit the series in the Google Calendar app instead."
        };

    // BOTH assemblies that declare DomainException subtypes today: Core, and Services (where the
    // Google auth exceptions live). A subtype added in Services would otherwise bypass this
    // classification entirely and could opt kiosk text in unnoticed.
    private static IEnumerable<Type> ConcreteDomainExceptionTypes() =>
        new[] { typeof(DomainException).Assembly, typeof(GoogleReauthRequiredException).Assembly }
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(DomainException).IsAssignableFrom(t));

    public static IEnumerable<object[]> NonOptedInDomainExceptions() =>
        DomainExceptionFactories.Keys
            .Where(t => !ExpectedUserMessages.ContainsKey(t))
            .Select(t => new object[] { t });

    public static IEnumerable<object[]> OptedInDomainExceptions() =>
        ExpectedUserMessages.Keys.Select(t => new object[] { t });

    [Fact]
    public void EveryConcreteDomainException_HasAFactoryHere()
    {
        // Tripwire: a new DomainException subtype must be registered in DomainExceptionFactories
        // (and, if it carries kiosk text, in ExpectedUserMessages) before this suite goes green.
        ConcreteDomainExceptionTypes().Should().BeEquivalentTo(
            DomainExceptionFactories.Keys,
            "every concrete DomainException must be classified as opted-in or not for the kiosk userMessage");
    }

    [Theory]
    [MemberData(nameof(NonOptedInDomainExceptions))]
    public async Task NonOptedInDomainException_EmitsNoUserMessageExtension(Type exceptionType)
    {
        // Absent key, not a null value: an absent key is the kiosk's "show the generic fallback".
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(context, DomainExceptionFactories[exceptionType](), CancellationToken.None);

        captured!.ProblemDetails.Extensions.Should().NotContainKey(DomainExceptionHandler.UserMessageExtension);
    }

    [Theory]
    [MemberData(nameof(OptedInDomainExceptions))]
    public async Task OptedInDomainException_EmitsItsExactUserMessage(Type exceptionType)
    {
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(context, DomainExceptionFactories[exceptionType](), CancellationToken.None);

        captured!.ProblemDetails.Extensions[DomainExceptionHandler.UserMessageExtension]
            .Should().Be(ExpectedUserMessages[exceptionType]);
    }

    [Fact]
    public async Task OptedInUserMessages_CarryNoIdentifiersOrInternals()
    {
        // The kiosk renders userMessage verbatim, so no GUID, no C# property name, no RFC-5545 term
        // may appear in one — those stay in Detail for logs and API clients.
        foreach (var (type, _) in ExpectedUserMessages)
        {
            ProblemDetailsContext? captured = null;
            var (handler, context) = CreateSut(ctx => captured = ctx);

            await handler.TryHandleAsync(context, DomainExceptionFactories[type](), CancellationToken.None);

            var userMessage = (string)captured!.ProblemDetails.Extensions[DomainExceptionHandler.UserMessageExtension]!;
            userMessage.Should().NotContainAny(
                [EventId.ToString(), CalId.ToString(), "RecurrenceRule", "ClearRecurrence", "RRULE", "COUNT", "CalendarInfoId"],
                because: "{0} opted in to a kiosk-facing message", type.Name);
        }
    }

    [Fact]
    public async Task SeriesOriginUnresolved_MapsTo409WithItsOwnTitle_NotValidationFailed()
    {
        // FHQ-172 deferred this: "Validation Failed" was wrong for a condition that is not the
        // caller's fault, and it was free only while the kiosk rendered nothing. 409 because the
        // series' Google-side state blocks the write, nothing was written, and the user can resolve
        // it (retry later, or edit in the Google Calendar app) — see the type's remarks.
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);
        var exception = SeriesOriginUnresolvedException.ForSeriesSplit();

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        captured!.ProblemDetails.Title.Should().Be("Series Origin Unavailable");
        captured.ProblemDetails.Title.Should().NotBe("Validation Failed");
        captured.ProblemDetails.Detail.Should().Be(exception.Message, "Detail stays the log/API-client string");
    }

    [Fact]
    public async Task SeriesOriginUnresolved_BothSites_EmitAKioskMessageThatFitsTheKioskCap()
    {
        // Both messages must survive the kiosk's 280-char cap intact: the clause that gets cut by an
        // ellipsis would be "edit the series in the Google Calendar app", the advice this exists for.
        foreach (var exception in new[]
                 {
                     SeriesOriginUnresolvedException.ForSeriesTimingChange(),
                     SeriesOriginUnresolvedException.ForSeriesSplit()
                 })
        {
            ProblemDetailsContext? captured = null;
            var (handler, context) = CreateSut(ctx => captured = ctx);

            await handler.TryHandleAsync(context, exception, CancellationToken.None);

            var userMessage = (string)captured!.ProblemDetails.Extensions[DomainExceptionHandler.UserMessageExtension]!;
            userMessage.Length.Should().BeLessThanOrEqualTo(280);
            userMessage.Should().Contain("Nothing has been changed");
            userMessage.Should().Contain("Google Calendar app");
        }
    }

    [Fact]
    public async Task EventNotFound_EmitsUserMessage_AndDetailStaysNull()
    {
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(context, new EventNotFoundException(EventId), CancellationToken.None);

        captured!.ProblemDetails.Detail.Should().BeNull();
        captured.ProblemDetails.Extensions[DomainExceptionHandler.UserMessageExtension]
            .Should().Be("This event no longer exists — it may have been deleted from another device.");
    }

    [Fact]
    public async Task GoogleReauthRequired_EmitsUserMessage_AndNeedsReauthCode()
    {
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(
            context,
            new GoogleReauthRequiredException(GoogleAuthFailureSource.CalendarApi, "google-error-description-SECRET"),
            CancellationToken.None);

        var pd = captured!.ProblemDetails;
        pd.Extensions[DomainExceptionHandler.UserMessageExtension]
            .Should().Be("Your Google connection needs to be re-authorised.");
        pd.Extensions[DomainExceptionHandler.CodeExtension].Should().Be("needs_reauth");
        JsonSerializer.Serialize(pd).Should().NotContain("SECRET");
    }

    [Fact]
    public async Task GoogleApiRateLimit_EmitsKioskWording_WithoutRateLimitingJargon()
    {
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(
            context,
            new GoogleApiException(HttpStatusCode.TooManyRequests, "InsertEvent", TimeSpan.FromSeconds(30)),
            CancellationToken.None);

        var userMessage = (string)captured!.ProblemDetails.Extensions[DomainExceptionHandler.UserMessageExtension]!;
        userMessage.Should().Be("Google Calendar is busy right now. Please try again in a moment.");
        userMessage.Should().NotContainAny(["rate-limit", "upstream", "provider"]);
        captured.ProblemDetails.Detail.Should().Be(
            "The calendar provider is rate-limiting requests. Please retry shortly.",
            "Detail is unchanged for logs and API clients");
    }

    [Fact]
    public async Task GoogleApiRejection_EmitsKioskWording_WithoutUpstreamJargon()
    {
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(
            context,
            new GoogleApiException(HttpStatusCode.BadRequest, "PatchEventFields"),
            CancellationToken.None);

        var userMessage = (string)captured!.ProblemDetails.Extensions[DomainExceptionHandler.UserMessageExtension]!;
        // Advice-free by design: this arm includes deterministic Google 400s, where a vetted
        // "please try again" would be wrong advice the kiosk renders with authority.
        userMessage.Should().Be("Google Calendar couldn't apply this change.");
        userMessage.Should().NotContainAny(["upstream", "provider", "PatchEventFields"]);
    }

    [Fact]
    public async Task HttpTimeout_EmitsKioskWording_WithoutUpstreamJargon()
    {
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(
            context,
            new TaskCanceledException("internal-timeout-detail-SECRET", new TimeoutException()),
            CancellationToken.None);

        var userMessage = (string)captured!.ProblemDetails.Extensions[DomainExceptionHandler.UserMessageExtension]!;
        userMessage.Should().Be("The request timed out. Please try again in a moment.");
        userMessage.Should().NotContainAny(["upstream", "SECRET"]);
    }

    private static (DomainExceptionHandler Handler, HttpContext Context) CreateSut(
        Action<ProblemDetailsContext>? callback = null)
    {
        var problemDetails = new Mock<IProblemDetailsService>();
        problemDetails
            .Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(ctx => callback?.Invoke(ctx))
            .ReturnsAsync(true);

        var logger  = new Mock<ILogger<DomainExceptionHandler>>();
        var handler = new DomainExceptionHandler(problemDetails.Object, logger.Object);
        var context = new DefaultHttpContext();
        return (handler, context);
    }

    private static (DomainExceptionHandler Handler, HttpContext Context, Mock<ITokenStore> TokenStore) CreateSutWithTokenStore()
    {
        var (handler, context) = CreateSut();

        var tokenStore = new Mock<ITokenStore>();
        var services = new Mock<IServiceProvider>();
        services
            .Setup(s => s.GetService(typeof(ITokenStore)))
            .Returns(tokenStore.Object);
        context.RequestServices = services.Object;

        return (handler, context, tokenStore);
    }
}
