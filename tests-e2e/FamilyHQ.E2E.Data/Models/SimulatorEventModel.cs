using System;
using System.Collections.Generic;

namespace FamilyHQ.E2E.Data.Models;

public class SimulatorEventModel
{
    public string Id { get; set; } = "";
    public string CalendarId { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    /// <summary>
    /// Additional calendar IDs that this event appears in (attendee calendars).
    /// When non-empty, the Simulator seeds these as EventAttendee rows so that
    /// the event shows up on each attendee calendar's feed.
    /// </summary>
    public List<string> AttendeeCalendarIds { get; set; } = new();
}
