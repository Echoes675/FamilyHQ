using System.Text.Json.Serialization;

namespace FamilyHQ.Simulator.DTOs;

public class GoogleEventRequest
{
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }
    [JsonPropertyName("location")]
    public string? Location { get; set; }
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    [JsonPropertyName("start")]
    public GoogleDateTime Start { get; set; } = new();
    [JsonPropertyName("end")]
    public GoogleDateTime End { get; set; } = new();

    [JsonPropertyName("extendedProperties")]
    public GoogleEventExtendedPropertiesRequest? ExtendedProperties { get; set; }

    // FHQ-18.11 WRITE side: the recurrence array the app sends on events.insert (new series) and
    // events.patch (toggle ON / change RRULE / toggle OFF). Google packs RRULE/EXDATE/RDATE lines
    // here; FamilyHQ only ever emits a single "RRULE:…" line. The distinction the simulator must
    // honour:
    //   • null          → field absent from the body: leave any existing RecurrenceRule untouched.
    //   • empty array    → the explicit toggle-OFF/collapse call: clear RecurrenceRule.
    //   • ["RRULE:…"]    → set/replace RecurrenceRule with the first RRULE line.
    [JsonPropertyName("recurrence")]
    public List<string>? Recurrence { get; set; }

    public class GoogleEventExtendedPropertiesRequest
    {
        [JsonPropertyName("private")]
        public Dictionary<string, string>? Private { get; set; }
    }
}