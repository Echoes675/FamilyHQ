namespace FamilyHQ.WebUi.ViewModels;

public static class CalendarEventViewModelExtensions
{
    public static DateTime StartLocal(this CalendarEventViewModel evt) => evt.Start.LocalDateTime;
    public static DateTime EndLocal(this CalendarEventViewModel evt) => evt.End.LocalDateTime;
}
