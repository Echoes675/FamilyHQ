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
    And I switch to the Day View tab
    Then I see the Day View Container

  Scenario: Tapping "+N more" navigates to the day view for the correct date
    Given I have a user like "OverflowUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has 4 events on "tomorrow" in "Work Calendar"
    And I login as the user "OverflowUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I tap the overflow indicator for "tomorrow" in "Work Calendar"
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
    And I do not see a column header for "Family Calendar"

  Scenario: Timed events display in 24hr HH:mm format
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has a timed event "Standup" at "14:30" on "tomorrow" in "Work Calendar"
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then I see the event "14:30 Standup" in the "Work Calendar" column for "tomorrow"

  Scenario: All-day events display title only with no time prefix
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has an all-day event "Bank Holiday" tomorrow in "Work Calendar"
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then I see the event "Bank Holiday" in the "Work Calendar" column for "tomorrow"
    And the event "Bank Holiday" has no time prefix in the "Work Calendar" column for "tomorrow"

  Scenario: A day with more than 3 events shows 3 lines and a "+N more" indicator
    Given I have a user like "OverflowUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has 4 events on "tomorrow" in "Work Calendar"
    And I login as the user "OverflowUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then I see 3 event lines in the "Work Calendar" column for "tomorrow"
    And I see a "+1 more" indicator in the "Work Calendar" column for "tomorrow"

  Scenario: An event in two calendars appears in both columns
    Given I have a user like "MultiCalUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has an all-day event "Team Meeting" tomorrow in "Work Calendar"
    And the user has the event "Team Meeting" also in "Personal Calendar"
    And I login as the user "MultiCalUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then I see the event "Team Meeting" in the "Work Calendar" column for "tomorrow"
    And I see the event "Team Meeting" in the "Personal Calendar" column for "tomorrow"

  Scenario: All six calendar column headers are visible in the agenda view
    Given I have a user like "SixCalUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "SixCalUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then I see a column header for "Work Calendar"
    And I see a column header for "Personal Calendar"
    And I see a column header for "School Calendar"
    And I see a column header for "Sports Calendar"
    And I see a column header for "Health Calendar"
    And I see a column header for "Hobbies Calendar"

  Scenario: Events from all six calendars appear in their respective columns
    Given I have a user like "SixCalUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has an all-day event "Work Event" tomorrow in "Work Calendar"
    And the user has an all-day event "Personal Event" tomorrow in "Personal Calendar"
    And the user has an all-day event "School Event" tomorrow in "School Calendar"
    And the user has an all-day event "Sports Event" tomorrow in "Sports Calendar"
    And the user has an all-day event "Health Event" tomorrow in "Health Calendar"
    And the user has an all-day event "Hobbies Event" tomorrow in "Hobbies Calendar"
    And I login as the user "SixCalUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then I see the event "Work Event" in the "Work Calendar" column for "tomorrow"
    And I see the event "Personal Event" in the "Personal Calendar" column for "tomorrow"
    And I see the event "School Event" in the "School Calendar" column for "tomorrow"
    And I see the event "Sports Event" in the "Sports Calendar" column for "tomorrow"
    And I see the event "Health Event" in the "Health Calendar" column for "tomorrow"
    And I see the event "Hobbies Event" in the "Hobbies Calendar" column for "tomorrow"

  # Interaction Scenarios

  Scenario: Tapping a cell with events navigates to the Day view
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has a timed event "Standup" at "09:00" on "tomorrow" in "Work Calendar"
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I tap the agenda cell in the "Work Calendar" column for "tomorrow"
    Then I see the Day View Container

  Scenario: Tapping an empty cell opens the create modal with correct date and calendar
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to show a date in 5 days
    And I tap the empty cell in the "Work Calendar" column for "in 5 days"
    Then I see the event modal
    And the modal start date contains "in 5 days"
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
    And I navigate the agenda to show a date in 3 days
    And I tap the empty cell in the "Work Calendar" column for "in 3 days"
    And I fill in and save the event "New Meeting"
    Then I see the event "New Meeting" in the "Work Calendar" column for "in 3 days"

  # Sync Scenarios

  Scenario: A newly synced event appears in the correct calendar column
    Given I have a user like "SyncUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "SyncUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to show a date in 2 days
    And a new event "Synced Meeting" is added to Google Calendar on "in 2 days" in "Work Calendar"
    And Google Calendar sends a webhook notification
    Then I see the event "Synced Meeting" in the "Work Calendar" column for "in 2 days"

  Scenario: A synced event deletion removes the event from the agenda view
    Given I have a user like "SyncUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has a timed event "Standup" at "09:00" on "in 2 days" in "Work Calendar"
    And I login as the user "SyncUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to show a date in 2 days
    And the event "Standup" is deleted from Google Calendar
    And Google Calendar sends a webhook notification
    Then I do not see "Standup" in the "Work Calendar" column for "in 2 days"

  Scenario: A synced event update is reflected in the agenda view
    Given I have a user like "SyncUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has a timed event "Old Title" at "09:00" on "in 2 days" in "Work Calendar"
    And I login as the user "SyncUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to show a date in 2 days
    And the event "Old Title" is updated to "New Title" in Google Calendar
    And Google Calendar sends a webhook notification
    Then I see the event "New Title" in the "Work Calendar" column for "in 2 days"
    And I do not see "Old Title" in the "Work Calendar" column for "in 2 days"
