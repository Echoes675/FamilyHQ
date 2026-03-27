namespace FamilyHQ.Services.Calendar.GoogleApi;

using System.Text.Json.Serialization;

internal record GoogleApiOrganizer(
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("self")]  bool    Self);