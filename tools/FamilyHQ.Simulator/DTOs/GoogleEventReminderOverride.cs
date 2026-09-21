using System.Text.Json.Serialization;

namespace FamilyHQ.Simulator.DTOs;

// FHQ-189 (I3): one entry of GoogleEventReminders.Overrides, and the same shape Google's
// calendarList `defaultReminders` array uses.
public sealed record GoogleEventReminderOverride(
    [property: JsonPropertyName("method")]  string? Method,
    [property: JsonPropertyName("minutes")] int?    Minutes);
