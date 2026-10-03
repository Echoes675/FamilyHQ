using System.Text.Json;
using System.Text.RegularExpressions;
using FamilyHQ.Core.Tests.Guards;
using FluentAssertions;

namespace FamilyHQ.Core.Tests;

/// <summary>
/// Guards the containment of the kiosk's dev clock bridge. <c>window.familyHqKiosk</c> lets a caller
/// move the displayed date and force an idle evaluation; E2E needs it, and a wall-mounted kiosk in a
/// family's kitchen must never have it. It exists only because <c>Index.OnAfterRenderAsync</c> calls
/// <c>attachDevBridge</c> inside <c>if (Clock.OverrideEnabled)</c>, and that flag reaches
/// <c>KioskTimeProvider</c> from the <c>FeatureClockOverride</c> configuration key.
/// <para>
/// Nothing tested that. The concern raised was "if someone ever set that flag in a prod env file,
/// nothing would object" — and the containment rests on two things that a reasonable edit could
/// remove without any test going red: the shipped default being <c>false</c>, and the single call
/// site staying inside its guard. Modelled on <see cref="OutboundZoneGuardTests"/> and
/// <see cref="PiiInLogsGuardTests"/>, which exist for the same class of invisible regression.
/// </para>
///
/// <para><b>What this does NOT cover, and cannot.</b> It cannot stop a deployed environment turning
/// the flag on. Every <c>docker-compose.*.yml</c> passes <c>env_file: .env</c>, which forwards the
/// whole file into the container — so the per-service <c>environment:</c> block is not the gate it
/// looks like, and <c>FEATURE_CLOCK_OVERRIDE_ENABLED=true</c> in a production env file would reach
/// <c>docker/webui/docker-entrypoint.sh</c> and flip the shipped <c>false</c> to <c>true</c>. The
/// deployed env files are not the ones in this repository either. That is a deployment control, not
/// a test one. What a green run here means is narrower and worth stating exactly: the repository
/// ships the bridge off, reads the flag with no fallback that could turn it on, and attaches the
/// bridge from exactly one guarded place.</para>
///
/// <para><b>The behavioural half is already covered elsewhere</b> and is deliberately not repeated
/// here: <c>KioskTimeProviderTests.WhenOverrideDisabled_AdvanceDays_IsIgnored</c> pins that a clock
/// built with the flag off cannot be moved at all. So between that test and these four, the flag
/// being off means the bridge is never published AND the clock would refuse to move if it were.</para>
///
/// <para>Like its siblings this is a lexical tripwire, not a proof: a bridge attached through a
/// helper in another file, or reached by reflection, is invisible to it.</para>
/// </summary>
public class ClockOverrideBridgeGuardTests
{
    private const string SourceFolderName = "src";

    /// <summary>The JS module function that publishes <c>window.familyHqKiosk</c>.</summary>
    private const string BridgeAttachFunction = "attachDevBridge";

    /// <summary>
    /// The configuration key the flag is read from, and the file every environment's bundle starts
    /// from before <c>docker-entrypoint.sh</c> has a chance to flip anything.
    /// </summary>
    private const string FlagKey = "FeatureClockOverride";
    private static readonly string[] ShippedAppSettingsPath =
        ["src", "FamilyHQ.WebUi", "wwwroot", "appsettings.json"];

    /// <summary>
    /// Matches an <c>if</c> whose entire condition is some <c>…OverrideEnabled</c> member. Deliberately
    /// not pinned to <c>Clock.OverrideEnabled</c> by name — renaming the injected clock is fine; what
    /// must not change is that the attach is reached only through a flag check. Equally deliberately
    /// it does not allow a compound condition: <c>if (OverrideEnabled || somethingElse)</c> would be
    /// a second way in, and this is the one assertion standing between that edit and production.
    /// </summary>
    private static readonly Regex OverrideGuard =
        new(@"\bif\s*\(\s*(?:[A-Za-z_][A-Za-z0-9_]*\s*\.\s*)*[A-Za-z0-9_]*OverrideEnabled\s*\)",
            RegexOptions.Compiled);

    [Fact]
    public void TheDevClockBridge_IsAttachedFromExactlyOnePlace()
    {
        var sites = BridgeAttachSites();

        sites.Should().HaveCount(1,
            $"'{BridgeAttachFunction}' publishes window.familyHqKiosk, so every call site is a way " +
            "the kiosk's clock can be driven from the page. One site can be read and guarded; a " +
            "second has to be found first. Sites: " + string.Join(", ", sites.Select(s => s.Describe())));
    }

