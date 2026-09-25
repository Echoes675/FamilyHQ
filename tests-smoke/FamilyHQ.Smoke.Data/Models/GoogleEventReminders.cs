using System.Text.Json.Serialization;

namespace FamilyHQ.Smoke.Data.Models;

/// <summary>
/// An event's reminder configuration. <c>overrides</c> is absent — not empty — when an event has none,
/// so the list stays nullable and "absent" is never silently read as "no reminders". FamilyHQ does read
/// and write this field (FHQ-189, FHQ-205), which makes it a real golden-rule probe rather than an inert
/// one.
/// </summary>
public sealed record GoogleEventReminders(
    [property: JsonPropertyName("useDefault")] bool? UseDefault,
    [property: JsonPropertyName("overrides")] IReadOnlyList<GoogleEventReminderOverride>? Overrides);
