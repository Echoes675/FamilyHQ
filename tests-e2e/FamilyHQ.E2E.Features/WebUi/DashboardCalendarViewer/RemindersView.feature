@RemindersView
Feature: Reminders view
  As a family member glancing at the kiosk
  I want to see what will ping our phones and when
  So that nothing we rely on being told about passes unnoticed

  Every row is one EVENT that still has a reminder due, filed by when the EVENT happens — never one
  row per reminder, and never filed by when any one reminder's own trigger instant falls. An event
  with several reminders due at very different lead times still appears exactly once, in the section
  containing its own start, and nowhere else. A row appears only if at least one of its reminders will
  actually still fire: an event inheriting from a calendar with no defaults does not ping, and neither
  does one whose reminders were removed, so neither appears — and an event whose only reminder has
  already fired is equally absent, even if the event itself has not started yet.

  No Background: scenarios need different seeding orders (a backdoor-seeded event has to exist
  BEFORE the login that triggers the first sync; a calendar's defaults have to be set AFTER any
  backdoor seed but BEFORE that same login, or the seed's own re-post wipes them) — see
  CalendarDefaultRemindersSteps for why. Folding all of that into one shared Background would hide
  the ordering each scenario actually needs.

  A "today" reminder is seeded relative to NOW, not at a fixed clock time, and this is deliberate,
  not a style choice: RemindersController excludes an event once EVERY one of its reminders has
  already fired — a real wall-clock comparison no other seeding helper in this suite has to consider —
  a Month/Day/Agenda assertion only cares which calendar day an event falls on, never what time of day
  it already is. A reminder fixed at "09:00 today" has already fired, and its event is therefore
  correctly ABSENT, for any run that happens to execute after 09:00 — most of the day. See
  GivenTheUserHasATimedEventStartingInMinutesInCalendar.

  Scenario: The timeline is a fourth tab and does not displace the month view
    Given I have a user like "RemindersViewUser"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    Then the dashboard is showing the month view
    When I show the reminders view
    Then the reminders view is showing

  Scenario: A reminder on an event today appears under Today with its lead and start
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Dentist Visit" starting in 140 minutes in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I open the event "Dentist Visit" for editing
    And I give the event a reminder 2 hours before
    And I save the event
    And I show the reminders view
    Then the "Today" section has a row for "Dentist Visit" showing "2 hrs before" and its start time

  # The explicit two-step flip (own reminder first, then explicitly ask for the calendar's usual
  # ones) is used here rather than a bare untouched create — not because a bare create would fail
  # to reach the default state (CreateAsync persists Google's own create response, which already
  # comes back useDefault:true for an untouched event; see CalendarSyncService.cs's remarks), but
  # because the flip reaches the state through a write this scenario itself makes and can reason
  # about directly, the same transition EventReminders.feature's own "Dog Groomer" row exercises.
  # Relying on what a bare create happens to persist would make this scenario's result depend on an
  # implementation detail of the create path rather than on the state it actually asks for.
  Scenario: An inherited reminder appears and is tagged as the calendar's default
    Given I have a user like "RemindersViewUser"
    And the "Appointments" calendar is the active calendar
    And the user has a timed event "Checkup" starting in 65 minutes in "Appointments"
    And the active calendar's usual reminders in Google are 45 minutes
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I change the event "Checkup" to reminders of its own
    And I change the event "Checkup" to the calendar's usual reminders
    And I show the reminders view
    Then the "Today" section has a default-tagged row for "Checkup"

  Scenario: An event whose reminders were removed never appears
    Given I have a user like "RemindersViewUser"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I create the event "Quiet Visit" in "Appointments" with no reminders
    And I show the reminders view
    Then no row names "Quiet Visit" anywhere in the reminders view

  # Same reasoning as the scenario above: the explicit flip reaches the follows-default state
  # (Compute's empty-overrides branch, for a calendar with no defaults of its own) through a write
  # this scenario makes directly, rather than depending on what a bare create happens to persist.
  Scenario: An event inheriting from a calendar with no defaults never appears
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Bin Day" starting in 65 minutes in "Chores"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I change the event "Bin Day" to reminders of its own
    And I change the event "Bin Day" to the calendar's usual reminders
    And I show the reminders view
    Then no row names "Bin Day" anywhere in the reminders view

  Scenario: An event with two reminders of its own appears once, naming how many it has
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Parents Evening" starting in 150 minutes in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I change the event "Parents Evening" to reminders of its own
    And I open the event "Parents Evening" for editing
    And I give the event a reminder 30 minutes before
    And I save the event
    And I show the reminders view
    Then the "Today" section has a row for "Parents Evening" naming 2 reminders

  # The family's own motivating example: an event with reminders due at very different lead times —
  # one soon, one more than two weeks out — must still appear exactly once, under its own start, and
  # NOT under any section one of its reminders' own trigger instants happens to fall in. 360 hours
  # (15 days) before a "next month" event always lands its trigger on or before the LAST day of the
  # CURRENT month — "next month" is always day 15 of the following month (DateExpressionResolver), so
  # 15 days before that is never later than the current month's final day — whatever day of the month
  # this suite happens to run on. That is the exact scattering the old per-ping rows produced and the
  # user explicitly asked to stop.
  Scenario: An event with widely-spaced reminders still appears exactly once, under its own start
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Annual Checkup" at "10:00" on "next month" in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I navigate to the next month
    And I open the event "Annual Checkup" for editing
    And I give the event a reminder 2 hours before
    And I give the event a reminder 360 hours before
    And I save the event
    And I show the reminders view
    Then the row for "Annual Checkup" appears only in the "Next month" section

  Scenario: A shared event's row names each person rather than the shared calendar
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Family Movie Night" starting in 50 minutes in "Work Calendar"
    And the user has the event "Family Movie Night" also in "Personal Calendar"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I open the event "Family Movie Night" for editing
    And I give the event a reminder 30 minutes before
    And I save the event
    And I show the reminders view
    Then the "Today" section has a row for "Family Movie Night" naming "Work Calendar" and "Personal Calendar"

  # The real server clock cannot be moved in E2E (unlike the client-only kiosk day-rollover hook), so
  # this cannot force "today" to actually be the last day of a week. What it proves on every run is
  # the general rule the bucketing code applies unconditionally: Tomorrow is matched before This
  # week, so tomorrow's event is never misfiled regardless of where the week boundary falls. The
  # deterministic edge itself — today IS the last day of the week — is pinned with a controlled
  # DateOnly in ReminderBucketingTests.File_OnTheLastDayOfAWeek_TomorrowIsStillTomorrowAndNotNextMonth.
  # Seeded at a fixed clock time on "tomorrow" rather than relative to now, unlike the "today"
  # scenarios above — tomorrow 08:00 is always in the future no matter what time of day it is today.
  Scenario: Tomorrow's event stays under Tomorrow on the last day of a week
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Bin Collection" at "08:00" on "tomorrow" in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I open the event "Bin Collection" for editing
    And I give the event a reminder 15 minutes before
    And I save the event
    And I show the reminders view
    Then the "Tomorrow" section has a row for "Bin Collection"
    And the "This week" section has no row for "Bin Collection"

  Scenario: A section with nothing in it collapses to one line
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Checkup" starting in 140 minutes in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I change the event "Checkup" to reminders of its own
    And I show the reminders view
    Then the "Next month" section is empty

  Scenario: With nothing coming up the view says so rather than showing a blank panel
    Given I have a user like "RemindersViewUser"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I show the reminders view
    Then the reminders view says there is nothing coming up
    And the all-day footnote is shown

  Scenario: The view always states that same-day all-day reminders cannot be shown
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Checkup" starting in 140 minutes in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I change the event "Checkup" to reminders of its own
    And I show the reminders view
    Then the all-day footnote is shown

  # Seeded "next month", like the Tomorrow scenario above — always in the future regardless of time
  # of day, and also the case this task exists to cover: the event is far outside the month the
  # dashboard has loaded, so tapping the row can only work via the fetch-by-id endpoint (Task 3).
  Scenario: Tapping a row opens that event on its Reminders tab
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Flu Clinic" at "10:00" on "next month" in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I navigate to the next month
    And I open the event "Flu Clinic" for editing
    And I give the event a reminder 2 hours before
    And I save the event
    And I show the reminders view
    And I tap the reminders row for "Flu Clinic"
    Then the event modal is open on "Flu Clinic" showing its Reminders tab

  # The regression target: EnterRemindersViewAsync re-checks the current view after its own fetch
  # completes before starting the per-minute tick loop, specifically so switching away mid-fetch
  # cannot orphan a running timer. Nothing exercises the enter/leave path otherwise — there is no
  # bUnit in this repo, and no other scenario leaves the Reminders tab. This cannot reproduce the
  # race deterministically (the fetch here is too fast to interleave with the tab switch), but it
  # does cover the plain enter-then-leave path, which had no coverage at all.
  Scenario: Leaving the Reminders view for another tab renders the destination cleanly
    Given I have a user like "RemindersViewUser"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I show the reminders view
    And I switch to the Month View tab
    Then the dashboard is showing the month view
