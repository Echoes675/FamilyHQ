using System.Text.Json.Serialization;

namespace FamilyHQ.Simulator.DTOs;

public class GoogleDateTime
{
    [JsonPropertyName("dateTime")]
    public DateTime? DateTime { get; set; }
    [JsonPropertyName("date")]
    public string? Date { get; set; }
    // IANA/UTC zone Google requires on a recurring timed event's start/end (FHQ-42).
    [JsonPropertyName("timeZone")]
    public string? TimeZone { get; set; }
}