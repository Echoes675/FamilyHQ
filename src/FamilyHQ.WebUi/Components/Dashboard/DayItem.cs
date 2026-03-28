// src/FamilyHQ.WebUi/Components/Dashboard/DayItem.cs
using FamilyHQ.WebUi.ViewModels;

namespace FamilyHQ.WebUi.Components.Dashboard;

public class DayItem
{
    public DateTime Date { get; set; }
    public List<CalendarEventViewModel> Events { get; set; } = new();
}
