namespace FamilyHQ.Services.Calendar.GoogleApi;

using System.Text.Json.Serialization;

internal record GoogleApiCalendarListEntry(
    [property: JsonPropertyName("id")]              string  Id,
    [property: JsonPropertyName("summary")]         string? Summary,
    [property: JsonPropertyName("summaryOverride")] string? SummaryOverride,
    [property: JsonPropertyName("backgroundColor")] string? BackgroundColor,
    [property: JsonPropertyName("foregroundColor")] string? ForegroundColor,
    [property: JsonPropertyName("accessRole")]      string? AccessRole);