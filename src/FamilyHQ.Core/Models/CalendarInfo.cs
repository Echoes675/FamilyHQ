namespace FamilyHQ.Core.Models;

public class CalendarInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = null!;
    public string GoogleCalendarId { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Color { get; set; }
    public bool IsVisible { get; set; } = true;

    // Marks this calendar as the shared calendar used for multi-member events.
    public bool IsShared { get; set; } = false;

    // FHQ-61: false once Google reports the calendar can't have push notifications
    // (pushNotSupportedForRequestedResource) — read-only/subscribed calendars. Skipped by webhook registration.
    public bool WebhooksSupported { get; set; } = true;

    // Order of this calendar's column in the Agenda view (0 = leftmost).
    public int DisplayOrder { get; set; } = 0;

    // Navigation properties
    public SyncState? SyncState { get; set; }
}
