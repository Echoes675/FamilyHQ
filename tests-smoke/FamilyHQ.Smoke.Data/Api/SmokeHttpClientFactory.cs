using FamilyHQ.Smoke.Common.Configuration;

namespace FamilyHQ.Smoke.Data.Api;

/// <summary>
/// Builds the suite's HTTP clients.
/// <para>
/// One decision worth stating: certificate validation is <b>on</b> unless
/// <c>Smoke__AllowUntrustedCertificate</c> says otherwise. The E2E suite waves certificate errors
/// through because it talks to a dev certificate on localhost; preprod is a real environment behind an
/// internal CA, and the fix for "the chain does not validate on the agent" is to trust that CA, not to
/// stop checking. The flag exists so an exploratory run from a laptop is possible, and it defaults off
/// so a pipeline cannot quietly acquire that posture.
/// </para>
/// </summary>
public static class SmokeHttpClientFactory
{
    public static HttpClient Create(SmokeConfiguration configuration, string baseAddress)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var handler = new HttpClientHandler();
        if (configuration.AllowUntrustedCertificate)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        return new HttpClient(handler)
        {
            BaseAddress = new Uri(baseAddress.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMilliseconds(configuration.DefaultTimeoutMs)
        };
    }
}
