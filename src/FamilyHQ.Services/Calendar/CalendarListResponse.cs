using System.Text.Json.Serialization;

namespace FamilyHQ.Services.Calendar;

internal class CalendarListResponse
{
    [JsonPropertyName("items")]
    public List<CalendarListItem> Items { get; set; } = new();
}
