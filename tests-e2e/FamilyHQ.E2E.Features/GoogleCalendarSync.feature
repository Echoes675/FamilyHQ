Feature: Google Calendar Webhook Sync
  As a family member
  I want my dashboard to reflect changes made in Google Calendar
  So that I always see an up-to-date view of my schedule

  Scenario: New event added in Google Calendar appears on dashboard after sync
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When a new event "Dentist Appointment" is added to Google Calendar
    And Google Calendar sends a webhook notification
    And I view the dashboard
    Then I see the event "Dentist Appointment" displayed on the calendar

  Scenario: Event updated in Google Calendar shows new title after sync
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When the event "School Holiday" is updated to "School Holiday (Cancelled)" in Google Calendar
    And Google Calendar sends a webhook notification
    And I view the dashboard
    Then I see the event "School Holiday (Cancelled)" displayed on the calendar

  Scenario: Event deleted in Google Calendar disappears after sync
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When the event "School Holiday" is deleted from Google Calendar
    And Google Calendar sends a webhook notification
    And I view the dashboard
    Then I do not see the event "School Holiday" displayed on the calendar

  Scenario: New event added in Google Calendar appears live on open dashboard
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When a new event "Dentist Appointment" is added to Google Calendar
    And Google Calendar sends a webhook notification
    Then the dashboard live-updates to show "Dentist Appointment"

  Scenario: Event updated in Google Calendar shows live on open dashboard
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When the event "School Holiday" is updated to "School Holiday (Cancelled)" in Google Calendar
    And Google Calendar sends a webhook notification
    Then the dashboard live-updates to show "School Holiday (Cancelled)"

  Scenario: Event deleted in Google Calendar disappears live from open dashboard
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When the event "School Holiday" is deleted from Google Calendar
    And Google Calendar sends a webhook notification
    Then the dashboard live-updates to remove "School Holiday"
