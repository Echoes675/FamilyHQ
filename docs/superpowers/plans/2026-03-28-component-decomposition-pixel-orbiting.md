# Index.razor Component Decomposition + Pixel Orbiting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Decompose the `Index.razor` monolith into focused Blazor components and add a JS-driven pixel orbit burn-in prevention mechanism for a portrait 27" touchscreen running continuously.

**Architecture:** `Index.razor` becomes a thin orchestrator holding shared state and wiring 8 focused components together via parameters and EventCallbacks. `EventModal` is the exception — it owns its own form state and API calls, opened via `@ref`. A standalone JS ES module (`pixel-orbit.js`) runs a `setInterval` loop that applies `transform: translate` to the dashboard container, entirely independent of Blazor's render cycle.

**Tech Stack:** Blazor WASM (.NET 10), C#, JavaScript ES modules, Bootstrap 5

---

### Task 1: Extract shared types and update imports

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/Dashboard/DashboardView.cs`
- Create: `src/FamilyHQ.WebUi/Components/Dashboard/DayItem.cs`
- Modify: `src/FamilyHQ.WebUi/_Imports.razor`

- [ ] **Step 1: Create `DashboardView.cs`**

```csharp
// src/FamilyHQ.WebUi/Components/Dashboard/DashboardView.cs
namespace FamilyHQ.WebUi.Components.Dashboard;

public enum DashboardView { Month, MonthAgenda, Day }
```

- [ ] **Step 2: Create `DayItem.cs`**

```csharp
// src/FamilyHQ.WebUi/Components/Dashboard/DayItem.cs
using FamilyHQ.WebUi.ViewModels;

namespace FamilyHQ.WebUi.Components.Dashboard;

public class DayItem
{
    public DateTime Date { get; set; }
    public List<CalendarEventViewModel> Events { get; set; } = new();
}
```

- [ ] **Step 3: Update `_Imports.razor` to add the Dashboard and ViewModels namespaces**

The current content of `_Imports.razor` is:
```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.AspNetCore.Components.WebAssembly.Http
@using Microsoft.JSInterop
@using FamilyHQ.WebUi
@using FamilyHQ.WebUi.Layout
@using FamilyHQ.WebUi.Components
```

Replace with:
```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.AspNetCore.Components.WebAssembly.Http
@using Microsoft.JSInterop
@using FamilyHQ.WebUi
@using FamilyHQ.WebUi.Layout
@using FamilyHQ.WebUi.Components
@using FamilyHQ.WebUi.Components.Dashboard
@using FamilyHQ.WebUi.ViewModels
```

- [ ] **Step 4: Remove the private `DashboardView` enum and `DayItem` class from the `@code` block in `Index.razor`**

In `Index.razor`, remove lines:
```csharp
private enum DashboardView { Month, MonthAgenda, Day }
```
and:
```csharp
private class DayItem
{
    public DateTime Date { get; set; }
    public List<CalendarEventViewModel> Events { get; set; } = new();
}
```
Also remove the two `@using` lines at the top of `Index.razor` that are now in `_Imports.razor`:
```
@using FamilyHQ.WebUi.ViewModels
```
(Keep `@using FamilyHQ.Core.DTOs` and the service usings as they are not in `_Imports.razor`.)

- [ ] **Step 5: Build to verify no errors**

```
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/DashboardView.cs \
        src/FamilyHQ.WebUi/Components/Dashboard/DayItem.cs \
        src/FamilyHQ.WebUi/_Imports.razor \
        src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "refactor: extract DashboardView enum and DayItem to shared files"
```

---

### Task 2: Create DashboardHeader component

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/Dashboard/DashboardHeader.razor`
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor`

- [ ] **Step 1: Create `DashboardHeader.razor`**

```razor
<div class="d-flex justify-content-between align-items-center mb-4">
    <div class="d-flex align-items-center gap-3">
        <h1>Family HQ Dashboard</h1>
    </div>
    <div class="d-flex align-items-center gap-3">
        <span class="text-muted">Signed in as: <strong>@(Username ?? UserId ?? "Unknown")</strong></span>
        <button class="btn btn-outline-danger btn-sm" @onclick="OnSignOut">Sign Out</button>
    </div>
</div>

@code {
    [Parameter] public string? Username { get; set; }
    [Parameter] public string? UserId { get; set; }
    [Parameter] public EventCallback OnSignOut { get; set; }
}
```

- [ ] **Step 2: Replace the authenticated header block in `Index.razor`**

In `Index.razor`, replace:
```razor
<!-- Authenticated - Show full dashboard with header -->
<div class="d-flex justify-content-between align-items-center mb-4">
    <div class="d-flex align-items-center gap-3">
        <h1>Family HQ Dashboard</h1>
    </div>

    <!-- User Info and Sign Out (Top Right) -->
    <div class="d-flex align-items-center gap-3">
        <span class="text-muted">Signed in as: <strong>@(_username ?? _userId ?? "Unknown")</strong></span>
        <button class="btn btn-outline-danger btn-sm" @onclick="OnSignOutClicked">Sign Out</button>
    </div>
</div>
```
with:
```razor
<DashboardHeader Username="@_username" UserId="@_userId" OnSignOut="OnSignOutClicked" />
```

- [ ] **Step 3: Build to verify no errors**

```
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/DashboardHeader.razor \
        src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "refactor: extract DashboardHeader component"
```

---

### Task 3: Create DashboardTabs component

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/Dashboard/DashboardTabs.razor`
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor`

- [ ] **Step 1: Create `DashboardTabs.razor`**

```razor
<ul class="nav nav-tabs mb-4">
    <li class="nav-item">
        <button data-testid="month-tab"
                class="nav-link @(CurrentView == DashboardView.Month ? "active" : "")"
                @onclick="() => OnViewChanged.InvokeAsync(DashboardView.Month)">Month View</button>
    </li>
    <li class="nav-item">
        <button data-testid="agenda-tab"
                class="nav-link @(CurrentView == DashboardView.MonthAgenda ? "active" : "")"
                @onclick="() => OnViewChanged.InvokeAsync(DashboardView.MonthAgenda)">Agenda</button>
    </li>
    <li class="nav-item">
        <button data-testid="day-tab"
                class="nav-link @(CurrentView == DashboardView.Day ? "active" : "")"
                @onclick="() => OnViewChanged.InvokeAsync(DashboardView.Day)">Day View</button>
    </li>
