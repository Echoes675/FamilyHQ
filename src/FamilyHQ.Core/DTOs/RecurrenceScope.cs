namespace FamilyHQ.Core.DTOs;

/// <summary>
/// The Google-parity scope of a recurring-event edit or delete.
/// Mirrors Google Calendar's three-way prompt verbatim.
/// </summary>
public enum RecurrenceScope
{
    /// <summary>Affects only the single instance the user opened (Google "This event").</summary>
    ThisOnly,

    /// <summary>Affects this instance and every later instance (Google "This and following events").</summary>
    ThisAndFollowing,

    /// <summary>Affects the whole series (Google "All events").</summary>
    AllInSeries
}
