using System.Text.Json.Serialization;

namespace FamilyHQ.Services.Calendar.GoogleApi;

internal record GoogleApiEventDateTime(
    [property: JsonPropertyName("dateTime")] DateTimeOffset? DateTime,
    [property: JsonPropertyName("date")]     string?         Date,
    [property: JsonPropertyName("timeZone")] string?         TimeZone);