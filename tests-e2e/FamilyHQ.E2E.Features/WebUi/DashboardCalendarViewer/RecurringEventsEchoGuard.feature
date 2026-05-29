@WebhookEchoGuard
Feature: Recurring Event Echo Guard
  As the calendar sync pipeline
  I want a single recurring write to produce exactly one outbound write to Google
  So that the fan-out echo webhooks for the expanded instances do not cause write loops

  Background:
    Given I have a user like "TimedEventsUser"
    And the "Appointments" calendar is the active calendar
    And I login as the user "TimedEventsUser"
    And I view the dashboard
    And I switch to the Day View tab

  Scenario: An All events edit produces exactly one outbound write
    Given the user has a weekly recurring event "Yoga" for 5 occurrences with a tracked write count in "Appointments"
    When Google Calendar sends a webhook notification
    And I rename the recurring series "Yoga" to "Hot Yoga" applying to all events
    And I wait for the recurring fan-out webhooks to be processed
    Then exactly one outbound write to Google is recorded for the series

  Scenario: Native creation of a recurring series produces exactly one outbound write
    When I create a weekly recurring event "Trash day" in "Appointments" tracking outbound writes
    And I wait for the recurring fan-out webhooks to be processed
    Then exactly one outbound write to Google is recorded in total
