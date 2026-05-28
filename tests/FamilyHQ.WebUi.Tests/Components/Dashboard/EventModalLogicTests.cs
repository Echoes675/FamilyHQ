using FamilyHQ.WebUi.Components.Dashboard;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Components.Dashboard;

// FHQ-32: the create-event modal must not silently default the calendar selection.
// The initial selection depends ONLY on an explicitly-passed calendarId — never on
// the order or composition of the user's calendar list. Taking no Calendars argument
// is deliberate: it proves by construction that list order / IsShared distribution
// cannot influence the default (the old bug pre-selected Calendars.FirstOrDefault()).
public class EventModalLogicTests
{
    [Fact]
    public void InitialCreateSelection_WithExplicitCalendarId_SelectsOnlyThatId()
    {
        var calendarId = Guid.NewGuid();

        var result = EventModalLogic.InitialCreateSelection(calendarId);

        result.Should().ContainSingle().Which.Should().Be(calendarId);
    }

    [Fact]
    public void InitialCreateSelection_WithNoCalendarId_IsEmpty()
    {
        var result = EventModalLogic.InitialCreateSelection(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void InitialCreateSelection_WithEmptyGuid_IsEmpty()
    {
        // Agenda view guards against Guid.Empty before calling, but treat it defensively:
        // an empty id is not a real calendar and must not become a selection.
        var result = EventModalLogic.InitialCreateSelection(Guid.Empty);

        result.Should().BeEmpty();
    }
}
