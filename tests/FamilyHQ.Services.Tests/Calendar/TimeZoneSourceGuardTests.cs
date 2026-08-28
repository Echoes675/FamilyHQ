using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// FHQ-178 / FHQ-179. Guards the invariant the fix rests on: <b>nothing may feed the zone FamilyHQ
/// sends to Google except sources that describe the family.</b>
/// <para>
/// The original defect was an ip-api call made <em>from the WebApi container</em>, which geolocates
/// the hosting VPS — production returned <c>Europe/Berlin</c> for a household in Derry. That is
/// structural: no server-side IP lookup can identify where a family lives, in any deployment. And it
/// does not stay cosmetic — <c>GetSendZoneAsync</c> supplies <c>familyZone</c> when
/// <c>GoogleCalendarClient</c> creates an event, so a guessed zone is stamped onto the family's
/// calendar and read back on their phones. AGENTS.md: an edit must change what the user asked to
/// change, and nothing else.
/// </para>
/// <para>
/// FHQ-179 then deleted the ip-api client outright, so this can no longer name the offending type.
/// That is why the guard is an <b>allowlist rather than a denylist</b>: naming what is permitted
/// catches any newly-introduced source, including a re-added geolocation client under a different
/// name. A denylist would only have caught the one we already knew about.
/// </para>
/// <para>
/// Structural rather than behavioural because the failure mode is a plausible future edit, not a bug
/// in today's logic. Re-adding IP detection would look like an improvement — "fall back to
/// auto-detect when the kiosk has not reported yet" — and every existing behavioural test would
/// still pass.
/// </para>
/// </summary>
public class TimeZoneSourceGuardTests
{
    /// <summary>
    /// Every dependency permitted to influence the zone, and why each one describes the FAMILY:
    /// their own saved location, the kiosk's own OS zone and explicit choice, a bundled coordinate
    /// lookup, and the caller's identity.
    /// </summary>
    private static readonly HashSet<Type> PermittedSources =
    [
        typeof(ICurrentUserService),          // whose zone we are resolving
        typeof(IDisplaySettingRepository),    // the kiosk's reported zone, and any explicit choice
        typeof(ILocationSettingRepository),   // the location the family typed in themselves
        typeof(ITimeZoneLookup),              // coordinates -> zone, bundled GeoTimeZone data, no network
    ];

    [Fact]
    public void TimeZoneService_TakesNoSourceOutsideTheApprovedSet()
    {
        var dependencies = typeof(TimeZoneService)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .Distinct()
            .ToList();

        dependencies.Should().OnlyContain(d => PermittedSources.Contains(d),
            "this service's zone reaches Google event creation, so every input to it must describe " +
            "the FAMILY. A geolocation client added here would describe the datacentre — that was " +
            "FHQ-178, and it stamped Europe/Berlin onto a household in Derry. If a new source is " +
            "genuinely needed, add it to PermittedSources deliberately, with a reason.");
    }

    [Fact]
    public void TimeZoneService_StillTakesEverySourceItNeeds()
    {
        // The other half: an allowlist that has quietly lost an entry would pass the test above
        // while the service silently stopped consulting the family's own saved location.
        var dependencies = typeof(TimeZoneService)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        dependencies.Should().Contain(typeof(ILocationSettingRepository));
        dependencies.Should().Contain(typeof(IDisplaySettingRepository));
        dependencies.Should().Contain(typeof(ITimeZoneLookup));
    }
}