</ul>

@code {
    [Parameter] public DashboardView CurrentView { get; set; }
    [Parameter] public EventCallback<DashboardView> OnViewChanged { get; set; }
}
```

- [ ] **Step 2: Add a tab-change handler to `Index.razor` @code block**

Add this method to `Index.razor`:
```csharp
private Task OnTabViewChanged(DashboardView view) => SwitchToView(view);
```

- [ ] **Step 3: Replace the `<ul class="nav nav-tabs">` block in `Index.razor`**

Replace:
```razor
<ul class="nav nav-tabs mb-4">
    <li class="nav-item">
        <button data-testid="month-tab" class="nav-link @(_currentView == DashboardView.Month ? "active" : "")" @onclick="() => SwitchToView(DashboardView.Month)">Month View</button>
    </li>
    <li class="nav-item">
        <button data-testid="agenda-tab" class="nav-link @(_currentView == DashboardView.MonthAgenda ? "active" : "")" @onclick="() => SwitchToView(DashboardView.MonthAgenda)">Agenda</button>
    </li>
    <li class="nav-item">
        <button data-testid="day-tab" class="nav-link @(_currentView == DashboardView.Day ? "active" : "")" @onclick="() => SwitchToView(DashboardView.Day)">Day View</button>
    </li>
</ul>
```
with:
```razor
<DashboardTabs CurrentView="_currentView" OnViewChanged="OnTabViewChanged" />
```

- [ ] **Step 4: Build to verify no errors**

```
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/DashboardTabs.razor \
        src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "refactor: extract DashboardTabs component"
```

---

### Task 4: Create QuickJumpModal component

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/Dashboard/QuickJumpModal.razor`
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor`

- [ ] **Step 1: Create `QuickJumpModal.razor`**

```razor
@if (IsVisible)
{
    <div class="modal fade show d-block" tabindex="-1" style="background-color: rgba(0,0,0,0.5);">
        <div class="modal-dialog modal-sm modal-dialog-centered">
            <div class="modal-content">
                <div class="modal-header border-0 pb-0">
                    <h5 class="modal-title w-100 text-center">Jump to Date</h5>
                    <button type="button" class="btn-close" @onclick="OnClose" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <div class="d-flex justify-content-between align-items-center mb-3">
                        <button class="btn btn-sm btn-outline-secondary" @onclick="() => _jumpYear--">&lt;</button>
                        <span class="fs-5 fw-bold">@_jumpYear</span>
                        <button class="btn btn-sm btn-outline-secondary" @onclick="() => _jumpYear++">&gt;</button>
                    </div>
                    <div class="row g-2">
                        @for (int i = 1; i <= 12; i++)
                        {
                            var month = i;
                            <div class="col-4">
                                <button class="btn w-100 @(IsSelectedMonth(month) ? "btn-primary" : "btn-outline-primary")"
                                        @onclick="() => OnJump.InvokeAsync((_jumpYear, month))">
                                    @(new DateTime(2000, month, 1).ToString("MMM"))
                                </button>
                            </div>
                        }
                    </div>
                </div>
                <div class="modal-footer border-0 pt-0 justify-content-center">
                    <button class="btn btn-link text-decoration-none" @onclick="OnJumpToday">Go to Today</button>
                </div>
            </div>
        </div>
    </div>
}

