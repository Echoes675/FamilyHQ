namespace FamilyHQ.Simulator.Models;

public class EventModel
{
    public string Id { get; set; } = "";
    public string CalendarId { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    /// <summary>
    /// Additional calendar IDs that this event appears in.
    /// Seeds EventAttendee rows so the event surfaces on each attendee calendar's feed.
    /// </summary>
    public List<string> AttendeeCalendarIds { get; set; } = new();
}