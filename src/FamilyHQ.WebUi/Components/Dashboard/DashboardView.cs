// src/FamilyHQ.WebUi/Components/Dashboard/DashboardView.cs
namespace FamilyHQ.WebUi.Components.Dashboard;

// Appended, never reordered: the enum is bound in existing component parameters and persisted idle
// state, and inserting a value would silently renumber every view after it.
public enum DashboardView { Month, MonthAgenda, Day, Reminders }
