namespace FamilyHQ.Services.Calendar.GoogleApi;

using System.Text.Json.Serialization;

internal record GoogleApiEvent(
    [property: JsonPropertyName("id")]          string               Id,
    [property: JsonPropertyName("iCalUID")]     string?              ICalUID,
    [property: JsonPropertyName("status")]      string?              Status,
    [property: JsonPropertyName("summary")]     string?              Summary,
    [property: JsonPropertyName("description")] string?              Description,
    [property: JsonPropertyName("location")]    string?              Location,
    [property: JsonPropertyName("start")]       GoogleApiEventDateTime? Start,
    [property: JsonPropertyName("end")]         GoogleApiEventDateTime? End,
    [property: JsonPropertyName("organizer")]   GoogleApiOrganizer?  Organizer,
    [property: JsonPropertyName("attendees")]   IReadOnlyList<GoogleApiAttendee>? Attendees);