using Microsoft.JSInterop;

namespace FamilyHQ.WebUi.Services;

public interface IKioskTimeZoneReporter
{
    Task ReportAsync();
}

/// <summary>
/// FHQ-178: reports the zone the kiosk's own operating system is set to.
/// <para>
/// This exists because the server cannot work it out. The previous automatic source was an ip-api
/// call made from the WebApi container, which geolocates the <em>hosting VPS</em> — production
/// reported <c>Europe/Berlin</c> for a household in Derry. That is structural, not bad luck: no
/// server-side IP lookup can identify where a family lives. The kiosk, by contrast, is physically in
/// their house, so its OS zone is the one automatic answer that describes them — and reading it costs
/// no network call, no third party, and sends nothing about their location anywhere.
/// </para>
/// <para>
/// Reported on every load rather than stored once, so changing the kiosk's timezone propagates by
/// itself. The server ignores the report when the family has chosen an explicit zone.
/// </para>
/// </summary>
public class KioskTimeZoneReporter(
    IJSRuntime jsRuntime,
    ISettingsApiService settingsApi,
    ILogger<KioskTimeZoneReporter> logger) : IKioskTimeZoneReporter
{
    public async Task ReportAsync()
    {
        // Never let this break startup. It runs on a wall display during boot, and a zone that fails
        // to report leaves the previous value in place — which is a worse outcome than a blank screen
        // only if it silently persists, and it does not: the server keeps whatever it already had.
        try
        {
            await using var module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/theme.js");
            var zone = await module.InvokeAsync<string?>("getKioskTimeZone");

            if (string.IsNullOrWhiteSpace(zone))
            {
                logger.LogDebug("Kiosk did not report an OS time zone; leaving the stored zone unchanged.");
                return;
            }

            await settingsApi.ReportKioskTimeZoneAsync(zone);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reporting the kiosk time zone failed; the stored zone is unchanged.");
        }
    }
}
