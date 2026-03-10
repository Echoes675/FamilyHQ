using System.Text.Json.Serialization;

namespace FamilyHQ.Simulator.DTOs;

public class GoogleDateTime
{
    [JsonPropertyName("dateTime")]
    public DateTime? DateTime { get; set; }
    [JsonPropertyName("date")]
    public string? Date { get; set; }
}