namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// The event identified by <see cref="EventId"/> does not exist. Maps to HTTP 404.
/// </summary>
public sealed class EventNotFoundException : NotFoundException
{
    public Guid EventId { get; }

    public EventNotFoundException(Guid eventId)
        : base($"Event {eventId} not found.")
    {
        EventId = eventId;
    }
}
