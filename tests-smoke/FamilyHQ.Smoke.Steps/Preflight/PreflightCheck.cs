namespace FamilyHQ.Smoke.Steps.Preflight;

/// <summary>
/// One environment health check and what it found.
/// <para>
/// <paramref name="Message"/> is the whole value of this type. A check that fails says what is wrong
/// <b>and what to do about it</b> — which calendar to rename, which flag to set, where to sign in again
/// — because the reader of a red smoke run is someone who has just been paged and does not yet know
/// whether FamilyHQ is broken or the environment has drifted.
/// </para>
/// </summary>
/// <param name="Name">The check's name, as a feature file refers to it.</param>
/// <param name="Passed">Whether the environment satisfied it.</param>
/// <param name="Message">What was found, and on failure what to do about it.</param>
public sealed record PreflightCheck(string Name, bool Passed, string Message);
