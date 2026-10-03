using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.DTOs;

public record CalendarEventDto(
    Guid Id,
    string GoogleEventId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    IReadOnlyList<EventCalendarDto> Members,
    // FHQ-18: recurrence projection so the UI can pre-populate the picker on edit and decide
    // whether to show the scope prompt. IsRecurring mirrors CalendarEvent.IsRecurring; the
    // RRULE seeds RecurrencePicker. Defaulted so existing non-recurrence callers are unaffected.
    bool IsRecurring = false,
    string? RecurrenceRule = null,
    // Google's reminders object for this event, in Google's own shape and exactly as stored — which
    // is what Google returned, not what the kiosk sent. The reminder tab opens from this and has no
    // other source.
    //
    // NULL means "never synced", NOT "no reminders": the four states (never synced, follows the
    // calendar's defaults, an explicit list, explicitly none) are four different things to Google on
    // a subsequent write, so none of them may be mapped onto another here. In particular an absent
    // object must not become an empty list.
    //
    // Nothing is normalised on the way out. A delivery method the kiosk's form cannot offer, or an
    // offset it would never choose, can still have been set from a phone; Google is the authority on
    // its own data, and the kiosk's validation applies only to values the kiosk itself creates.
    EventReminders? Reminders = null,
    // The calendar this event actually lives on, as the server stored it. The client used to infer
    // this from the member list by re-implementing the server's routing rule, which agreed only
    // because exactly one calendar is ever shared. A value that is read cannot drift from the value
    // it mirrors.
    Guid? OwningCalendarId = null,
    // That calendar's own default reminders, so the reminders tab can describe what inheriting does
    // without a second request.
    EventReminders? OwningCalendarDefaultReminders = null);
