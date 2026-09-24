namespace FamilyHQ.Smoke.Steps.Preflight;

/// <summary>
/// Everything preflight found out about preprod, gathered once per run.
/// <para>
/// A report rather than a series of exceptions, for two reasons. First, one bad setting should not hide
/// the other six answers — the person fixing the environment wants the whole list, not the first line of
/// it. Second, <see cref="IsHealthy"/> is what gates the core scenarios: FHQ-141 principle 1 says a bad
/// environment followed by a green run is false confidence, so every scenario refuses to run against an
/// environment that failed any check, and says which.
/// </para>
/// </summary>
public sealed class PreflightReport
{
    private readonly List<PreflightCheck> _checks = [];

    public IReadOnlyList<PreflightCheck> Checks => _checks;

    public bool IsHealthy => _checks.Count > 0 && _checks.All(check => check.Passed);

    /// <summary>Every failure, one per line, for a message that has to explain itself on its own.</summary>
    public string FailureSummary =>
        string.Join(
            Environment.NewLine,
            _checks.Where(check => !check.Passed).Select(check => $"  - {check.Name}: {check.Message}"));

    public void Add(string name, bool passed, string message) =>
        _checks.Add(new PreflightCheck(name, passed, message));

    public void Pass(string name, string message) => Add(name, passed: true, message);

    public void Fail(string name, string message) => Add(name, passed: false, message);

    /// <summary>
    /// The named check. Throws when preflight never ran it — a feature file naming a check that does not
    /// exist is a defect in the suite, not an unhealthy environment, and the two must not look alike.
    /// </summary>
    public PreflightCheck Require(string name) =>
        _checks.SingleOrDefault(check => string.Equals(check.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"Preflight ran no check called '{name}'. Known checks: "
            + $"{string.Join(", ", _checks.Select(check => $"'{check.Name}'"))}.");
}
