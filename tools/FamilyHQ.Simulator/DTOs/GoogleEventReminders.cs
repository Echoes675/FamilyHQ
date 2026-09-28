using System.Text.Json.Serialization;

namespace FamilyHQ.Simulator.DTOs;

// FHQ-189 (I3): mirrors Google's `reminders` object shape so a round trip through the simulator is
// coherent — accept what the app sends, store it, and hand the same shape back. This is only the
// shape: Google's behaviour (clamping, de-duplication, dropping an unrecognised method, the two
// rejections, and how a read is shaped) lives in ReminderSemantics, which every path goes through.
public sealed record GoogleEventReminders(
    [property: JsonPropertyName("useDefault")] bool? UseDefault,
    // Omitted (not emitted as `null`) when there is nothing to report, matching how Google omits
    // the key entirely for the useDefault:false/explicitly-none state.
    [property: JsonPropertyName("overrides")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    List<GoogleEventReminderOverride>? Overrides);
