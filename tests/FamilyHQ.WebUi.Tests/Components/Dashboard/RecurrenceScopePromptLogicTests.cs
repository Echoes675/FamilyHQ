using FamilyHQ.Core.DTOs;
using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// FHQ-18.8 / §10.1: member changes are only valid at AllInSeries. The prompt must not let
// the user confirm a member change at ThisOnly / ThisAndFollowing (the service also rejects
// it from FHQ-18.4, but the UI must block it first). This pure logic gates the OK button so
// it can be unit-tested without rendering the Blazor component (the project has no bUnit).
public class RecurrenceScopePromptLogicTests
{
    [Theory]
    [InlineData(RecurrenceScope.ThisOnly)]
    [InlineData(RecurrenceScope.ThisAndFollowing)]
    [InlineData(RecurrenceScope.AllInSeries)]
    public void IsConfirmAllowed_WithoutPendingMemberChange_AllowsEveryScope(RecurrenceScope scope)
    {
        var result = RecurrenceScopePromptLogic.IsConfirmAllowed(scope, memberChangePending: false);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(RecurrenceScope.ThisOnly)]
    [InlineData(RecurrenceScope.ThisAndFollowing)]
    public void IsConfirmAllowed_WithPendingMemberChangeAtNonAllScope_IsBlocked(RecurrenceScope scope)
    {
        var result = RecurrenceScopePromptLogic.IsConfirmAllowed(scope, memberChangePending: true);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsConfirmAllowed_WithPendingMemberChangeAtAllInSeries_IsAllowed()
    {
        var result = RecurrenceScopePromptLogic.IsConfirmAllowed(RecurrenceScope.AllInSeries, memberChangePending: true);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldShowMemberChangeWarning_OnlyWhenPendingAndScopeNotAll()
    {
        RecurrenceScopePromptLogic.ShouldShowMemberChangeWarning(RecurrenceScope.ThisOnly, memberChangePending: true)
            .Should().BeTrue();
        RecurrenceScopePromptLogic.ShouldShowMemberChangeWarning(RecurrenceScope.ThisAndFollowing, memberChangePending: true)
            .Should().BeTrue();
        RecurrenceScopePromptLogic.ShouldShowMemberChangeWarning(RecurrenceScope.AllInSeries, memberChangePending: true)
            .Should().BeFalse();
        RecurrenceScopePromptLogic.ShouldShowMemberChangeWarning(RecurrenceScope.ThisOnly, memberChangePending: false)
            .Should().BeFalse();
    }

    [Fact]
    public void HeaderText_SwitchesBetweenEditAndDelete()
    {
        RecurrenceScopePromptLogic.HeaderText(isDelete: false).Should().Be("Edit recurring event");
        RecurrenceScopePromptLogic.HeaderText(isDelete: true).Should().Be("Delete recurring event");
    }

    // A reminder change applied to the whole series replaces the reminders on every occurrence,
    // including one whose reminders were set on their own. Google does that when the series master is
    // patched, so the kiosk mirrors it and warns instead of diverging.

    [Fact]
    public void ShouldShowReminderChangeWarning_WithAReminderChangeAtAllInSeries_IsShown()
    {
        RecurrenceScopePromptLogic
            .ShouldShowReminderChangeWarning(RecurrenceScope.AllInSeries, reminderChangePending: true)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(RecurrenceScope.ThisOnly)]
    [InlineData(RecurrenceScope.ThisAndFollowing)]
    public void ShouldShowReminderChangeWarning_WithAReminderChangeAtANarrowerScope_IsNotShown(RecurrenceScope scope)
    {
        // These write to one occurrence or to a fresh series, so no other occurrence's own reminders
        // are touched — warning about them would teach the family to ignore the warning.
        RecurrenceScopePromptLogic.ShouldShowReminderChangeWarning(scope, reminderChangePending: true)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(RecurrenceScope.ThisOnly)]
    [InlineData(RecurrenceScope.ThisAndFollowing)]
    [InlineData(RecurrenceScope.AllInSeries)]
    public void ShouldShowReminderChangeWarning_WithoutAReminderChange_IsNeverShown(RecurrenceScope scope)
    {
        // An edit that did not touch the Reminders tab sends no reminders at all, so there is nothing
        // for the series to lose.
        RecurrenceScopePromptLogic.ShouldShowReminderChangeWarning(scope, reminderChangePending: false)
            .Should().BeFalse();
    }

    [Fact]
    public void IsConfirmAllowed_WithAReminderChange_IsNotBlockedAtAnyScope()
    {
        // The reminder warning informs; it never blocks. Refusing would leave a reminder the family
        // asked for unset, and Google itself permits the change.
        foreach (var scope in new[] { RecurrenceScope.ThisOnly, RecurrenceScope.ThisAndFollowing, RecurrenceScope.AllInSeries })
        {
            RecurrenceScopePromptLogic.IsConfirmAllowed(scope, memberChangePending: false).Should().BeTrue();
        }
    }
}
