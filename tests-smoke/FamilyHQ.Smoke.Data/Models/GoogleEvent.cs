using System.Text.Json.Serialization;

namespace FamilyHQ.Smoke.Data.Models;

/// <summary>
/// An event as Google holds it — the oracle for every assertion the smoke suite makes about a write.
/// <para>
/// <c>colorId</c> is bound here although FamilyHQ never reads or writes it, and that is precisely why:
/// a field FamilyHQ has no opinion about is the cleanest available probe for the golden rule. If an edit
/// that changed only the title comes back with the colour gone, FamilyHQ rewrote something nobody asked
/// it to rewrite.
/// </para>
/// </summary>
public sealed record GoogleEvent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("location")] string? Location,
    [property: JsonPropertyName("colorId")] string? ColorId,
    [property: JsonPropertyName("start")] GoogleEventDateTime? Start,
    [property: JsonPropertyName("end")] GoogleEventDateTime? End,
    [property: JsonPropertyName("recurrence")] IReadOnlyList<string>? Recurrence,
    [property: JsonPropertyName("recurringEventId")] string? RecurringEventId,
    [property: JsonPropertyName("reminders")] GoogleEventReminders? Reminders);
