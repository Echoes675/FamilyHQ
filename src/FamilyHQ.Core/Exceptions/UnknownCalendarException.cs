namespace FamilyHQ.Core.Exceptions;

/// <summary>
/// A supplied CalendarInfoId is not one of the user's known calendars. Client-fixable input error;
/// maps to HTTP 400.
/// </summary>
public sealed class UnknownCalendarException : DomainValidationException
{
    public Guid CalendarInfoId { get; }

    public UnknownCalendarException(Guid calendarInfoId)
        : base($"CalendarInfoId {calendarInfoId} is not known to the user.")
    {
        CalendarInfoId = calendarInfoId;
    }
}
