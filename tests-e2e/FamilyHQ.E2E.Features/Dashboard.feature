Feature: Dashboard Calendar Viewer
  As a family member
  I want to view my imported events on the dashboard
  So that I can see my upcoming schedule

  Background:
    Given I have a user like "Test Family Member" with calendar "Family Events"
    And the user has an all-day event "School Holiday" tomorrow

  Scenario: View upcoming events on the dashboard month view
    When I view the dashboard
    Then I see the event "School Holiday" displayed on the calendar

  Scenario: Create a new event
    Given I view the dashboard
    When I create an event "Dentist Appointment"
    Then I see the event "Dentist Appointment" displayed on the calendar

  Scenario: Update an existing event
    Given I view the dashboard
    When I rename the event "School Holiday" to "Doctor Appointment"
    Then I see the event "Doctor Appointment" displayed on the calendar

  Scenario: Delete an existing event
    Given I view the dashboard
    When I delete the event "School Holiday"
    Then I do not see the event "School Holiday" displayed on the calendar