    [Fact]
    public void TheDevClockBridge_IsAttachedOnlyInsideTheOverrideEnabledGuard()
    {
        var site = BridgeAttachSites().Should().ContainSingle().Which;
        var code = SourceScan.MaskCommentsAndLiterals(site.Source);

        var guards = OverrideGuard.Matches(code);
        guards.Should().ContainSingle(
            $"{site.RelativePath} should check the clock-override flag exactly once — the one check " +
            $"the {BridgeAttachFunction} call sits behind. More than one, or none, means the call's " +
            "reachability can no longer be read off a single condition.");

        var guard = guards[0];
        var blockOpen = code.IndexOf('{', guard.Index + guard.Length);
        blockOpen.Should().BeGreaterThan(-1,
            $"the override-enabled check in {site.RelativePath} should open a block. A single-statement " +
            "if with no braces would put the attach one careless added line away from being " +
            "unconditional.");

        var blockClose = SourceScan.MatchingBrace(code, blockOpen);

        site.Index.Should().BeInRange(blockOpen, blockClose,
            $"the {BridgeAttachFunction} call at {site.Describe()} must sit inside the " +
            $"override-enabled block (lines {SourceScan.LineNumberAt(site.Source, blockOpen)}–" +
            $"{SourceScan.LineNumberAt(site.Source, blockClose)}). Outside it, every kiosk gets the " +
            "bridge — including the one on the wall.");
    }

    [Fact]
    public void TheClockOverrideFlag_ShipsOffInTheDefaultAppSettings()
    {
        var path = Path.Combine([SourceScan.FindRepositoryRoot(), .. ShippedAppSettingsPath]);
        File.Exists(path).Should().BeTrue(
            $"the WebUi's shipped appsettings.json should be at {string.Join("/", ShippedAppSettingsPath)}; " +
            "it is the file every environment's bundle starts from, so this guard has nothing to " +
            "stand on if it has moved.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        document.RootElement.TryGetProperty(FlagKey, out var flag).Should().BeTrue(
            $"'{FlagKey}' should be declared in the shipped appsettings.json. Leaving it out would " +
            "still read as false today, but docker-entrypoint.sh flips the flag by substituting the " +
            "literal \"" + FlagKey + "\": false in this file — with the key absent, the substitution " +
            "silently matches nothing and dev and staging lose the bridge the day-rollover scenarios " +
            "need, with no error to say why.");

        flag.ValueKind.Should().Be(JsonValueKind.False,
            $"'{FlagKey}' must ship OFF. This is the value a production kiosk runs with unless its " +
            "deployment deliberately overrides it, so a true here would hand every environment the " +
            "dev clock bridge at once.");
    }

    [Fact]
    public void TheClockOverrideFlag_IsReadWithNoFallbackThatCouldTurnItOn()
    {
        var root = Path.Combine(SourceScan.FindRepositoryRoot(), SourceFolderName);
        var reads = new List<string>();

        foreach (var file in SourceScan.EnumerateSources(root, ".cs", ".razor"))
        {
            var code = SourceScan.MaskCommentsAndLiterals(File.ReadAllText(file));

            // The key is a string literal, which masking has blanked — so the read is found by the
            // GetValue<bool>( call and its argument list is then checked against the raw source at
            // the same offsets. Positions survive masking, which is what makes that pairing safe.
            var raw = File.ReadAllText(file);
            foreach (var match in Regex.Matches(code, @"GetValue\s*<\s*bool\s*>\s*\(").Cast<Match>())
            {
                var open = raw.IndexOf('(', match.Index);
                var arguments = raw[open..(SourceScan.MatchingParenthesis(raw, open) + 1)];
                if (!arguments.Contains(FlagKey, StringComparison.Ordinal)) continue;

                reads.Add($"{Path.GetRelativePath(root, file).Replace('\\', '/')}" +
                          $":{SourceScan.LineNumberAt(raw, match.Index)} → {arguments.Trim()}");
            }
        }

        reads.Should().ContainSingle(
            $"'{FlagKey}' should be read in exactly one place, so what the flag means cannot differ " +
            "between two readers. Reads: " + string.Join(", ", reads));

        // A second argument to GetValue<bool> is its default-when-absent. The whole safety of an
        // unset key rests on that default being false, and the only way to be sure is for there to
        // be no default argument at all.
        reads[0].Should().NotContain(",",
            $"'{FlagKey}' must be read with no fallback value. A default argument is what would let " +
            "an absent key mean ON, which is the opposite of the behaviour every environment that " +
            "does not set the key is relying on.");
    }

    /// <summary>
    /// Every place in <c>src/</c> that calls the bridge-attaching JS function. Matched on the
    /// function NAME inside a string literal, because that is how a Blazor component names a JS
    /// export — so unlike this project's other guards the search runs on the raw source, and the
    /// hits are filtered to ones that look like an interop invocation rather than prose.
    /// </summary>
    private static List<BridgeAttachSite> BridgeAttachSites()
    {
        var root = Path.Combine(SourceScan.FindRepositoryRoot(), SourceFolderName);
        var sites = new List<BridgeAttachSite>();

        foreach (var file in SourceScan.EnumerateSources(root, ".cs", ".razor"))
        {
            var source = File.ReadAllText(file);
            foreach (var match in Regex.Matches(
                         source, $@"Invoke\w*Async\s*(?:<[^>()]*>\s*)?\(\s*""{BridgeAttachFunction}""").Cast<Match>())
            {
                sites.Add(new BridgeAttachSite(
                    Path.GetRelativePath(root, file).Replace('\\', '/'), source, match.Index));
            }
        }

        return sites;
    }

    private sealed record BridgeAttachSite(string RelativePath, string Source, int Index)
    {
        public string Describe() => $"{RelativePath}:{SourceScan.LineNumberAt(Source, Index)}";
    }
}
