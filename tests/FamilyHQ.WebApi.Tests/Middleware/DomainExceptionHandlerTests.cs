using FamilyHQ.Core.Exceptions;
using FamilyHQ.Services.Auth;
using FamilyHQ.WebApi.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
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
            new GoogleApiException(HttpStatusCode.BadRequest, "PatchEventFields", "{\"error\":{\"message\":\"Invalid start time.\"}}"),
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
            new GoogleApiException(HttpStatusCode.BadRequest, "PatchEventFields", "raw-google-body-SECRET"),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ProblemDetails.Title.Should().Be("Upstream Calendar Error");
        captured.ProblemDetails.Status.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Fact]
    public async Task GoogleApiException_Detail_DoesNotLeakGoogleResponseBody()
    {
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(
            context,
            new GoogleApiException(HttpStatusCode.BadRequest, "PatchEventFields", "raw-google-body-SECRET"),
            CancellationToken.None);

        captured!.ProblemDetails.Detail.Should().Be("The calendar provider rejected the request.");
        captured.ProblemDetails.Detail.Should().NotContain("SECRET");
    }

    [Fact]
    public async Task GoogleApiException_429_MapsTo503WithRetryAfterHeader()
    {
        var (handler, context) = CreateSut();

        var handled = await handler.TryHandleAsync(
            context,
            new GoogleApiException(HttpStatusCode.TooManyRequests, "GetCalendars", "rate limited", TimeSpan.FromSeconds(30)),
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
            new GoogleApiException(HttpStatusCode.TooManyRequests, "GetCalendars", "rate limited"),
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
            new GoogleApiException(HttpStatusCode.Forbidden, "GetCalendars", "rate limited", TimeSpan.FromSeconds(30)),
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
            new GoogleApiException(HttpStatusCode.BadRequest, "PatchEventFields", "{\"error\":{\"message\":\"Invalid start time.\"}}"),
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
            new GoogleReauthRequiredException(GoogleAuthFailureSource.TokenRefresh, "Token has been expired or revoked.", "raw-google-body-SECRET"),
            CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task GoogleReauthRequiredException_CarriesReauthExtensions_AndNoBodyLeak()
    {
        ProblemDetailsContext? captured = null;
        var (handler, context) = CreateSut(ctx => captured = ctx);

        await handler.TryHandleAsync(
            context,
            new GoogleReauthRequiredException(GoogleAuthFailureSource.TokenRefresh, "revoked", "raw-google-body-SECRET"),
            CancellationToken.None);

        captured.Should().NotBeNull();
        var pd = captured!.ProblemDetails;
        pd.Title.Should().Be("Reconnect Google Calendar");
        pd.Extensions["code"].Should().Be("needs_reauth");
        pd.Extensions["source"].Should().Be("token_refresh");
        pd.Extensions["reconnectUrl"].Should().Be("/api/auth/login");
        pd.Detail.Should().NotContain("SECRET");
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
}
