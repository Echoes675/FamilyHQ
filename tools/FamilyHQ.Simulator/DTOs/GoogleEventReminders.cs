using System.Text.Json.Serialization;

namespace FamilyHQ.Simulator.DTOs;

// FHQ-189 (I3): mirrors Google's `reminders` object shape so a round trip through the simulator is
// coherent — accept what the app sends, store it, and hand the same shape back. NOT a faithful
// implementation of Google's write semantics (clamping, de-duplication, dropping an unknown
// method, useDefault-vs-overrides validation) — that fidelity belongs to FHQ-192.
public sealed record GoogleEventReminders(
    [property: JsonPropertyName("useDefault")] bool? UseDefault,
    // Omitted (not emitted as `null`) when there is nothing to report, matching how Google omits
    // the key entirely for the useDefault:false/explicitly-none state.
    [property: JsonPropertyName("overrides")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    List<GoogleEventReminderOverride>? Overrides);
