namespace FamilyHQ.Smoke.Common.Pages;

/// <summary>
/// The repeat frequencies the smoke suite drives the kiosk's recurrence picker to.
/// <para>
/// Only the three the suite actually asks for. Each one is here because some third-party behaviour
/// depends on it: a daily series is what can be made to straddle a daylight-saving transition inside
/// three occurrences, a weekly one carries <c>BYDAY</c> and an interval, and a yearly one is the shape
/// whose anchor Google has to reproduce a whole year later.
/// </para>
/// </summary>
public enum SmokeRecurrenceFrequency
{
    Daily,
    Weekly,
    Yearly
}
