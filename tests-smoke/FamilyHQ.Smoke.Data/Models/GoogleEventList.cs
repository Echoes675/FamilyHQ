using System.Text.Json.Serialization;

namespace FamilyHQ.Smoke.Data.Models;

/// <summary>A page of Google's <c>events.list</c> or <c>events.instances</c> response.</summary>
public sealed record GoogleEventList(
    [property: JsonPropertyName("items")] IReadOnlyList<GoogleEvent>? Items,
    [property: JsonPropertyName("nextPageToken")] string? NextPageToken);
