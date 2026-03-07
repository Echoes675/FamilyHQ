using System.Text.Json.Serialization;

namespace FamilyHQ.Services.Calendar;

internal class CalendarListItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("summary")] public string Summary { get; set; } = string.Empty;
    [JsonPropertyName("summaryOverride")] public string? SummaryOverride { get; set; }
    [JsonPropertyName("backgroundColor")] public string? BackgroundColor { get; set; }
}
