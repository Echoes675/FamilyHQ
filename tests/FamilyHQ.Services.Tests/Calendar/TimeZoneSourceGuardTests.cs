using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// FHQ-178. Guards the invariant the fix rests on: <b>no server-side IP geolocation may reach the
/// zone FamilyHQ sends to Google.</b>
/// <para>
/// <c>ILocationService</c> calls ip-api <em>from the WebApi container</em>, so it geolocates the
/// hosting VPS, not the family — production returned <c>Europe/Berlin</c> for a household in Derry.
/// That is structural: no server-side IP lookup can identify where a family lives, in any deployment.
/// And the value does not stay cosmetic — <c>TimeZoneService.GetSendZoneAsync</c> supplies
/// <c>familyZone</c> when <c>GoogleCalendarClient</c> creates an event, so a guessed zone is stamped
/// onto the family's calendar and read back on their phones. AGENTS.md: an edit must change what the
/// user asked to change, and nothing else.
/// </para>
/// <para>
/// This is guarded structurally rather than behaviourally because the failure mode is a plausible
/// future edit, not a bug in today's logic. Re-adding IP detection would look like an improvement —
/// "fall back to auto-detect when the kiosk has not reported yet" — and every existing test would
/// still pass. The dependency is the thing to forbid, so the constructor is where to forbid it.
/// </para>
/// </summary>
public class TimeZoneSourceGuardTests
{
    [Fact]
    public void TimeZoneService_DoesNotDependOnIpGeolocation()
    {
        var dependencies = typeof(TimeZoneService)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        dependencies.Should().NotContain(typeof(ILocationService),
            "ip-api runs on the server and resolves the hosting datacentre, and this service's zone " +
            "reaches Google event creation. The kiosk reports its own OS zone instead " +
            "(SetKioskZoneAsync); when it has not, the correct answer is no zone, not a guessed one.");
    }

    [Fact]
    public void GoogleWriteZone_ComesFromTheKioskOrTheFamilysOwnLocation_Only()
    {
        // Documents the whole permitted set in one place, so a reviewer can see what the zone may be
        // derived from without reading the service.
        var dependencies = typeof(TimeZoneService)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        dependencies.Should().Contain(typeof(ILocationSettingRepository),
            "the family's own saved location is a legitimate source — they typed it");
        dependencies.Should().Contain(typeof(IDisplaySettingRepository),
            "the kiosk's reported OS zone and any explicit choice are stored here");
        dependencies.Should().Contain(typeof(ITimeZoneLookup),
            "coordinates are turned into a zone with bundled GeoTimeZone data, not a network call");
    }
}
