namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// A recurring-scope operation was requested for an event that is not part of a recurring series.
/// The caller should use the single-event channel instead. Maps to HTTP 400.
/// </summary>
public sealed class NotPartOfRecurringSeriesException : DomainValidationException
{
    public Guid EventId { get; }

    public NotPartOfRecurringSeriesException(Guid eventId)
        : base($"Event {eventId} is not part of a recurring series. Use the single-event channel for non-recurring events.")
    {
        EventId = eventId;
    }
}
