namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// A single selectable option in a <see cref="PillSegmentGroup{TValue}"/>:
/// the underlying value paired with the label shown on the pill.
/// </summary>
/// <typeparam name="TValue">The option value type (e.g. an enum).</typeparam>
public sealed record PillSegmentOption<TValue>(TValue Value, string Label);
