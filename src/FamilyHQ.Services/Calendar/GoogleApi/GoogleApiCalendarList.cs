namespace FamilyHQ.Services.Calendar.GoogleApi;

using System.Text.Json.Serialization;

internal record GoogleApiCalendarList(
    [property: JsonPropertyName("items")]         IReadOnlyList<GoogleApiCalendarListEntry> Items,
    [property: JsonPropertyName("nextPageToken")] string? NextPageToken,
    [property: JsonPropertyName("nextSyncToken")] string? NextSyncToken);