@code {
    [Parameter] public bool IsVisible { get; set; }
    [Parameter] public DateTime CurrentMonth { get; set; }
    [Parameter] public EventCallback<(int Year, int Month)> OnJump { get; set; }
    [Parameter] public EventCallback OnJumpToday { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private int _jumpYear;

    protected override void OnParametersSet()
    {
        if (IsVisible)
            _jumpYear = CurrentMonth.Year;
    }

    private bool IsSelectedMonth(int month) =>
        month == CurrentMonth.Month && _jumpYear == CurrentMonth.Year;
}
```

- [ ] **Step 2: Update jump-related handlers in `Index.razor` @code**

Remove the `_jumpYear` field and the `IsSelectedJumpMonth` method (they now live in the component).

Update `JumpToDate` to accept year and month:
```csharp
private async Task JumpToDate((int Year, int Month) args)
{
    CurrentMonth = new DateTime(args.Year, args.Month, 1);
    _showQuickJumpModal = false;
    await LoadMonthDataAsync();
}
```

`OpenQuickJumpModal`, `CloseQuickJumpModal`, and `JumpToToday` become:
```csharp
private void OpenQuickJumpModal() => _showQuickJumpModal = true;
private void CloseQuickJumpModal() => _showQuickJumpModal = false;

private async Task JumpToToday()
{
    CurrentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    _showQuickJumpModal = false;
    await LoadMonthDataAsync();
}
```

- [ ] **Step 3: Replace the QuickJump modal block in `Index.razor`**

Replace the entire `@if (_showQuickJumpModal) { ... }` block with:
```razor
<QuickJumpModal IsVisible="_showQuickJumpModal"
                CurrentMonth="CurrentMonth"
                OnJump="JumpToDate"
                OnJumpToday="JumpToToday"
                OnClose="CloseQuickJumpModal" />
```

- [ ] **Step 4: Build to verify no errors**

```
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/QuickJumpModal.razor \
        src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "refactor: extract QuickJumpModal component"
```

---

### Task 5: Create DayPickerModal component

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/Dashboard/DayPickerModal.razor`
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor`

- [ ] **Step 1: Create `DayPickerModal.razor`**

```razor
@if (IsVisible)
{
    <div class="modal fade show d-block" tabindex="-1" style="background-color: rgba(0,0,0,0.5);">
        <div class="modal-dialog modal-sm modal-dialog-centered">
            <div class="modal-content">
                <div class="modal-header pb-2">
                    <h5 class="modal-title">Select Date</h5>
                    <button type="button" class="btn-close" @onclick="OnClose" aria-label="Close"></button>
                </div>
                <div class="modal-body text-center">
                    <input type="date" class="form-control mb-3" @bind="_pickedDate" data-testid="day-picker-input" />
                    <button class="btn btn-primary w-100 mb-2" @onclick="HandleGo" data-testid="day-picker-go-btn">Go Context</button>
                    <button class="btn btn-outline-secondary w-100" @onclick="OnJumpToday" data-testid="day-picker-today-btn">Jump to Today</button>
                </div>
            </div>
        </div>
    </div>
}

@code {
    [Parameter] public bool IsVisible { get; set; }
    [Parameter] public DateTime InitialDate { get; set; }
    [Parameter] public EventCallback<DateTime> OnDatePicked { get; set; }
    [Parameter] public EventCallback OnJumpToday { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private DateTime _pickedDate;

    protected override void OnParametersSet()
    {
        if (IsVisible)
            _pickedDate = InitialDate;
    }

    private Task HandleGo() => OnDatePicked.InvokeAsync(_pickedDate);
}
```

- [ ] **Step 2: Update day-picker handlers in `Index.razor` @code**

Remove the `_pickedDate` field. Update the handlers to match the new callback signatures:

```csharp
private void OpenDayPickerModal() => _showDayPickerModal = true;
private void CloseDayPickerModal() => _showDayPickerModal = false;

private async Task JumpToPickedDay(DateTime date)
{
    _showDayPickerModal = false;
    await SwitchToView(DashboardView.Day, date);
}

private async Task JumpToTodayDayView()
{
    _showDayPickerModal = false;
    await SwitchToView(DashboardView.Day, DateTime.Today);
}
```

- [ ] **Step 3: Replace the DayPicker modal block in `Index.razor`**

Replace the entire `@if (_showDayPickerModal) { ... }` block with:
```razor
<DayPickerModal IsVisible="_showDayPickerModal"
                InitialDate="@(_selectedDate == default ? DateTime.Today : _selectedDate)"
                OnDatePicked="JumpToPickedDay"
                OnJumpToday="JumpToTodayDayView"
                OnClose="CloseDayPickerModal" />
```

- [ ] **Step 4: Build to verify no errors**

```
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/DayPickerModal.razor \
        src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "refactor: extract DayPickerModal component"
```

---

### Task 6: Create EventModal component

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/Dashboard/EventModal.razor`
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor`

- [ ] **Step 1: Create `EventModal.razor`**

```razor
@using FamilyHQ.Core.DTOs
@inject ICalendarApiService CalendarApi

@if (_showEventModal)
{
    <div class="modal fade show d-block" tabindex="-1" style="background-color: rgba(0,0,0,0.5);">
        <div class="modal-dialog modal-dialog-centered">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">@(_isEditingEvent ? "Edit Event" : "Create Event")</h5>
                    <button type="button" class="btn-close" @onclick="Close" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <div class="mb-3">
                        <label class="form-label">Calendars</label>
                        <CalendarChipSelector
                            AllCalendars="Calendars"
                            SelectedCalendarIds="_selectedCalendarIds"
                            SelectedCalendarIdsChanged="ids => { _selectedCalendarIds = ids; }"
                            EventId="@(_isEditingEvent ? (Guid?)_eventModel.Id : null)"
                            CalendarApi="CalendarApi" />
                        @if (_selectedCalendarIds.Count == 0)
                        {
                            <div class="text-danger mt-1" style="font-size: 0.82rem;">Please select at least one calendar.</div>
                        }
                    </div>
                    <div class="mb-3">
                        <label class="form-label">Event Title</label>
                        <input type="text" class="form-control" @bind="_eventModel.Title" placeholder="e.g. Doctor Appointment" />
                    </div>
                    <div class="form-check mb-3">
                        <input class="form-check-input" type="checkbox" id="allDayCheck" @bind="_eventModel.IsAllDay">
                        <label class="form-check-label" for="allDayCheck">All Day Event</label>
                    </div>
                    <div class="row mb-3">
                        <div class="col-6">
                            <label class="form-label">Start</label>
                            @if (_eventModel.IsAllDay)
                            {
                                <input type="date" class="form-control" @bind="@_eventModelStartDate" />
                            }
                            else
                            {
                                <input type="datetime-local" class="form-control" @bind="_eventModel.Start" />
                            }
                        </div>
                        <div class="col-6">
                            <label class="form-label">End</label>
                            @if (_eventModel.IsAllDay)
                            {
                                <input type="date" class="form-control" @bind="@_eventModelEndDate" />
                            }
                            else
                            {
                                <input type="datetime-local" class="form-control" @bind="_eventModel.End" />
                            }
                        </div>
                    </div>
                    <div class="mb-3">
                        <label class="form-label">Location (Optional)</label>
                        <input type="text" class="form-control" @bind="_eventModel.Location" placeholder="e.g. 123 Main St" />
                    </div>
                    @if (!string.IsNullOrEmpty(_modalError))
                    {
                        <div class="alert alert-danger p-2 mb-0">@_modalError</div>
                    }
                </div>
                <div class="modal-footer d-flex justify-content-between">
                    <div>
                        @if (_isEditingEvent)
                        {
                            <button type="button" class="btn btn-outline-danger" @onclick="DeleteEvent">Delete</button>
                        }
                    </div>
                    <div>
                        <button type="button" class="btn btn-secondary" @onclick="Close">Cancel</button>
                        <button type="button" class="btn btn-primary" @onclick="SaveEvent" disabled="@_isSaving">
                            @if (_isSaving)
                            {
                                <span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
                                <span class="visually-hidden">Saving...</span>
                            }
                            else
                            {
                                <span>Save</span>
                            }
                        </button>
                    </div>
                </div>
            </div>
        </div>
    </div>
}

@code {
    [Parameter] public IReadOnlyList<CalendarSummaryViewModel> Calendars { get; set; } = Array.Empty<CalendarSummaryViewModel>();
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnDeleted { get; set; }

    private bool _showEventModal;
    private bool _isEditingEvent;
    private EventFormModel _eventModel = new();
    private HashSet<Guid> _selectedCalendarIds = new();
    private string? _modalError;
    private bool _isSaving;

    private DateTime _eventModelStartDate
    {
        get => _eventModel.Start.LocalDateTime.Date;
        set => _eventModel.Start = new DateTimeOffset(value.Date, TimeSpan.Zero);
    }

    private DateTime _eventModelEndDate
    {
        get => _eventModel.End.LocalDateTime.Date;
        set => _eventModel.End = new DateTimeOffset(value.Date.AddDays(1).AddTicks(-1), TimeSpan.Zero);
    }

    public void OpenForCreate(DateTime date, Guid? calendarId = null)
    {
        _isEditingEvent = false;
        var start = date.Hour == 0 && date.Minute == 0 ? date.AddHours(9) : date;
        var end = start.AddHours(1);
        _eventModel = new EventFormModel
        {
            Title = "",
            Start = new DateTimeOffset(start, TimeZoneInfo.Local.GetUtcOffset(start)),
            End = new DateTimeOffset(end, TimeZoneInfo.Local.GetUtcOffset(end)),
            IsAllDay = false
        };
        var targetCalendarId = calendarId ?? Calendars.FirstOrDefault()?.Id ?? Guid.Empty;
        _selectedCalendarIds = targetCalendarId != Guid.Empty ? [targetCalendarId] : [];
        _modalError = null;
        _showEventModal = true;
        StateHasChanged();
    }

    public void OpenForEdit(CalendarEventViewModel evt)
    {
        _isEditingEvent = true;
        _eventModel = new EventFormModel
        {
            Id = evt.Id,
            Title = evt.Title,
            Start = evt.Start,
            End = evt.End,
            IsAllDay = evt.IsAllDay,
            Location = evt.Location,
            AllCalendars = evt.AllCalendars.ToList()
        };
        _selectedCalendarIds = evt.AllCalendars.Select(c => c.Id).ToHashSet();
        _modalError = null;
        _showEventModal = true;
        StateHasChanged();
    }

    private void Close() => _showEventModal = false;

    private async Task SaveEvent()
    {
        _modalError = null;

        if (string.IsNullOrWhiteSpace(_eventModel.Title))
        {
            _modalError = "Title is required.";
            return;
        }
        if (_selectedCalendarIds.Count == 0)
        {
            _modalError = "Please select at least one calendar.";
            return;
        }
        if (_eventModel.End < _eventModel.Start)
        {
            _modalError = "End time must be after start time.";
            return;
        }

        _isSaving = true;
        try
        {
            if (_isEditingEvent)
            {
                var updateReq = new UpdateEventRequest(
                    _eventModel.Title,
                    _eventModel.Start,
                    _eventModel.End,
                    _eventModel.IsAllDay,
                    _eventModel.Location,
                    null);
                await CalendarApi.UpdateEventAsync(_eventModel.Id, updateReq);
            }
            else
            {
                var createReq = new CreateEventRequest(
                    _selectedCalendarIds.ToList(),
                    _eventModel.Title,
                    _eventModel.Start,
                    _eventModel.End,
                    _eventModel.IsAllDay,
                    _eventModel.Location,
                    null);
                await CalendarApi.CreateEventAsync(createReq);
            }
            Close();
            await OnSaved.InvokeAsync();
        }
        catch (Exception ex)
        {
            _modalError = "An error occurred while saving.";
            Console.WriteLine(ex.ToString());
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task DeleteEvent()
    {
        if (_eventModel.Id == Guid.Empty) return;
        _isSaving = true;
        try
        {
            await CalendarApi.DeleteEventAsync(_eventModel.Id);
            Close();
            await OnDeleted.InvokeAsync();
        }
        catch (Exception ex)
        {
            _modalError = "An error occurred while deleting.";
            Console.WriteLine(ex.ToString());
        }
        finally
        {
            _isSaving = false;
        }
    }

    private class EventFormModel
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public DateTimeOffset Start { get; set; }
        public DateTimeOffset End { get; set; }
        public bool IsAllDay { get; set; }
        public string? Location { get; set; }
        public List<CalendarSummaryViewModel> AllCalendars { get; set; } = new();
    }
}
```

- [ ] **Step 2: Add `_eventModal` ref field and bridge methods to `Index.razor` @code**

Add the field:
```csharp
private EventModal _eventModal = default!;
```

Add bridge methods (replacing the existing `OpenAddEventModal` and `OpenEditEventModal`):
```csharp
private void OpenAddEventModal(DateTime date, Guid? calendarId = null) =>
    _eventModal.OpenForCreate(date, calendarId);

private void OpenEditEventModal(CalendarEventViewModel evt) =>
    _eventModal.OpenForEdit(evt);
```

Remove from `Index.razor` @code: `_showEventModal`, `_isEditingEvent`, `_eventModel`, `_selectedCalendarIds`, `_modalError`, `_isSaving`, `_eventModelStartDate`, `_eventModelEndDate`, `SaveEvent()`, `DeleteEvent()`, `CloseEventModal()`, and the `EventFormModel` private class.

- [ ] **Step 3: Replace the Event modal block in `Index.razor`**

Replace the entire `@if (_showEventModal) { ... }` block with:
```razor
<EventModal @ref="_eventModal"
            Calendars="_calendars"
            OnSaved="LoadMonthDataAsync"
            OnDeleted="LoadMonthDataAsync" />
```

- [ ] **Step 4: Build to verify no errors**

```
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/EventModal.razor \
        src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "refactor: extract EventModal component with own state and API calls"
```

---

### Task 7: Create MonthView component

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/Dashboard/MonthView.razor`
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor`

- [ ] **Step 1: Create `MonthView.razor`**

```razor
@* Month nav bar *@
<div class="d-flex justify-content-between align-items-center mb-4">
    <div class="btn-group">
        <button class="btn btn-outline-primary" @onclick="OnPreviousMonth">&lt; Prev</button>
        <button class="btn btn-light" style="min-width: 150px; font-weight: 600;" @onclick="OnOpenQuickJump">
            @CurrentMonth.ToString("MMMM yyyy")
        </button>
        <button class="btn btn-outline-primary" @onclick="OnNextMonth">Next &gt;</button>
    </div>
    <button class="btn btn-primary ms-3" @onclick="() => OnAddEvent.InvokeAsync(DateTime.Today)" data-testid="add-event-btn">
        <i class="bi bi-plus-circle"></i> Add Event
    </button>
</div>

@* Month grid *@
<table class="month-table">
    <thead>
        <tr>
            <th style="width: 14.28%">Sun</th>
            <th style="width: 14.28%">Mon</th>
            <th style="width: 14.28%">Tue</th>
            <th style="width: 14.28%">Wed</th>
            <th style="width: 14.28%">Thu</th>
            <th style="width: 14.28%">Fri</th>
            <th style="width: 14.28%">Sat</th>
        </tr>
    </thead>
    <tbody>
        @foreach (var week in Weeks)
        {
            <tr>
                @foreach (var day in week)
                {
                    var isCurrentMonth = day.Date.Month == CurrentMonth.Month;
                    var isToday = day.Date.Date == DateTime.Today;
                    var isWeekend = day.Date.DayOfWeek == DayOfWeek.Saturday || day.Date.DayOfWeek == DayOfWeek.Sunday;

                    var cssClass = "calendar-cell";
                    if (!isCurrentMonth) cssClass += " text-muted bg-light";
                    if (isToday) cssClass += " today-cell";
                    else if (isWeekend && isCurrentMonth) cssClass += " weekend-cell";

                    <td class="@cssClass" id="day-cell-@day.Date.ToString("yyyy-MM-dd")">
                        <div class="d-flex justify-content-between align-items-center">
                            <span style="font-weight: @(isToday ? "700" : "400")">@day.Date.Day</span>
                            <button class="btn btn-sm btn-link p-0 text-muted shadow-none" title="Add event on this day"
                                    @onclick="() => OnAddEvent.InvokeAsync(day.Date)">
                                <span style="font-size: 1.2rem; line-height: 1;">+</span>
                            </button>
                        </div>
                        <div class="mt-2">
                            @foreach (var evt in day.Events.Take(3))
                            {
                                <div class="event-capsule"
                                     style="background-color: @(evt.CalendarColor ?? "var(--primary)"); cursor: pointer;"
                                     title="@evt.Title (@evt.Start.ToString("t")) - @evt.Location"
                                     @onclick="() => OnEditEvent.InvokeAsync(evt)">
                                    @if (evt.IsAllDay)
                                    {
                                        <span>@evt.Title</span>
                                    }
                                    else
                                    {
                                        <span><b>@evt.Start.ToString("h:mm tt")</b> @evt.Title</span>
                                    }
                                </div>
                            }
                            @if (day.Events.Count() > 3)
                            {
                                <div class="overflow-indicator mt-1 text-center"
                                     @onclick="() => OnDayDrillDown.InvokeAsync(day.Date)"
                                     data-testid="overflow-indicator">
                                    +@(day.Events.Count() - 3) more
                                </div>
                            }
                        </div>
                    </td>
                }
            </tr>
        }
    </tbody>
</table>

@code {
    [Parameter] public DateTime CurrentMonth { get; set; }
    [Parameter] public List<List<DayItem>> Weeks { get; set; } = new();
    [Parameter] public EventCallback OnPreviousMonth { get; set; }
    [Parameter] public EventCallback OnNextMonth { get; set; }
    [Parameter] public EventCallback OnOpenQuickJump { get; set; }
    [Parameter] public EventCallback<DateTime> OnAddEvent { get; set; }
    [Parameter] public EventCallback<CalendarEventViewModel> OnEditEvent { get; set; }
    [Parameter] public EventCallback<DateTime> OnDayDrillDown { get; set; }
}
```

- [ ] **Step 2: Replace the Month view block in `Index.razor`**

In the `@if (_currentView == DashboardView.Month)` section, replace the nav bar and table with:
```razor
@if (_currentView == DashboardView.Month)
{
    <MonthView CurrentMonth="CurrentMonth"
               Weeks="_weeks"
               OnPreviousMonth="GoToPreviousMonth"
               OnNextMonth="GoToNextMonth"
               OnOpenQuickJump="OpenQuickJumpModal"
               OnAddEvent="OpenAddEventModal"
               OnEditEvent="OpenEditEventModal"
               OnDayDrillDown="OnMonthDrillDown" />
}
```

Add the drill-down handler to `Index.razor` @code:
```csharp
private Task OnMonthDrillDown(DateTime date) => SwitchToView(DashboardView.Day, date);
```

- [ ] **Step 3: Build to verify no errors**

```
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/MonthView.razor \
        src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "refactor: extract MonthView component"
```

---

### Task 8: Create AgendaView component

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/Dashboard/AgendaView.razor`
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor`

- [ ] **Step 1: Create `AgendaView.razor`**

```razor
@* Agenda nav bar *@
<div class="d-flex justify-content-between align-items-center mb-4">
    <div class="btn-group">
        <button class="btn btn-outline-primary" data-testid="agenda-prev-month" @onclick="OnPreviousMonth">&lt; Prev</button>
        <button class="btn btn-light" style="min-width: 150px; font-weight: 600;" data-testid="agenda-month-year-label" @onclick="OnOpenQuickJump">
            @CurrentMonth.ToString("MMMM yyyy")
        </button>
        <button class="btn btn-outline-primary" data-testid="agenda-next-month" @onclick="OnNextMonth">Next &gt;</button>
    </div>
    <button class="btn btn-primary ms-3" data-testid="agenda-create-button" @onclick="() => OnAddEvent.InvokeAsync(DateTime.Today)">
        <i class="bi bi-plus-circle"></i> Add Event
    </button>
</div>

@* Agenda table *@
<div class="agenda-view-container">
    <table class="agenda-table">
        <thead>
            <tr class="agenda-header-row">
                <th class="agenda-day-col">Day</th>
                @foreach (var cal in Calendars)
                {
                    <th data-testid="@($"agenda-calendar-header-{cal.Id}")" class="agenda-cal-col">
                        <span class="cal-color-dot" style="background-color: @(cal.Color ?? "#9e9e9e");"></span>
                        @cal.DisplayName
                    </th>
                }
            </tr>
        </thead>
        <tbody>
            @{
                var daysInMonth = DateTime.DaysInMonth(CurrentMonth.Year, CurrentMonth.Month);
            }
            @for (int d = 1; d <= daysInMonth; d++)
            {
                var date = new DateTime(CurrentMonth.Year, CurrentMonth.Month, d);
                var dateKey = date.ToString("yyyy-MM-dd");
                var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                var isToday = date.Date == DateTime.Today;
                var rowCss = "agenda-day-row"
                    + (isWeekend ? " agenda-weekend-row" : "")
                    + (isToday ? " agenda-today-row" : "");

                <tr class="@rowCss">
                    <td data-testid="@($"agenda-day-label-{dateKey}")" class="agenda-day-col"
                        style="cursor:pointer" @onclick="() => OnAddEvent.InvokeAsync(date)">
                        @date.ToString("dd ddd")
                    </td>
                    @foreach (var cal in Calendars)
                    {
                        var calDayEvents = (MonthView!.Days.TryGetValue(dateKey, out var dayList)
                                ? dayList
                                : new List<CalendarEventViewModel>())
                            .Where(e => e.CalendarInfoId == cal.Id)
                            .OrderBy(e => (e.IsAllDay || e.Start.Date < date.Date) ? 0 : 1)
                            .ThenBy(e => e.Start)
                            .ToList();

                        <td data-testid="@($"agenda-cell-{dateKey}-{cal.Id}")"
                            class="agenda-cal-cell"
                            @onclick="async () => {
                                if (calDayEvents.Count > 0) await OnDayDrillDown.InvokeAsync(date);
                                else await OnAddEventForCalendar.InvokeAsync((date, cal.Id));
                            }">
                            @foreach (var evt in calDayEvents.Take(3))
                            {
                                var isContinuation = !evt.IsAllDay && evt.Start.Date < date.Date;
                                var displayText = (evt.IsAllDay || isContinuation)
                                    ? evt.Title
                                    : $"{evt.Start.LocalDateTime:HH:mm} {evt.Title}";
                                <div data-testid="@($"agenda-event-{evt.Id}-{cal.Id}")"
                                     class="agenda-event-line">
                                    @displayText
                                </div>
                            }
                            @if (calDayEvents.Count > 3)
                            {
                                <div data-testid="@($"agenda-overflow-{dateKey}-{cal.Id}")"
                                     class="agenda-overflow-btn">
                                    +@(calDayEvents.Count - 3) more
                                </div>
                            }
                        </td>
                    }
                </tr>
            }
        </tbody>
    </table>
</div>

@code {
    [Parameter] public DateTime CurrentMonth { get; set; }
    [Parameter] public MonthViewModel? MonthView { get; set; }
    [Parameter] public IReadOnlyList<CalendarSummaryViewModel> Calendars { get; set; } = Array.Empty<CalendarSummaryViewModel>();
    [Parameter] public EventCallback OnPreviousMonth { get; set; }
    [Parameter] public EventCallback OnNextMonth { get; set; }
    [Parameter] public EventCallback OnOpenQuickJump { get; set; }
    [Parameter] public EventCallback<DateTime> OnAddEvent { get; set; }
    [Parameter] public EventCallback<(DateTime Date, Guid CalendarId)> OnAddEventForCalendar { get; set; }
    [Parameter] public EventCallback<DateTime> OnDayDrillDown { get; set; }
}
```

- [ ] **Step 2: Replace the Agenda view block in `Index.razor`**

Replace the `else if (_currentView == DashboardView.MonthAgenda)` section with:
```razor
else if (_currentView == DashboardView.MonthAgenda)
{
    <AgendaView CurrentMonth="CurrentMonth"
                MonthView="_monthView"
                Calendars="_calendars"
                OnPreviousMonth="GoToPreviousMonth"
                OnNextMonth="GoToNextMonth"
                OnOpenQuickJump="OpenQuickJumpModal"
                OnAddEvent="OpenAddEventModal"
                OnAddEventForCalendar="OnAgendaAddEventForCalendar"
                OnDayDrillDown="OnAgendaDrillDown" />
}
```

Add to `Index.razor` @code:
```csharp
private void OnAgendaAddEventForCalendar((DateTime Date, Guid CalendarId) args) =>
    OpenAddEventModal(args.Date, args.CalendarId);

private Task OnAgendaDrillDown(DateTime date) => SwitchToView(DashboardView.Day, date);
```

- [ ] **Step 3: Build to verify no errors**

```
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/AgendaView.razor \
        src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "refactor: extract AgendaView component"
```

---

### Task 9: Create DayView component and finalize Index.razor

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/Dashboard/DayView.razor`
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor`

- [ ] **Step 1: Create `DayView.razor`**

```razor
@inject IJSRuntime JSRuntime

@* Day nav bar *@
<div class="d-flex justify-content-between align-items-center mb-4">
    <div class="btn-group">
        <button class="btn btn-outline-primary" @onclick="() => OnNavigate.InvokeAsync(SelectedDate.AddDays(-1))">&lt; Prev</button>
        <button class="btn btn-light" style="min-width: 150px; font-weight: 600;" @onclick="OnOpenDayPicker" data-testid="day-picker-btn">
            @SelectedDate.ToString("D")
        </button>
        <button class="btn btn-outline-primary" @onclick="() => OnNavigate.InvokeAsync(SelectedDate.AddDays(1))">Next &gt;</button>
    </div>
    <button class="btn btn-primary ms-3" @onclick="() => OnAddEvent.InvokeAsync((SelectedDate, Guid.Empty))" data-testid="add-event-btn">
        <i class="bi bi-plus-circle"></i> Add Event
    </button>
</div>

@* Day grid *@
@{
    var key = SelectedDate.ToString("yyyy-MM-dd");
    var dayEvents = MonthView?.Days.TryGetValue(key, out var evts) == true ? evts : new List<CalendarEventViewModel>();
}
<div class="day-view-container" id="day-view-container">
    <div class="day-view-sticky-header">
        <div class="day-header">
            <div class="time-axis-header"></div>
            @foreach (var cal in Calendars)
            {
                <div class="calendar-header-col" title="@cal.DisplayName">@cal.DisplayName</div>
            }
        </div>
        <div class="all-day-container">
            <div class="all-day-label">All Day</div>
            @foreach (var cal in Calendars)
            {
                var calAllDayEvents = dayEvents.Where(e => e.CalendarInfoId == cal.Id && e.IsAllDay).ToList();
                <div class="all-day-col">
                    @foreach (var evt in calAllDayEvents)
                    {
                        <div class="event-capsule" style="background-color: @(evt.CalendarColor ?? "var(--primary)");"
                             title="@evt.Title"
                             @onclick="() => OnEditEvent.InvokeAsync(evt)">
                            @evt.Title
                        </div>
                    }
                </div>
            }
        </div>
    </div>
    <div class="day-body-scrollable" id="day-body-scrollable">
        <div class="day-body-flex">
            @if (SelectedDate == DateTime.Today)
            {
                <div class="current-time-line" style="top: @GetCurrentTimeTopPosition()%" id="current-time-line"></div>
            }
            <div class="time-axis">
                @for (int i = 0; i < 24; i++)
                {
                    var topPos = (i / 24.0) * 100;
                    var label = i == 0 ? "12 AM" : i < 12 ? $"{i} AM" : i == 12 ? "12 PM" : $"{i - 12} PM";
                    if (i > 0)
                    {
                        <div class="time-slot-label" style="top: @topPos%">@label</div>
                    }
                }
            </div>
            <div class="hour-lines-container">
                @for (int i = 1; i < 24; i++)
                {
                    <div class="hour-line" style="top: @((i / 24.0) * 100)%"></div>
                }
            </div>
            <div class="calendar-cols-container">
                @foreach (var cal in Calendars)
                {
                    var calEvents = dayEvents.Where(e => e.CalendarInfoId == cal.Id).ToList();
                    var placedEvents = GetPlacedEvents(calEvents);
                    <div class="calendar-col" @onclick="(e) => HandleGridClick(e, SelectedDate, cal.Id)">
                        @foreach (var p in placedEvents)
                        {
                            <div class="day-event-block"
                                 style="@GetEventStyle(p, SelectedDate)"
                                 title="@($"{p.Event.Title}\n{p.Event.Start.LocalDateTime:t} - {p.Event.End.LocalDateTime:t}\n{p.Event.Location}")"
                                 @onclick:stopPropagation="true"
                                 @onclick="() => OnEditEvent.InvokeAsync(p.Event)">
                                <div class="fw-bold">@p.Event.Title</div>
                                <div>@p.Event.Start.LocalDateTime.ToString("h:mm tt")</div>
                            </div>
                        }
                    </div>
                }
            </div>
        </div>
    </div>
</div>

@code {
    [Parameter] public DateTime SelectedDate { get; set; }
    [Parameter] public MonthViewModel? MonthView { get; set; }
    [Parameter] public IReadOnlyList<CalendarSummaryViewModel> Calendars { get; set; } = Array.Empty<CalendarSummaryViewModel>();
    [Parameter] public EventCallback<DateTime> OnNavigate { get; set; }
    [Parameter] public EventCallback OnOpenDayPicker { get; set; }
    [Parameter] public EventCallback<CalendarEventViewModel> OnEditEvent { get; set; }
    [Parameter] public EventCallback<(DateTime Date, Guid CalendarId)> OnAddEvent { get; set; }

    private DateTime? _lastScrolledDate;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (SelectedDate.Date == DateTime.Today && _lastScrolledDate != DateTime.Today)
        {
            _lastScrolledDate = DateTime.Today;
            try
            {
                var currentMins = DateTime.Now.TimeOfDay.TotalMinutes;
                await JSRuntime.InvokeVoidAsync("eval", $@"
                    var c = document.getElementById('day-view-container');
                    if(c) {{
                        var target = ({currentMins} / 1440) * 1440;
                        c.scrollTop = target - (c.clientHeight / 4);
                    }}
                ");
            }
            catch { }
        }
        else if (SelectedDate.Date != DateTime.Today)
        {
            _lastScrolledDate = null;
        }
    }

    private void HandleGridClick(MouseEventArgs e, DateTime date, Guid calendarId)
    {
        int totalMinutes = (int)e.OffsetY;
        int roundedMinutes = (totalMinutes / 30) * 30;
        var startDateTime = date.Date.AddMinutes(roundedMinutes);
        OnAddEvent.InvokeAsync((startDateTime, calendarId));
    }

    private double GetCurrentTimeTopPosition()
    {
        var now = DateTime.Now.TimeOfDay;
        return (now.TotalMinutes / 1440.0) * 100;
    }

    private List<PlacedEvent> GetPlacedEvents(IEnumerable<CalendarEventViewModel> events)
    {
        var timedEvents = events.Where(e => !e.IsAllDay).OrderBy(e => e.Start).ThenByDescending(e => e.End).ToList();
        var groups = new List<List<PlacedEvent>>();

        foreach (var evt in timedEvents)
        {
            var pEvent = new PlacedEvent { Event = evt, ColumnIndex = 0, TotalColumns = 1 };
            var overlappingGroup = groups.FirstOrDefault(g => g.Any(e => EventsOverlap(e.Event, evt)));
            if (overlappingGroup != null)
            {
                var usedColumns = overlappingGroup.Where(e => EventsOverlap(e.Event, evt)).Select(e => e.ColumnIndex).ToHashSet();
                int col = 0;
                while (usedColumns.Contains(col)) col++;
                pEvent.ColumnIndex = col;
                overlappingGroup.Add(pEvent);
                int maxCols = overlappingGroup.Max(e => e.ColumnIndex) + 1;
                foreach (var e in overlappingGroup)
                    e.TotalColumns = Math.Max(e.TotalColumns, maxCols);
            }
            else
            {
                groups.Add([pEvent]);
            }
        }

        return groups.SelectMany(g => g).ToList();
    }

    private bool EventsOverlap(CalendarEventViewModel a, CalendarEventViewModel b) =>
        a.Start < b.End && b.Start < a.End;

    private string GetEventStyle(PlacedEvent p, DateTime expectedDate)
    {
        var localStart = p.Event.Start.LocalDateTime;
        var localEnd = p.Event.End.LocalDateTime;
        if (localStart < expectedDate.Date) localStart = expectedDate.Date;
        if (localEnd > expectedDate.Date.AddDays(1)) localEnd = expectedDate.Date.AddDays(1);
        var startMins = localStart.Hour * 60 + localStart.Minute;
        var durationMins = (localEnd - localStart).TotalMinutes;
        if (durationMins < 15) durationMins = 15;
        if (startMins + durationMins > 1440) durationMins = 1440 - startMins;
        var top = (startMins / 1440.0) * 100;
        var height = (durationMins / 1440.0) * 100;
        var width = 100.0 / p.TotalColumns;
        var left = width * p.ColumnIndex;
        return $"top: {top}%; height: {height}%; width: calc({width}% - 1px); left: {left}%; background-color: {p.Event.CalendarColor ?? "var(--primary)"};";
    }

    private class PlacedEvent
    {
        public CalendarEventViewModel Event { get; set; } = null!;
        public int ColumnIndex { get; set; }
        public int TotalColumns { get; set; }
    }
}
```

- [ ] **Step 2: Replace the Day view block in `Index.razor` and clean up @code**

Replace the `else if (_currentView == DashboardView.Day)` section with:
```razor
else if (_currentView == DashboardView.Day)
{
    <DayView SelectedDate="_selectedDate"
             MonthView="_monthView"
             Calendars="_calendars"
             OnNavigate="OnDayNavigate"
             OnOpenDayPicker="OpenDayPickerModal"
             OnEditEvent="OpenEditEventModal"
             OnAddEvent="OnDayAddEvent" />
}
```

Add to `Index.razor` @code:
```csharp
private Task OnDayNavigate(DateTime date) => SwitchToView(DashboardView.Day, date);

private void OnDayAddEvent((DateTime Date, Guid CalendarId) args) =>
    OpenAddEventModal(args.Date, args.CalendarId == Guid.Empty ? null : args.CalendarId);
```

Remove from `Index.razor` @code: `GetPlacedEvents`, `EventsOverlap`, `GetEventStyle`, `GetCurrentTimeTopPosition`, `OnDayGridClick`, and the `PlacedEvent` private class. Also remove the day-view scroll logic from `OnAfterRenderAsync` (only pixel orbit init will remain there after Task 10).

Clean up `OnAfterRenderAsync` in `Index.razor` — remove all its current content; it will be replaced in Task 10:
```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    // pixel orbit init added in Task 10
    await Task.CompletedTask;
}
```

- [ ] **Step 3: Build to verify no errors**

```
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/DayView.razor \
        src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "refactor: extract DayView component; Index.razor is now a thin orchestrator"
```

---

### Task 10: Implement pixel orbit burn-in prevention

**Files:**
- Create: `src/FamilyHQ.WebUi/wwwroot/js/pixel-orbit.js`
- Modify: `src/FamilyHQ.WebUi/wwwroot/index.html`
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor`

- [ ] **Step 1: Create `pixel-orbit.js`**

```javascript
// src/FamilyHQ.WebUi/wwwroot/js/pixel-orbit.js
const ORBIT_PATH = [
    [  0,  0 ],
    [  2,  1 ],
    [  3,  0 ],
    [  2, -1 ],
    [  0, -2 ],
    [ -2, -1 ],
    [ -3,  0 ],
    [ -2,  1 ],
];

const BASE_INTERVAL_MS = 120_000;
const JITTER_MS = 15_000;

function nextInterval() {
    return BASE_INTERVAL_MS + (Math.random() * 2 - 1) * JITTER_MS;
}

export function init(elementId) {
    const el = document.getElementById(elementId);
    if (!el) return;

    el.style.transition = 'transform 2s ease-in-out';

    let step = 0;

    function advance() {
        step = (step + 1) % ORBIT_PATH.length;
        const [x, y] = ORBIT_PATH[step];
        el.style.transform = `translate(${x}px, ${y}px)`;
        setTimeout(advance, nextInterval());
    }

    setTimeout(advance, nextInterval());
}
```

- [ ] **Step 2: Add the script reference to `index.html`**

In `src/FamilyHQ.WebUi/wwwroot/index.html`, add before the closing `</body>` tag (after the Blazor script line):

```html
    <script type="module">
        import { init } from './js/pixel-orbit.js';
        window.pixelOrbit = { init };
    </script>
```

The full `<body>` section should look like:
```html
<body>
    <div id="app">
        <svg class="loading-progress">
            <circle r="40%" cx="50%" cy="50%" />
            <circle r="40%" cx="50%" cy="50%" />
        </svg>
        <div class="loading-progress-text"></div>
    </div>

    <div id="blazor-error-ui">
        An unhandled error has occurred.
        <a href="." class="reload">Reload</a>
        <span class="dismiss">🗙</span>
    </div>
    <script src="_framework/blazor.webassembly#[.{fingerprint}].js"></script>
    <script type="module">
        import { init } from './js/pixel-orbit.js';
        window.pixelOrbit = { init };
    </script>
</body>
```

- [ ] **Step 3: Add `id` to dashboard container and wire orbit init in `Index.razor`**

In `Index.razor`, change the outer div from:
```razor
<div class="dashboard-container">
```
to:
```razor
<div class="dashboard-container" id="dashboard-container">
```

Replace the `OnAfterRenderAsync` stub with:
```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("pixelOrbit.init", "dashboard-container");
        }
        catch { /* JS interop not available on pre-render */ }
    }
}
```

- [ ] **Step 4: Build to verify no errors**

```
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.WebUi/wwwroot/js/pixel-orbit.js \
        src/FamilyHQ.WebUi/wwwroot/index.html \
        src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "feat: add pixel orbit burn-in prevention for continuous portrait display"
```

---

## Verification

After all tasks complete, push to the `dev` branch and confirm the FamilyHQ-Dev pipeline passes all E2E tests (Authentication, MonthCalendarView, DayCalendarView, MonthAgendaView, GoogleCalendarSync features).

All `data-testid` attributes are preserved unchanged in the rendered HTML — no E2E test modifications are required.
