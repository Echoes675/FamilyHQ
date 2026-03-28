Feature: Day Calendar View
  As a family member
  I want to view my imported events on the daily dashboard
  So that I can see exactly how my schedule looks for any given day

  Background:
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"

  Scenario: Tab navigation and default dates
    When I view the dashboard
    And I switch to the Day View tab
    Then I see the Day View Container
    And the current time line is visible
    When I switch to the Month View tab
    Then I see the Month View Table

  Scenario: Navigate via "+n more" link
    Given I have a user like "NMoreUser"
    And the "Family Events" calendar is the active calendar
    And the user has a timed event "Event 1" tomorrow at "10:00" for 60 minutes
    And the user has a timed event "Event 2" tomorrow at "11:00" for 60 minutes
    And the user has a timed event "Event 3" tomorrow at "12:00" for 60 minutes
    And the user has a timed event "Event 4" tomorrow at "13:00" for 60 minutes
    And I login as the user "NMoreUser"
    When I view the dashboard
    And I click the "+n more" link for tomorrow
    Then I see the Day View Container
    And I see the timed event "Event 4" displayed in the Day View grid

  Scenario: Navigate via Day Picker
    When I view the dashboard
    And I switch to the Day View tab
    And I select the date "2026-12-25" using the day picker
    Then I see the Day View Container

  Scenario: Create an event by clicking a grid slot
    When I view the dashboard
    And I switch to the Day View tab
    And I click an empty grid slot for calendar "Family Events" at "14:00"
    And I fill in and save the event "Dentist Appointment"
    Then I see the timed event "Dentist Appointment" displayed in the Day View grid

  Scenario: Edit an event from the Day View
    When I view the dashboard
    And I switch to the Day View tab
    And I select the date "tomorrow" using the day picker
    And I rename the event "School Holiday" to "Doctor Appointment"
    Then I see the all-day event "Doctor Appointment" displayed at the top of the Day View

  Scenario: Delete an event from the Day View
    When I view the dashboard
    And I switch to the Day View tab
    And I select the date "tomorrow" using the day picker
    And I delete the event "School Holiday"
    Then I do not see the event "School Holiday" displayed on the calendar

  Scenario: Day View Layout and Columns
    Given I have a user like "LayoutUser"
    And the "Family Events" calendar is the active calendar
    And the user has a timed event "Dentist Checkup" tomorrow at "10:00" for 90 minutes
    And I login as the user "LayoutUser"
    When I view the dashboard
    And I switch to the Day View tab
    And I select the date "tomorrow" using the day picker
    Then there are 1 calendar columns in the day view
    And I see the timed event "Dentist Checkup" displayed in the Day View grid
    And the event "Dentist Checkup" has a height representing 90 minutes

  Scenario: Day view shows 6 calendar columns for a user with 6 calendars
    Given I have a user like "SixCalUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "SixCalUser"
    When I view the dashboard
    And I switch to the Day View tab
    Then there are 6 calendar columns in the day view

  Scenario: Day View displays multi-day events
    Given I have a user like "MultiDayUser"
    And the "Family Events" calendar is the active calendar
    And the user has an all-day event "Vacation" spanning 3 days starting tomorrow
    And the user has a timed event "Conference" starting tomorrow at "09:00" spanning 2 days
    And I login as the user "MultiDayUser"
    When I view the dashboard
    And I switch to the Day View tab
    And I select the date "tomorrow" using the day picker
    Then I see the all-day event "Vacation" displayed at the top of the Day View
    And I see the timed event "Conference" displayed in the Day View grid
