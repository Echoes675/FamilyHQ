Feature: Calendar default reminders
  FHQ-207. CI had no aged data: every calendar was created fresh, so a calendar's default
  reminders never CHANGED and RefreshCalendarDefaultsAsync always early-returned. FHQ-205 took
  production down in exactly the path that early return skipped.

  There is no API that exposes DefaultReminders, so the sync scenarios assert on the CONSEQUENCE.
  Under the FHQ-205 bug the failing write kills the whole sync-all and its drain loop, so an
  event added in the same sync never reaches the dashboard.

  The last two scenarios are about what the kiosk TELLS a family instead. A calendar reporting an
  empty list of usual reminders and a calendar nothing has reported on are different answers, and
  only the first may be described as notifying nobody. Told the wrong one, a family can save that
  reading back — and because Google replaces the whole reminders object, the reminders the event was
  really inheriting are gone. The two messages are asserted by test id, in both directions, because
  they render in the same place for the same reason and a scenario that only looked for one of them
  would pass again the moment they were collapsed.

  Background:
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar

  Scenario: A calendar gaining default reminders does not break the sync that carries it
    Given I login as the user "TestFamilyMember"
    And I view the dashboard
    # null -> value: the calendar was created with no defaults, exactly as every production
    # calendar predating the Reminders column was.
    When the active calendar's default reminders change to 45 minutes in Google
    And a new event "Defaults Added Event" is added to Google Calendar
    And Google Calendar sends a webhook notification
    And I view the dashboard
    Then I see the event "Defaults Added Event" displayed on the calendar

  Scenario: A calendar changing its default reminders does not break the sync that carries it
    Given I login as the user "TestFamilyMember"
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

  Scenario: A calendar with no usual reminders says so, rather than that they can't be read
    # An empty defaultReminders array: Google's answer for a calendar that really has none.
    Given the active calendar has no usual reminders in Google
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    And the event "Swimming Lesson" in "Family Events" follows the calendar's usual reminders
    When I open the event "Swimming Lesson" for editing
    Then the Reminders tab says this calendar has no usual reminders

  Scenario: A calendar whose usual reminders nothing has reported says they can't be read
    # No backdoor call, so Google omits the array entirely. Nothing has told FamilyHQ what this
    # calendar's usual reminders are, which is not the same as being told it has none — Google may
    # still be applying some, so the tab may not claim the event will notify nobody.
    Given I login as the user "TestFamilyMember"
    And I view the dashboard
    And the event "Piano Practice" in "Family Events" follows the calendar's usual reminders
    When I open the event "Piano Practice" for editing
    Then the Reminders tab says this calendar's usual reminders can't be read
