namespace FamilyHQ.Smoke.Common.Pages;

/// <summary>
/// Which occurrences of a series an edit or a delete is meant to apply to — the choice the kiosk asks
/// for whenever a recurring event is saved or deleted.
/// <para>
/// The three scopes reach Google by three completely different routes: one patches a single instance
/// into an exception, one truncates the original series and creates a replacement from the split
/// point, and one patches the master. They are not three settings of one operation, which is why each
/// has a scenario of its own.
/// </para>
/// </summary>
public enum SmokeRecurrenceScope
{
    /// <summary>This occurrence only.</summary>
    ThisEvent,

    /// <summary>This occurrence and every later one.</summary>
    ThisAndFollowing,

    /// <summary>Every occurrence in the series.</summary>
    AllEvents
}
