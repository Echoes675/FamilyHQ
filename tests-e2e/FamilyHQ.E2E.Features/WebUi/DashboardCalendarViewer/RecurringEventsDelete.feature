Feature: Recurring Event Delete Scope
  As a family member
  I want to choose how a deletion of a repeating commitment applies
  So that I can remove one occurrence, this and following occurrences, or the whole series

  Background:
    Given I have a user like "TimedEventsUser"
    And the "Appointments" calendar is the active calendar
    And the user has a weekly recurring event "Soccer practice" for 3 occurrences in "Appointments"
    And I login as the user "TimedEventsUser"
    And I view the dashboard
    And I switch to the Day View tab

  Scenario: Deleting one occurrence removes only that occurrence
    When I delete occurrence 2 of "Soccer practice" applying to "this" scope
    Then the event "Soccer practice" no longer appears on occurrence 2
    And the event "Soccer practice" still appears on occurrence 1

  Scenario: Deleting this and following occurrences removes the split point onward
    When I delete occurrence 2 of "Soccer practice" applying to "following" scope
    Then the event "Soccer practice" no longer appears on occurrence 2
    And the event "Soccer practice" still appears on occurrence 1

  Scenario: Deleting all events removes the whole series
    When I delete occurrence 1 of "Soccer practice" applying to "all" scope
    Then the event "Soccer practice" no longer appears on occurrence 1
    And the event "Soccer practice" no longer appears on occurrence 3
