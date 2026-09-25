namespace FamilyHQ.Smoke.Steps.Hooks;

/// <summary>
/// The tags the hooks act on. Constants so a typo in a feature file's tag is a missing behaviour that
/// shows up immediately, rather than a hook that silently never fires.
/// </summary>
public static class SmokeTags
{
    /// <summary>
    /// The preflight feature. These scenarios <b>report</b> the environment's health, so they are the one
    /// set that is not gated on it — gating them would replace seven specific diagnoses with seven copies
    /// of the same one.
    /// </summary>
    public const string Preflight = "preflight";

    /// <summary>
    /// The scenario drives the kiosk in a browser. Only tagged scenarios get one: launching Chromium for
    /// the preflight checks, which are pure HTTP, would add a browser start to each of them for nothing.
    /// </summary>
    public const string Kiosk = "kiosk";
}
