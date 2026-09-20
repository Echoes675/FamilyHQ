using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// FHQ-196. The two operations a registration address needs: a hash that can be stored next to the
/// channel to answer "was this channel registered for the address we are configured for now?", and
/// a masked rendering for the log line that reports a change.
/// <para>
/// The digests below are hard-coded rather than recomputed with <c>SHA256</c> in the test: a test
/// that hashes the same input with the same primitive as the implementation asserts only that the
/// two agree, which they would even if both were wrong.
/// </para>
/// </summary>
public class WebhookAddressTests
{
    [Theory]
    [InlineData("https://familyhq.example.com/api/sync/webhook",
        "b6b02363fac419d5948c31b0c09b500cb6e260eaa39d0bb5b5c1fa569b54c426")]
    [InlineData("http://webapi:8080/api/sync/webhook",
        "273150a907df44654766a0f352aa471c7a68ef7393733e8de1bda203f43d76d8")]
    public void Hash_ReturnsTheSha256OfTheAddressAsLowercaseHex(string address, string expected)
    {
        WebhookAddress.Hash(address).Should().Be(expected);
    }

    [Fact]
    public void Hash_AddressesDifferingOnlyInTheRouteKey_ProduceDifferentHashes()
    {
        // The whole point of the column: a rotated route key must read as a different address.
        var first = WebhookAddress.Hash("https://relay.example.com/h/route-key-one/api/sync/webhook");
        var second = WebhookAddress.Hash("https://relay.example.com/h/route-key-two/api/sync/webhook");

        first.Should().NotBe(second);
    }

    [Fact]
    public void Mask_ReplacesTheRouteKeySegmentAndKeepsTheRest()
    {
        WebhookAddress.Mask("https://relay.example.com/h/s3cr3t-route-key/api/sync/webhook")
            .Should().Be("https://relay.example.com/h/***/api/sync/webhook");
    }

    [Fact]
    public void Mask_RouteKeyIsTheLastSegment_StillMasksIt()
    {
        WebhookAddress.Mask("https://relay.example.com/h/s3cr3t-route-key")
            .Should().Be("https://relay.example.com/h/***");
    }

    [Fact]
    public void Mask_AddressWithoutARouteKey_IsUnchanged()
    {
        // Direct addresses (prod today, and the Simulator in dev/staging) carry no credential in
        // the path, and masking must not mangle them into something unreadable in Seq.
        WebhookAddress.Mask("http://webapi:8080/api/sync/webhook")
            .Should().Be("http://webapi:8080/api/sync/webhook");
    }
}
