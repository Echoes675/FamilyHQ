using System.Text.Json.Serialization;

namespace FamilyHQ.Smoke.Data.Models;

/// <summary>A page of Google's <c>calendarList.list</c> response.</summary>
public sealed record GoogleCalendarList(
    [property: JsonPropertyName("items")] IReadOnlyList<GoogleCalendarListEntry>? Items,
    [property: JsonPropertyName("nextPageToken")] string? NextPageToken);
