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
}