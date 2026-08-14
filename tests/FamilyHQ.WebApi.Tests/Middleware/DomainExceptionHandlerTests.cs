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
