namespace FamilyHQ.Core.Interfaces;

/// <summary>
/// Resolves an IANA time-zone identifier into an <see cref="IRecurrenceTimeZone"/> (FHQ-161).
/// </summary>
/// <remarks>
/// Every implementation reaches a tz database through a static entry point
/// (<c>DateTimeZoneProviders.Tzdb</c>). Wrapping it behind this injectable seam is what lets callers
/// such as <c>CalendarEventService</c> be unit-tested without one, and what keeps
/// <c>FamilyHQ.Core</c> free of a tz-database package reference. Implementations are stateless and
/// thread-safe, so they register as singletons.
/// </remarks>
public interface IRecurrenceTimeZoneFactory
{
    /// <summary>
    /// The zone for <paramref name="ianaTimeZoneId"/>, or <c>null</c> when the id is null, blank, or
    /// absent from the tz database. Never throws — an unknown zone is a caller decision, not a fault.
    /// </summary>
    IRecurrenceTimeZone? TryCreate(string? ianaTimeZoneId);
}
