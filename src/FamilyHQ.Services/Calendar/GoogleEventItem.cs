using System.Text.Json.Serialization;

namespace FamilyHQ.Services.Calendar;

internal class GoogleEventItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("location")] public string? Location { get; set; }
    [JsonPropertyName("start")] public EventDateTime? Start { get; set; }
    [JsonPropertyName("end")] public EventDateTime? End { get; set; }
}
