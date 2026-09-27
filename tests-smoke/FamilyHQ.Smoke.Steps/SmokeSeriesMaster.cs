namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// A series master Google holds, together with the calendar it is on.
/// <para>
/// Both halves are needed to ask Google to expand it, and a scenario can legitimately be about two of
/// them: a "this and following" change ends with the original series truncated and a replacement series
/// carrying the rest, and the occurrence set the family sees is the union of the two.
/// </para>
/// </summary>
public sealed record SmokeSeriesMaster(string CalendarName, string GoogleEventId);
