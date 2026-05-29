Feature: Recurring Event Create and Toggle
  As a family member
  I want to create and modify recurring events directly in FamilyHQ
  So that a repeating commitment is written to my calendar and reflected back as individual occurrences

  Background:
    Given I have a user like "TimedEventsUser"
    And the "Appointments" calendar is the active calendar
    And I login as the user "TimedEventsUser"

  Scenario: Creating a weekly recurring event shows one occurrence per week
    When I create a weekly recurring event "Soccer practice" in "Appointments"
    And I switch to the Day View tab
    Then the event "Soccer practice" appears on each of its 3 weekly occurrence dates

  Scenario: Creating a recurring event on a chosen weekday lands on that weekday
    When I create a recurring event "Swim lessons" in "Appointments" repeating weekly on the day in 3 days
    And I switch to the Day View tab
    Then the event "Swim lessons" appears weekly starting in 3 days for 2 occurrences

  Scenario: Turning off recurrence collapses the series to a single event
    When I create a weekly recurring event "Book club" in "Appointments"
    And I switch to the Day View tab
    And I turn off recurrence for the event "Book club"
    Then only a single non-recurring "Book club" event remains
