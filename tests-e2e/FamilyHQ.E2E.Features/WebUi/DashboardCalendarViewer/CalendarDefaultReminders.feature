Feature: Calendar default reminders change in Google
  FHQ-207. CI had no aged data: every calendar was created fresh, so a calendar's default
  reminders never CHANGED and RefreshCalendarDefaultsAsync always early-returned. FHQ-205 took
  production down in exactly the path that early return skipped.

  There is no API that exposes DefaultReminders, so these scenarios assert on the CONSEQUENCE.
  Under the FHQ-205 bug the failing write kills the whole sync-all and its drain loop, so an
  event added in the same sync never reaches the dashboard.

  Scenario: A calendar gaining default reminders does not break the sync that carries it
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    # null -> value: the calendar was created with no defaults, exactly as every production
    # calendar predating the Reminders column was.
    When the active calendar's default reminders change to 45 minutes in Google
    And a new event "Defaults Added Event" is added to Google Calendar
    And Google Calendar sends a webhook notification
    And I view the dashboard
    Then I see the event "Defaults Added Event" displayed on the calendar

  Scenario: A calendar changing its default reminders does not break the sync that carries it
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When the active calendar's default reminders change to 30 minutes in Google
    And a new event "First Defaults Event" is added to Google Calendar
    And Google Calendar sends a webhook notification
    And I view the dashboard
    Then I see the event "First Defaults Event" displayed on the calendar
    # value -> different value: the steady-state transition, and the one a calendar hits every
    # time someone changes their Google notification settings.
    When the active calendar's default reminders change to 15 minutes in Google
    And a new event "Changed Defaults Event" is added to Google Calendar
    And Google Calendar sends a webhook notification
    And I view the dashboard
    Then I see the event "Changed Defaults Event" displayed on the calendar
