using System.Text.Json.Serialization;

public class GoogleDateTime
{
    [JsonPropertyName("dateTime")]
    public DateTime? DateTime { get; set; }
    [JsonPropertyName("date")]
    public string? Date { get; set; }
}