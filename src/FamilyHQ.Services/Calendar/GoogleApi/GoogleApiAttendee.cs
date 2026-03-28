namespace FamilyHQ.Services.Calendar.GoogleApi;

using System.Text.Json.Serialization;

internal record GoogleApiAttendee(
    [property: JsonPropertyName("email")]          string  Email,
    [property: JsonPropertyName("responseStatus")] string? ResponseStatus);