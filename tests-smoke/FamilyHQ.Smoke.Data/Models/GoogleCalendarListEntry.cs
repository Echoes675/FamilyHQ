using System.Text.Json.Serialization;

namespace FamilyHQ.Smoke.Data.Models;

/// <summary>
/// An entry in Google's calendar list for the smoke account.
/// <para>
/// <see cref="SummaryOverride"/> wins over <see cref="Summary"/> when present — that is Google's own
/// precedence for a subscribed calendar the user has renamed, and it is the name FamilyHQ adopts
/// (FHQ-211). <see cref="AccessRole"/> is how a read-only subscription such as a holidays feed is told
/// apart from a calendar the account owns and can be pushed for.
/// </para>
/// </summary>
public sealed record GoogleCalendarListEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("summaryOverride")] string? SummaryOverride,
    [property: JsonPropertyName("accessRole")] string? AccessRole,
    [property: JsonPropertyName("timeZone")] string? TimeZone)
{
    /// <summary>The name this calendar shows under, applying Google's own override precedence.</summary>
    public string? DisplayName => string.IsNullOrWhiteSpace(SummaryOverride) ? Summary : SummaryOverride;
}
