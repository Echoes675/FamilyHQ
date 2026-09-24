using System.Text.Json.Serialization;

namespace FamilyHQ.Simulator.DTOs;

// FHQ-207: body for the backdoor that changes a calendar's Google-side default reminders mid-run.
// A null/absent Overrides means "Google reports no defaults for this calendar" — the state every
// E2E calendar starts in, and the left-hand side of the null -> value transition that took
// production down in FHQ-205.
public sealed record SetCalendarDefaultRemindersRequest(
    [property: JsonPropertyName("overrides")] List<GoogleEventReminderOverride>? Overrides);
