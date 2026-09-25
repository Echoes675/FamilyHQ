using System.Text.Json.Serialization;

namespace FamilyHQ.Smoke.Data.Models;

/// <summary>
/// Google's event boundary: <c>dateTime</c> plus <c>timeZone</c> for a timed event, <c>date</c> for an
/// all-day one. Both shapes are kept because the difference <i>is</i> the semantics — an all-day end
/// date is exclusive and carries no zone — and flattening them would discard the thing worth asserting.
/// </summary>
public sealed record GoogleEventDateTime(
    [property: JsonPropertyName("dateTime")] DateTimeOffset? DateTime,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("timeZone")] string? TimeZone);
