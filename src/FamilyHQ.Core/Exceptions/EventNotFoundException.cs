namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// The event identified by <see cref="EventId"/> does not exist. Maps to HTTP 404.
/// </summary>
/// <remarks>
/// FHQ-175: carries a user message because a stale kiosk editing an event that was deleted from a
/// phone is an everyday family scenario, and "please try again" is the wrong advice for it — the
/// event is gone and no retry brings it back.
/// </remarks>
public sealed class EventNotFoundException : NotFoundException
{
    public const string UserFacingMessage =
        "This event no longer exists — it may have been deleted from another device.";

    public Guid EventId { get; }

    public EventNotFoundException(Guid eventId)
        : base($"Event {eventId} not found.", UserFacingMessage)
    {
        EventId = eventId;
    }
}
