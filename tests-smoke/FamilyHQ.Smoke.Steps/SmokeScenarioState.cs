using FamilyHQ.Smoke.Common.Correlation;
using FamilyHQ.Smoke.Common.Hooks;
using FamilyHQ.Smoke.Common.Pages;
using FamilyHQ.Smoke.Data.Api;
using FamilyHQ.Smoke.Data.Models;
using FamilyHQ.Smoke.Steps.Preflight;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// One scenario's world: its correlation identity, its clients, its kiosk, and the handful of facts its
/// steps hand to one another.
/// <para>
/// Held in the Reqnroll scenario context and never static, so nothing a scenario learns can leak into the
/// next one. The run-scoped things — the credentials, the calendar directory — live on
/// <see cref="SmokeRunEnvironment"/> instead, because they describe the environment rather than the
/// scenario.
/// </para>
/// </summary>
public sealed class SmokeScenarioState
{
    public required SmokeRunEnvironment Environment { get; init; }

    /// <summary>This scenario's correlation identity: the id on every request, in every description, in every title.</summary>
    public required SmokeCorrelation Correlation { get; init; }

    /// <summary>
    /// preprod's API, read as this scenario's signed-in kiosk. Null when the run never obtained a session
    /// token: the preflight feature still has to run in that case, and it does not need one.
    /// </summary>
    public PreprodApiClient? Api { get; init; }

    public SmokePlaywrightDriver? Driver { get; set; }

    public SmokeDashboardPage? Dashboard { get; set; }

    /// <summary>What the kiosk was asked to create, when this scenario created something through the UI.</summary>
    public SmokeEventDraft? Draft { get; set; }

    /// <summary>The calendar names this scenario is exercising, in the order the scenario named them.</summary>
    public IReadOnlyList<string> MemberNames { get; set; } = [];

    /// <summary>The calendar an event was seeded into directly in Google, when the scenario did that.</summary>
    public string? SeededCalendarName { get; set; }

    /// <summary>The event as Google returned it when this scenario seeded one there, standing in for a phone.</summary>
    public GoogleEvent? SeededGoogleEvent { get; set; }

    /// <summary>The event title currently expected on the kiosk. Changes when a scenario renames one.</summary>
    public string? ExpectedTitle { get; set; }

    /// <summary>The date, in the family's zone, the scenario's event sits on.</summary>
    public DateOnly EventDate { get; set; }

    private SmokeDayBlock? _reservedDays;

    /// <summary>
    /// The run of days this scenario has to itself, once it has asked for one. Null for a scenario that
    /// creates no events, and for the daylight-saving scenarios, whose dates the clocks decide.
    /// </summary>
    public SmokeDayBlock? ReservedDays => _reservedDays;

    /// <summary>
    /// Reserves a run of <paramref name="days"/> days for this scenario and returns it. Events put inside it
    /// cannot end up under another scenario's tile on the kiosk's day view, which is what happens when two
    /// scenarios share a day and the click for the tile one of them wants never lands.
    /// <para>
    /// The span is the scenario's to state, through one of the <see cref="SmokeScenarioDays"/> factories,
    /// because only the scenario knows the rule it is about to write. Asking twice for the same span is how a
    /// scenario with two steps on one day works; asking twice for different spans is a mistake, and says so.
    /// </para>
    /// </summary>
    public SmokeDayBlock ReserveDays(int days)
    {
        if (_reservedDays is { } already)
        {
            if (already.Days != days)
            {
                throw new InvalidOperationException(
                    $"This scenario has already reserved {already.Days} day(s) at {already} and is now asking "
                    + $"for {days}. A scenario gets one block: widening it after the fact would not move the "
                    + "events already created, and narrowing it would hand days it is still using to the next "
                    + "scenario. Reserve the span the whole scenario needs, in its first step that needs one.");
            }

            return already;
        }

        _reservedDays = SmokeScenarioDays.Reserve(days, Environment.Configuration.SyncHorizonDays);
        return _reservedDays;
    }

    /// <summary>The first day of a freshly reserved block — what a step that puts one event down wants.</summary>
    public DateOnly ReserveFirstDay(int days) => ReserveDays(days).FirstDay;

    /// <summary>
    /// The recurrence rule this scenario wrote into Google, when it seeded a series there. Kept so a later
    /// step can truncate <i>that</i> rule rather than compose a fresh one that merely resembles it.
    /// </summary>
    public string? SeededRecurrenceRule { get; set; }

    /// <summary>
    /// The series master(s) Google holds for this scenario. One for an ordinary series; two once a
    /// "this and following" change has split one, in which case the occurrence set is the union.
    /// </summary>
    public IReadOnlyList<SmokeSeriesMaster> SeriesMasters { get; set; } = [];

    /// <summary>
    /// How far forward this scenario's Google window reaches, in days. A yearly series needs more than the
    /// default, and nothing else does.
    /// </summary>
    public int WindowDaysAfter { get; set; } = SmokeLookup.DefaultWindowDaysAfter;

    /// <summary>
    /// preprod's API. Any scenario past the health gate has one; reaching this without a session token means
    /// the gate was bypassed, and the message says so rather than reading as a null dereference.
    /// </summary>
    public PreprodApiClient RequireApi() =>
        Api ?? throw new InvalidOperationException(
            "This scenario needs to read preprod's API, but the run never obtained a session token. See the "
            + "'FamilyHQ session token' preflight check.");

    public SmokeDashboardPage RequireDashboard() =>
        Dashboard ?? throw new InvalidOperationException(
            "This scenario used the kiosk without being tagged @kiosk, so no browser was started. Add the "
            + "tag to the scenario in its feature file.");

    public SmokeEventDraft RequireDraft() =>
        Draft ?? throw new InvalidOperationException(
            "This step expected the scenario to have created an event on the kiosk first.");

    public GoogleEvent RequireSeededGoogleEvent() =>
        SeededGoogleEvent ?? throw new InvalidOperationException(
            "This step expected the scenario to have seeded an event directly in Google first.");
}
