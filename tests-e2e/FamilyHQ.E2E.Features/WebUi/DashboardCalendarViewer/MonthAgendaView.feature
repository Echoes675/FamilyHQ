Feature: Month Agenda View
  As a family member
  I want to view my calendar in a month agenda layout
  So that I can see all days of the current month at a glance with events per calendar column

  # Navigation Scenarios

  Scenario: Agenda tab is visible and navigates to the agenda view
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then I see the month agenda view

  Scenario: Navigating to the previous month updates the displayed days
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate to the previous month on the agenda view
    Then the agenda view shows the previous month

  Scenario: Navigating to the next month updates the displayed days
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate to the next month on the agenda view
    Then the agenda view shows the next month

  Scenario: Switching from Agenda view to Day view preserves context
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I click the "Day View" tab
    Then I see the Day View Container

  Scenario: Tapping "+N more" navigates to the day view for the correct date
    Given I have a user like "OverflowUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has 4 events on "2026-06-15" in "Work Calendar"
    And I login as the user "OverflowUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    And I tap the overflow indicator for "2026-06-15" in "Work Calendar"
    Then I see the Day View Container

  # Display Scenarios

  Scenario: All days of the current month are visible
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then the agenda view shows all days of the current month

  Scenario: Weekend rows have a different background shade
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then weekend rows on the agenda view have the CSS class "agenda-weekend-row"
    And weekday rows on the agenda view do not have the class "agenda-weekend-row"

  Scenario: Today's row is highlighted
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then today's row on the agenda view has the CSS class "agenda-today-row"

  Scenario: Calendar column headers display correct names
    Given I have a user like "MultiCalUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "MultiCalUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then I see a column header for "Work Calendar"
    And I see a column header for "Personal Calendar"

  Scenario: Timed events display in 24hr HH:mm format
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has a timed event "Standup" at "14:30" on "2026-06-15" in "Work Calendar"
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    Then I see the event "14:30 Standup" in the "Work Calendar" column for "2026-06-15"

  Scenario: All-day events display title only with no time prefix
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has an all-day event "Bank Holiday" on "2026-06-17" in "Work Calendar"
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    Then I see the event "Bank Holiday" in the "Work Calendar" column for "2026-06-17"
    And the event "Bank Holiday" has no time prefix in the "Work Calendar" column for "2026-06-17"

  Scenario: A day with more than 3 events shows 3 lines and a "+N more" indicator
    Given I have a user like "OverflowUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has 4 events on "2026-06-15" in "Work Calendar"
    And I login as the user "OverflowUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    Then I see 3 event lines in the "Work Calendar" column for "2026-06-15"
    And I see a "+1 more" indicator in the "Work Calendar" column for "2026-06-15"

  Scenario: An event in two calendars appears in both columns
    Given I have a user like "MultiCalUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has an all-day event "Team Meeting" on "2026-06-20" in "Work Calendar"
    And the user has the event "Team Meeting" also in "Personal Calendar"
    And I login as the user "MultiCalUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    Then I see the event "Team Meeting" in the "Work Calendar" column for "2026-06-20"
    And I see the event "Team Meeting" in the "Personal Calendar" column for "2026-06-20"

  # Interaction Scenarios

  Scenario: Tapping an event opens the edit modal
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has a timed event "Standup" at "09:00" on "2026-06-15" in "Work Calendar"
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    And I tap the event "09:00 Standup" in the "Work Calendar" column for "2026-06-15"
    Then I see the event modal
    And I see the event details for "Standup"

  Scenario: Tapping an empty cell opens the create modal with correct date and calendar
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    And I tap the empty cell in the "Work Calendar" column for "2026-06-25"
    Then I see the event modal
    And the modal start date contains "2026-06-25"
    And the "Work Calendar" chip is pre-selected

  Scenario: Tapping the create button opens the modal with today's date
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I tap the agenda create button
    Then I see the event modal
    And the modal start date contains today's date

  Scenario: Creating an event via the modal refreshes the agenda view
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    And I tap the empty cell in the "Work Calendar" column for "2026-06-20"
    And I fill in and save the event "New Meeting"
    Then I see the event "New Meeting" in the "Work Calendar" column for "2026-06-20"

  # Sync Scenarios

  Scenario: A newly synced event appears in the correct calendar column
    Given I have a user like "SyncUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "SyncUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And a new event "Synced Meeting" is added to Google Calendar on "2026-06-18" in "Work Calendar"
    And Google Calendar sends a webhook notification
    Then I see the event "Synced Meeting" in the "Work Calendar" column for "2026-06-18"

  Scenario: A synced event deletion removes the event from the agenda view
    Given I have a user like "SyncUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has a timed event "Standup" at "09:00" on "2026-06-18" in "Work Calendar"
    And I login as the user "SyncUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    And the event "Standup" is deleted from Google Calendar
    And Google Calendar sends a webhook notification
    Then I do not see "Standup" in the "Work Calendar" column for "2026-06-18"

  Scenario: A synced event update is reflected in the agenda view
    Given I have a user like "SyncUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has a timed event "Old Title" at "09:00" on "2026-06-18" in "Work Calendar"
    And I login as the user "SyncUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    And the event "Old Title" is updated to "New Title" in Google Calendar
    And Google Calendar sends a webhook notification
    Then I see the event "New Title" in the "Work Calendar" column for "2026-06-18"
    And I do not see "Old Title" in the "Work Calendar" column for "2026-06-18"
