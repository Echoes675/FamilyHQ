using System;

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
    /// Optional event description. May contain a [members: Name1, Name2] tag
    /// to designate which member calendars this event appears in when it lives
    /// on the shared calendar.
    /// </summary>
    public string? Description { get; set; }
}
