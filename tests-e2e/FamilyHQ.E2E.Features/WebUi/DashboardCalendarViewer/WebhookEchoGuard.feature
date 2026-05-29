@WebhookEchoGuard
Feature: Webhook self-echo guard
  Outbound writes to Google Calendar do not cause infinite write-webhook-write loops.
  When FamilyHQ edits an event and Google echoes the change back as a webhook,
  the sync pipeline detects and skips the echo. Google-side edits still flow through.

  Background:
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar

  Scenario: A FamilyHQ-side edit produces exactly one outbound write to Google
    Given the user has an all-day event "Team Lunch" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When I update the event title to "Team Lunch with Alice"
    And I wait for any follow-up webhooks to be processed
    Then exactly one outbound write to Google has been recorded for the event
    And the event still shows the updated title "Team Lunch with Alice" on the dashboard

  Scenario: A Google-side edit on an existing event is not skipped
    Given the user has an all-day event "Friday Review" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When the event title is updated directly in Google to "Friday Review (rescheduled)"
    And Google Calendar sends a webhook notification
    Then the dashboard shows the updated title "Friday Review (rescheduled)"
    And the FamilyHQ to Google write count for the event is 0
