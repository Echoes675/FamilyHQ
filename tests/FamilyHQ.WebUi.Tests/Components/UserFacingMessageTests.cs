using System.Net;
using FamilyHQ.WebUi.Components;
using FamilyHQ.WebUi.Services;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components;

/// <summary>
/// FHQ-175. The cap-and-fallback shape every server string passes through on its way to the kiosk
/// (extracted from ReauthBanner), and the catch-site rule built on it: render a server-vetted
/// userMessage, otherwise the component's own generic fallback — and only the fallback may say
/// "please try again".
/// </summary>
public class UserFacingMessageTests
{
    private const string Fallback = "Couldn't save your event — please try again.";

    [Fact]
    public void Format_NullMessage_ReturnsFallback()
    {
        UserFacingMessage.Format(null, Fallback).Should().Be(Fallback);
    }

    [Fact]
    public void Format_WhitespaceMessage_ReturnsFallback()
    {
        UserFacingMessage.Format("   ", Fallback).Should().Be(Fallback);
    }

    [Fact]
    public void Format_ShortMessage_ReturnsItTrimmed()
    {
        UserFacingMessage.Format("  Token revoked.  ", Fallback).Should().Be("Token revoked.");
    }

    [Fact]
    public void Format_MessageExactlyAtCap_IsNotTruncated()
    {
        var atCap = new string('x', UserFacingMessage.MaxLength);

        UserFacingMessage.Format(atCap, Fallback).Should().Be(atCap);
    }

    [Fact]
    public void Format_OverflowingMessage_IsCappedWithEllipsis()
    {
        var overflowing = new string('x', UserFacingMessage.MaxLength + 50);

        var result = UserFacingMessage.Format(overflowing, Fallback);

        result.Length.Should().Be(UserFacingMessage.MaxLength + 1);
        result.Should().EndWith("…");
        result[..UserFacingMessage.MaxLength].Should().Be(new string('x', UserFacingMessage.MaxLength));
    }

    [Fact]
    public void Format_OverflowingMessage_TrimsTrailingSpaceBeforeEllipsis()
    {
        var overflowing = new string('x', UserFacingMessage.MaxLength - 1) + " " + new string('y', 50);

        var result = UserFacingMessage.Format(overflowing, Fallback);

        result.Should().Be(new string('x', UserFacingMessage.MaxLength - 1) + "…");
    }

    [Fact]
    public void ForFailure_ApiExceptionWithUserMessage_RendersItAndDropsTheRetryHint()
    {
        var exception = new CalendarApiException(
            HttpStatusCode.Conflict,
            new ApiProblem("Reconnect Google Calendar", "Your Google connection needs to be re-authorised.", "needs_reauth"));

        var result = UserFacingMessage.ForFailure(exception, Fallback);

        result.Should().Be("Your Google connection needs to be re-authorised.");
        result.Should().NotContain("try again", "a vetted message carries its own advice");
    }

    [Fact]
    public void ForFailure_ApiExceptionWithoutUserMessage_ReturnsFallback()
    {
        // A non-opted-in failure: title present, no userMessage. Title/Detail never reach the screen.
        var exception = new CalendarApiException(
            HttpStatusCode.BadRequest,
            new ApiProblem("Validation Failed", null, null));

        UserFacingMessage.ForFailure(exception, Fallback).Should().Be(Fallback);
    }

    [Fact]
    public void ForFailure_ApiExceptionWithBlankUserMessage_ReturnsFallback()
    {
        var exception = new CalendarApiException(
            HttpStatusCode.BadRequest,
            new ApiProblem("Validation Failed", "   ", null));

        UserFacingMessage.ForFailure(exception, Fallback).Should().Be(Fallback);
    }

    [Fact]
    public void ForFailure_ApiExceptionWithOverlongUserMessage_IsCapped()
    {
        var exception = new CalendarApiException(
            HttpStatusCode.BadRequest,
            new ApiProblem(null, new string('m', UserFacingMessage.MaxLength + 10), null));

        var result = UserFacingMessage.ForFailure(exception, Fallback);

        result.Length.Should().Be(UserFacingMessage.MaxLength + 1);
        result.Should().EndWith("…");
    }

    [Fact]
    public void ForFailure_TransportFailure_ReturnsFallback()
    {
        // A dead socket carries no server text; its message must not be shown either.
        var exception = new HttpRequestException("connection refused to https://internal-host-SECRET");

        var result = UserFacingMessage.ForFailure(exception, Fallback);

        result.Should().Be(Fallback);
        result.Should().NotContain("SECRET");
    }

    [Fact]
    public void ForFailure_ArbitraryException_ReturnsFallbackNotItsMessage()
    {
        var exception = new InvalidOperationException("Unhandled immediate save action: 7.");

        UserFacingMessage.ForFailure(exception, Fallback).Should().Be(Fallback);
    }
}
