using System.Text.Json.Serialization;

namespace FamilyHQ.Smoke.Data.Models;

/// <summary>
/// A partial update the smoke suite sends to Google, standing in for a phone changing something.
/// <para>
/// A <b>patch</b> and not a whole event resource, deliberately: the phone app changes the field the user
/// touched and leaves the rest alone, and a suite that sent a full resource while seeding would be
/// creating the very damage it exists to detect elsewhere. Unset members are omitted rather than sent as
/// null, because Google reads a present null as "clear this field".
/// </para>
/// </summary>
public sealed record GoogleEventPatch(
    [property: JsonPropertyName("summary")] string? Summary = null,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("start")] GoogleEventDateTime? Start = null,
    [property: JsonPropertyName("end")] GoogleEventDateTime? End = null,
    [property: JsonPropertyName("recurrence")] IReadOnlyList<string>? Recurrence = null);
