using System.Text.Json.Serialization;

namespace FamilyHQ.Services.Calendar;

internal class EventsResponse
{
    [JsonPropertyName("items")] public List<GoogleEventItem> Items { get; set; } = new();
    [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
    [JsonPropertyName("nextSyncToken")] public string? NextSyncToken { get; set; }
}
