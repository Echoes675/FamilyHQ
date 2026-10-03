namespace FamilyHQ.Core.Reminders;

/// <summary>
/// One notification a phone will make: the moment it fires, how it fires, and the offset Google
/// stores it as.
/// </summary>
/// <param name="TriggerAt">When the phone goes off. NOT what the reminders timeline files a row by —
/// that is the event's own start; this is only how the soonest reminder still to fire is picked.</param>
/// <param name="Method">Google's own value, unchanged — "popup", "email", or something this
/// application cannot create. Never normalised: Google is the authority on its own data.</param>
/// <param name="Minutes">The stored offset, kept so a row can say "30 min before" without
/// re-deriving it from two timestamps.</param>
public record ReminderPing(DateTimeOffset TriggerAt, string Method, int Minutes);
