using System.Text.Json.Serialization;

namespace FamilyHQ.Services.Calendar;

internal class EventDateTime
{
    [JsonPropertyName("dateTime")] public DateTimeOffset? DateTime { get; set; }
    [JsonPropertyName("date")] public DateTimeOffset? Date { get; set; }
}
