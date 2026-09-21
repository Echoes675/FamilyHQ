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

    // The calendar's own default IANA zone (Google's calendar-resource `timeZone`, surfaced on every
    // calendarList entry) — the zone Google itself applies to an event on this calendar that carries
    // none of its own. FHQ-164: the last Google-supplied rung of the series-zone discovery ladder.
    // Null until a calendar-list sync reports one; never defaulted.
    public string? IanaTimeZone { get; set; }

    // FHQ-189: the calendar's default reminders, as Google reports them on the calendarList entry
    // ("the default reminders that the authenticated user has for this calendar"). Needed to show
    // what a `useDefault` event will actually do. Null until a calendar-list sync reports one.
    //
    // Google's own UI keeps a SEPARATE default for all-day events; the API does not expose it
    // (FHQ-193 Q2), so this is the timed default only.
    public EventReminders? DefaultReminders { get; set; }

    // Navigation properties
    public SyncState? SyncState { get; set; }
}
