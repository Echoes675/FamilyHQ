using FamilyHQ.Core.Exceptions;
using FamilyHQ.WebApi.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
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

    private static (DomainExceptionHandler Handler, HttpContext Context) CreateSut()
    {
        var problemDetails = new Mock<IProblemDetailsService>();
        // Mirror the real service: when asked to write, it succeeds (returns true).
        problemDetails
            .Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .ReturnsAsync(true);

        var logger  = new Mock<ILogger<DomainExceptionHandler>>();
        var handler = new DomainExceptionHandler(problemDetails.Object, logger.Object);
        var context = new DefaultHttpContext();
        return (handler, context);
    }
}
