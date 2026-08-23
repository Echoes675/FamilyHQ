using System.Net;
using FamilyHQ.Services.Auth;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Auth;

/// <summary>
/// FHQ-173. The "may Google already have processed this?" predicate, tested on its own because two
/// call sites turn on it in OPPOSITE directions and a drift between them would be silent:
/// <c>ResilientGoogleCalendarClient.ShouldRetry</c> refuses to repeat a may-have-been-processed
/// failure for a non-idempotent operation, while <c>CalendarEventService</c>'s split compensator
/// refuses to UNDO one. A shape misclassified as "rejected" makes the compensator delete a forward
/// series whose truncation actually committed — silent, permanent data loss on Google.
/// <para>
/// The load-bearing property is the DEFAULT: anything that is not a status code Google itself
/// returned must come back true.
/// </para>
/// </summary>
public class GoogleWriteOutcomeTests
{
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void MayHaveBeenProcessed_GoogleApi5xx_IsTrue(HttpStatusCode status) =>
        GoogleWriteOutcome.MayHaveBeenProcessed(new GoogleApiException(status, "op")).Should().BeTrue(
            "Google's own side failed, and it may have failed AFTER applying the change");

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public void MayHaveBeenProcessed_GoogleApi4xx_IsFalse(HttpStatusCode status) =>
        GoogleWriteOutcome.MayHaveBeenProcessed(new GoogleApiException(status, "op")).Should().BeFalse(
            "the request reached Google, was understood and was refused, so nothing was written");

    [Fact]
    public void MayHaveBeenProcessed_Reauth_IsFalse() =>
        GoogleWriteOutcome.MayHaveBeenProcessed(
                new GoogleReauthRequiredException(GoogleAuthFailureSource.CalendarApi, "invalid_grant"))
            .Should().BeFalse("the call never got past authorisation");

    [Fact]
    public void MayHaveBeenProcessed_HttpClientAttemptTimeout_IsTrue() =>
        // FHQ-91's shape. It derives from OperationCanceledException, which is exactly why callers
        // must not classify it by type — the request may have reached Google and been processed
        // with only the response lost.
        GoogleWriteOutcome.MayHaveBeenProcessed(new TaskCanceledException("timeout", new TimeoutException()))
            .Should().BeTrue();

    [Fact]
    public void MayHaveBeenProcessed_DroppedConnection_IsTrue() =>
        GoogleWriteOutcome.MayHaveBeenProcessed(new HttpRequestException("connection reset")).Should().BeTrue();

    [Fact]
    public void MayHaveBeenProcessed_Cancellation_IsTrue() =>
        GoogleWriteOutcome.MayHaveBeenProcessed(new OperationCanceledException()).Should().BeTrue(
            "abandoning the wait does not recall a request Google may already have applied");

    [Fact]
    public void MayHaveBeenProcessed_UnrecognisedException_IsTrue() =>
        // The default is the whole point: a shape nobody has classified is ambiguous, and ambiguity
        // must resolve to the answer whose wrong case is recoverable.
        GoogleWriteOutcome.MayHaveBeenProcessed(new InvalidOperationException("something new"))
            .Should().BeTrue();
}
