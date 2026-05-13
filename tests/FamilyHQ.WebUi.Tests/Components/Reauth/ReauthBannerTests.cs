using FamilyHQ.Core.DTOs;
using FamilyHQ.WebUi.Components.Reauth;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Reauth;

public class ReauthBannerTests
{
    [Fact]
    public void ShouldShow_NullStatus_ReturnsFalse()
    {
        ReauthBanner.ShouldShow(null).Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_ActiveStatus_ReturnsFalse()
    {
        var status = new ConnectionStatusDto("active", null, null);
        ReauthBanner.ShouldShow(status).Should().BeFalse();
    }

    [Fact]
    public void ShouldShow_NeedsReauthStatus_ReturnsTrue()
    {
        var status = new ConnectionStatusDto("needs_reauth", "Token has been expired or revoked.", DateTimeOffset.UtcNow);
        ReauthBanner.ShouldShow(status).Should().BeTrue();
    }

    [Fact]
    public void ShouldShow_NeedsReauthStatus_IsCaseInsensitive()
    {
        var status = new ConnectionStatusDto("NEEDS_REAUTH", null, null);
        ReauthBanner.ShouldShow(status).Should().BeTrue();
    }

    [Fact]
    public void FormatMessage_NullError_ReturnsDefaultMessage()
    {
        ReauthBanner.FormatMessage(null).Should().Be(ReauthBanner.DefaultMessage);
    }

    [Fact]
    public void FormatMessage_EmptyError_ReturnsDefaultMessage()
    {
        ReauthBanner.FormatMessage("   ").Should().Be(ReauthBanner.DefaultMessage);
    }

    [Fact]
    public void FormatMessage_ShortError_ReturnsErrorVerbatim()
    {
        ReauthBanner.FormatMessage("Token revoked.").Should().Be("Token revoked.");
    }

    [Fact]
    public void FormatMessage_OverflowingError_IsTruncatedWithEllipsis()
    {
        var longError = new string('x', ReauthBanner.MaxErrorLength + 50);

        var result = ReauthBanner.FormatMessage(longError);

        result.Length.Should().BeLessThanOrEqualTo(ReauthBanner.MaxErrorLength + 1);
        result.Should().EndWith("…");
    }
}
