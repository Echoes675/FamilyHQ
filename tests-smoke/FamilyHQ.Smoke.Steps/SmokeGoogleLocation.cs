using FamilyHQ.Smoke.Data.Models;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// An event Google holds, together with <b>which calendar it is on</b>.
/// <para>
/// The calendar is the assertion in most of the kiosk-to-Google scenarios — "written once, to the shared
/// calendar only" is a statement about placement, not about content — and Google's event resource does not
/// carry the calendar it was fetched from. So the pair travels together.
/// </para>
/// </summary>
public sealed record SmokeGoogleLocation(string CalendarName, GoogleEvent Event);
