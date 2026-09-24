namespace FamilyHQ.Smoke.Data.Models;

/// <summary>
/// One month of preprod's calendar, keyed by <c>yyyy-MM-dd</c>. A multi-day event appears under every
/// date it spans, so anything counting occurrences must de-duplicate by Google event id.
/// </summary>
public sealed class PreprodMonthView
{
    public int Year { get; set; }

    public int Month { get; set; }

    public Dictionary<string, List<PreprodEvent>> Days { get; set; } = [];
}
