using System.Text.Json.Serialization;

namespace FamilyHQ.Smoke.Data.Models;

/// <summary>
/// An event the smoke suite creates <b>directly in Google</b>, standing in for a phone.
/// <para>
/// The Google-to-kiosk and golden-rule scenarios need an event that FamilyHQ did not create, because a
/// change that is correct for an event FamilyHQ wrote can be wrong for one it merely synced — and the
/// synced ones are the majority. So these are written with the oracle credential straight to Google,
/// carrying the fields a phone would set: free-text description, location, colour and reminders.
/// </para>
/// </summary>
public sealed record GoogleEventDraft(
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("start")] GoogleEventDateTime Start,
    [property: JsonPropertyName("end")] GoogleEventDateTime End,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("location")] string? Location = null,
    [property: JsonPropertyName("colorId")] string? ColorId = null,
    [property: JsonPropertyName("recurrence")] IReadOnlyList<string>? Recurrence = null,
    [property: JsonPropertyName("reminders")] GoogleEventReminders? Reminders = null);
