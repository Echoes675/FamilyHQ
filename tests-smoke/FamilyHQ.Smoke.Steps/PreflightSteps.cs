using FluentAssertions;
using Reqnroll;
using Xunit.Abstractions;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// Reports what preflight found. One scenario per check, so a red run names the specific thing that is
/// wrong with preprod rather than "the environment is unhealthy".
/// </summary>
[Binding]
public sealed class PreflightSteps(ScenarioContext scenarioContext, ITestOutputHelper output)
{
    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    /// <summary>
    /// The checks themselves ran once, before the first scenario of the run — they make real calls against
    /// a live account, and running them per scenario would mean seven refreshes of the same Google grant.
    /// This step asserts they ran at all, and prints the whole report so a red run carries every answer and
    /// not just the one its own scenario is about.
    /// </summary>
    [Given(@"preprod's environment health has been checked")]
    public void GivenPreprodsEnvironmentHealthHasBeenChecked()
    {
        var report = State.Environment.Report;

        report.Checks.Should().NotBeEmpty(
            "preflight must have run before any scenario; an empty report means the hook did not execute");

        foreach (var check in report.Checks)
        {
            output.WriteLine($"  preflight [{(check.Passed ? "pass" : "FAIL")}] {check.Name}: {check.Message}");
        }
    }

    [Then(@"the ""([^""]*)"" environment check passes")]
    public void ThenTheEnvironmentCheckPasses(string checkName)
    {
        var check = State.Environment.Report.Require(checkName);

        check.Passed.Should().BeTrue(
            "preprod must satisfy this check before any smoke scenario can mean anything. {0}",
            check.Message);
    }
}
