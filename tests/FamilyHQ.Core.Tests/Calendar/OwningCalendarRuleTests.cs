using FamilyHQ.Core.Calendar;
using FluentAssertions;

namespace FamilyHQ.Core.Tests.Calendar;

/// <summary>
/// The single statement of "which calendar does this event land on", which the event modal's
/// Reminders tab and the server's create path both answer. These are the ordinary unit tests of the
/// function itself; the test that it still matches what
/// <c>CalendarEventService.CreateAsync</c> actually does lives in
/// <c>FamilyHQ.Services.Tests</c>, because only that project can see the server.
/// </summary>
public class OwningCalendarRuleTests
{
    private static readonly Guid MemberAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MemberBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SharedId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid SecondSharedId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid UnknownId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    [Fact]
    public void OwningCalendarFor_WithOneSelectedMember_IsThatMembersOwnCalendar()
    {
        var result = OwningCalendarRule.OwningCalendarFor([MemberAId], Candidates());

        result.Should().Be(MemberAId);
    }

    [Fact]
    public void OwningCalendarFor_WithSeveralSelectedMembers_IsTheSharedCalendar()
    {
        var result = OwningCalendarRule.OwningCalendarFor([MemberAId, MemberBId], Candidates());

        result.Should().Be(SharedId);
    }

    [Fact]
    public void OwningCalendarFor_WithTheSharedCalendarAsTheOnlySelection_IsTheSharedCalendar()
    {
        // The single-selection branch does not care whether the chosen calendar is the shared one:
        // one member chosen means that member's calendar, and the shared calendar is reachable as a
        // member chip in its own right.
        var result = OwningCalendarRule.OwningCalendarFor([SharedId], Candidates());

        result.Should().Be(SharedId);
    }

    [Fact]
    public void OwningCalendarFor_WithNothingSelected_IsNull()
    {
        // No answer is available rather than a wrong one. This is not a disagreement with the server:
        // CreateEventRequestValidator rejects a memberless create ("At least one calendar is
        // required") before the create path runs, so there is no server answer to differ from.
        var result = OwningCalendarRule.OwningCalendarFor([], Candidates());

        result.Should().BeNull();
    }

    [Fact]
    public void OwningCalendarFor_WithASelectionNamingACalendarNotInTheCandidateList_IsNull()
    {
        // Again "no answer available", not a competing one: the server throws
        // UnknownCalendarException for an id missing from its own calendar list, so a caller that
        // reaches this is already holding a selection the server will refuse.
        var result = OwningCalendarRule.OwningCalendarFor([UnknownId], Candidates());

        result.Should().BeNull();
    }

    [Fact]
    public void OwningCalendarFor_WithSeveralSelectedMembersAndNoSharedCandidate_IsNull()
    {
        // The server throws InvalidOperationException here ("No shared calendar configured for
        // multi-member events"). A null is the closest a pure function gets to that: it reports that
        // the rule has no calendar to name, and leaves the caller to decide how loud that is.
        var result = OwningCalendarRule.OwningCalendarFor(
            [MemberAId, MemberBId],
            [new OwningCalendarCandidate(MemberAId, IsShared: false),
             new OwningCalendarCandidate(MemberBId, IsShared: false)]);

        result.Should().BeNull();
    }

    [Fact]
    public void OwningCalendarFor_WithTwoSharedCandidates_DoesNotDependOnTheOrderTheyArriveIn()
    {
        // Two shared calendars cannot exist today — CalendarsController clears the previous shared
        // calendar whenever one is set. This pins the tie-break anyway, because the two callers of
        // this rule hold their candidates in different orders (the dashboard's display order; an
        // unordered repository query), so a rule that answered "the first shared one I was handed"
        // would start returning different calendars on the two sides the moment that single-shared
        // enforcement slipped.
        var shared = new OwningCalendarCandidate(SharedId, IsShared: true);
        var secondShared = new OwningCalendarCandidate(SecondSharedId, IsShared: true);

        var oneOrder = OwningCalendarRule.OwningCalendarFor([MemberAId, MemberBId], [shared, secondShared]);
        var theOther = OwningCalendarRule.OwningCalendarFor([MemberAId, MemberBId], [secondShared, shared]);

        oneOrder.Should().Be(theOther);
    }

    private static IReadOnlyCollection<OwningCalendarCandidate> Candidates() =>
    [
        new OwningCalendarCandidate(MemberAId, IsShared: false),
        new OwningCalendarCandidate(MemberBId, IsShared: false),
        new OwningCalendarCandidate(SharedId, IsShared: true)
    ];
}
