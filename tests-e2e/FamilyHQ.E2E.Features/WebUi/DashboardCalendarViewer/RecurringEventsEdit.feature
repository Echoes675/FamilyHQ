Feature: Recurring Event Edit Scope
  As a family member
  I want to choose how an edit to a repeating commitment applies
  So that I can change one occurrence, this and following occurrences, or the whole series

  Background:
    Given I have a user like "TimedEventsUser"
    And the "Appointments" calendar is the active calendar
    And the user has a weekly recurring event "Soccer practice" for 3 occurrences in "Appointments"
    And I login as the user "TimedEventsUser"
    And I view the dashboard
    And I switch to the Day View tab

  Scenario: Editing one occurrence changes only that occurrence
    When I change occurrence 2 of "Soccer practice" to "Dentist" applying to "this" scope
    Then the event "Dentist" appears on occurrence 2
    And the event "Soccer practice" still appears on occurrence 1

  Scenario: Editing this and following occurrences changes the split point onward
    When I change occurrence 2 of "Soccer practice" to "Football training" applying to "following" scope
    Then the event "Football training" appears on occurrence 2
    And the event "Soccer practice" still appears on occurrence 1

  Scenario: Editing all events keeps a previously edited occurrence's override
    Given occurrence 2 of "Soccer practice" has already been changed to "Dentist"
    When I change occurrence 1 of "Soccer practice" to "Training camp" applying to "all" scope
    Then the event "Training camp" appears on occurrence 3
    And the event "Dentist" still appears on occurrence 2
