using System.Text.Json.Serialization;

namespace FamilyHQ.Smoke.Data.Models;

/// <summary>One of Google's per-event reminder overrides.</summary>
public sealed record GoogleEventReminderOverride(
    [property: JsonPropertyName("method")] string? Method,
    [property: JsonPropertyName("minutes")] int? Minutes);
