using System.Security.Cryptography;
using System.Text;

namespace FamilyHQ.Services.Calendar;

/// <summary>
/// FHQ-196. The address FamilyHQ registers a Google watch channel with, in the two forms nothing
/// else may use it in: a hash for storage and a masked rendering for a log line.
/// <para>
/// <b>Why a hash and not the address.</b> A RelayRobin address embeds its route key in the path
/// (<c>https://…/h/&lt;route key&gt;/api/sync/webhook</c>), and that key is the credential which
/// authorises posting a notification to FamilyHQ. The question the registration row has to answer
/// is only "is this channel registered for the address we are configured for now?", and a digest
/// answers it exactly, without putting the credential in a second place.
/// </para>
/// <para>
/// <b>The crypto primitive is called statically on purpose.</b> <c>SHA256.HashData</c> is a pure
/// function of its argument, so there is no ambient non-determinism for a seam to control, and a
/// substitutable digest would let a test assert the opposite of what production computes. See the
/// "Wrapping static calls" exemption in <c>.agent/skills/coding-standards/SKILL.md</c>.
/// </para>
/// </summary>
public static class WebhookAddress
{
    /// <summary>Path prefix that introduces a RelayRobin route key.</summary>
    private const string RouteKeyMarker = "/h/";

    /// <summary>Stands in for the route key in anything that reaches a log sink.</summary>
    private const string MaskedRouteKey = "***";

    /// <summary>Lowercase hex SHA-256 of the full registration address.</summary>
    public static string Hash(string address) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(address)));

    /// <summary>
    /// The address with its route key replaced, for logging. An address that carries no route key
    /// (a direct address, or the Simulator in dev and staging) is returned unchanged — there is no
    /// credential in it, and mangling it would cost the diagnostics the line exists for.
    /// </summary>
    public static string Mask(string address)
    {
        var markerIndex = address.IndexOf(RouteKeyMarker, StringComparison.Ordinal);

        if (markerIndex < 0)
        {
            return address;
        }

        var keyStart = markerIndex + RouteKeyMarker.Length;
        var keyEnd = address.IndexOf('/', keyStart);

        return keyEnd < 0
            ? string.Concat(address.AsSpan(0, keyStart), MaskedRouteKey)
            : string.Concat(address.AsSpan(0, keyStart), MaskedRouteKey, address.AsSpan(keyEnd));
    }
}
