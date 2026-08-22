namespace FamilyHQ.Core.Interfaces;

/// <summary>
/// Turns a value that must never reach a log sink verbatim (see the "Redaction (non-negotiable)"
/// section of <c>.agent/skills/logging/SKILL.md</c>) into a stable, non-reversible token.
/// <para>
/// FHQ-166. The reason such values were logged in the first place is investigation — a redaction
/// that simply deletes the value trades a disclosure problem for a diagnosis problem. The token is
/// therefore <b>stable</b>: the same input always redacts to the same token within a deployment, so
/// one calendar can still be followed across log lines in Seq. It is not reversible back to the
/// value it stands for.
/// </para>
/// <para>
/// Prefer an identifier FamilyHQ owns (a <c>CalendarInfo.Id</c>, a user id, a job id) wherever the
/// caller already has one — it correlates just as well, costs nothing, and carries no PII to begin
/// with. This seam is for the places that genuinely hold only the third-party value, such as
/// <c>GoogleCalendarClient</c>, which never sees FamilyHQ's own calendar row.
/// </para>
/// </summary>
public interface IPiiRedactor
{
    /// <summary>
    /// Returns a stable, non-reversible token standing for <paramref name="value"/>, or a fixed
    /// "no value" token when it is null or empty.
    /// </summary>
    string Redact(string? value);
}
