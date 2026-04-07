Feature: Calendar Settings
  As a family member
  I want to manage which calendars are visible and which is the shared family calendar
  So that the dashboard shows the right events for each family member

  Background:
    Given I have a user like "MultiCalUser"
    And I login as the user "MultiCalUser"

  Scenario: Calendars are listed in the settings tab
    When I navigate to the calendar settings tab
    Then I see "Work Calendar" in the calendar settings list
    And I see "Personal Calendar" in the calendar settings list

  Scenario: Hiding a calendar removes its column from the agenda view
    When I hide the "Work Calendar" in calendar settings
    And I view the dashboard
    And I switch to the Agenda View tab
    Then I do not see a column header for "Work Calendar"

  Scenario: The shared calendar is pre-designated in settings
    When I navigate to the calendar settings tab
    Then "Family Calendar" is designated as the shared calendar

  Scenario: Only one calendar can be the shared calendar at a time
    When I navigate to the calendar settings tab
    And I designate "Work Calendar" as the shared calendar
    Then "Work Calendar" is designated as the shared calendar
    And "Family Calendar" is no longer designated as the shared calendar
