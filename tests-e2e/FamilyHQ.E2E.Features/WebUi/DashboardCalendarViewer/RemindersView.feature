@RemindersView
Feature: Reminders view
  As a family member glancing at the kiosk
  I want to see what will ping our phones and when
  So that nothing we rely on being told about passes unnoticed

  Every row is one EVENT whose reminders were set ON THE EVENT, filed by when the EVENT happens —
  never one row per reminder, and never filed by when any one reminder's own trigger instant falls.
  An event with several reminders due at very different lead times still appears exactly once, in the
  section containing its own start, and nowhere else.

  Which events appear is narrower than "will this ping". An event that merely follows its calendar's
  usual reminders is left out although its phone really will go off, because the subject of this view
  is reminders somebody deliberately set; the family chose that, knowing it under-reports, on the
  condition that the view says so in a permanent footnote. An event whose reminders were removed, and
  one inheriting from a calendar with no defaults, do not appear either. An all-day event as Google
  CREATES it is NOT caught by the inherited exclusion and must keep appearing: Google materialises a
  calendar's defaults onto it instead of letting it inherit, so a birthday or bin-day event arrives
  carrying reminders of its own. (The kiosk's own All-day toggle can send a revert-to-default body
  on an all-day event; what Google returns for that is recorded, not yet known — preprod smoke RM4.)

  A reminder having already fired is not a reason to drop a row — on the day of an event its
  reminders have usually all gone off, which is exactly when the family needs the row most, so it
  stays put and says "all sent" instead.

  No Background: scenarios need different seeding orders (a backdoor-seeded event has to exist
  BEFORE the login that triggers the first sync; a calendar's defaults have to be set AFTER any
  backdoor seed but BEFORE that same login, or the seed's own re-post wipes them) — see
  CalendarDefaultRemindersSteps for why. Folding all of that into one shared Background would hide
  the ordering each scenario actually needs.

  A "today" event is seeded relative to NOW, not at a fixed clock time, and this is deliberate, not a
  style choice: whether a reminder has fired yet decides what its row SAYS, and only a start relative
  to now keeps that answer the same whatever time of day the suite runs. A reminder set against a
  fixed "09:00 today" has already fired for most of the day, so a scenario expecting "next 2 hrs
  before" would read "all sent" instead. The scenario below that wants an already-fired reminder asks
  for one explicitly, by giving a lead time longer than the gap to the event. Seeding this way has one
  constraint of its own — the start must still land on today's date — which
  GivenTheUserHasATimedEventStartingInMinutesInCalendar refuses outright rather than letting the
  scenario fail as a missing row.

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
    And the "Today" section has a row for "Dentist Visit"

  # The ordinary state of a row on the day of its own event, and the one the view used to get wrong by
  # dropping the row entirely. A reminder 3 hours before an event 65 minutes away has already fired,
  # so there is nothing left pending — but the event itself is still ahead, which is the whole reason
  # the family is looking at the panel.
  Scenario: An event keeps its row under Today after its last reminder has fired
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Eye Test" starting in 65 minutes in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I open the event "Eye Test" for editing
    And I give the event a reminder 3 hours before
    And I save the event
    And I show the reminders view
    Then the "Today" section has a row for "Eye Test" saying its reminders have all been sent

  # The pin for the whole exclusion, and the one case that used to produce a row: the calendar HAS
  # usual reminders, so "Checkup" really will ping the family's phones — it is absent because nobody
  # set that reminder on the event.
  #
  # "Swim Lesson" is a control, not decoration. Without it the Then would pass just as well if the
  # sync never ran, the tab never rendered or the whole timeline came back empty — the scenario would
  # stop being able to fail for the reason it exists for. One event whose reminders were set on the
  # event itself must be listed in the same view that leaves the inheriting one out. It ends up with
  # TWO of them, not one: switching inheritance off copies the calendar's 45-minute default in as an
  # editable row before the 30 is added (the gotcha is documented in e2e-testing-maintenance.md).
  # Harmless here, because the Then asserts the row's presence and nothing about its count — but two
  # is what is on screen. Seeded at the same 65 minutes as "Checkup" deliberately: a scenario's
  # widest seeding offset is what decides how close to local midnight it can still run, so matching
  # the offset adds a control without costing any of that margin.
  #
  # The explicit two-step flip (own reminder first, then explicitly ask for the calendar's usual
  # ones) is used rather than a bare untouched create — not because a bare create would fail to reach
  # the default state (CreateAsync persists Google's own create response, which already comes back
  # useDefault:true for an untouched event; see CalendarSyncService.cs's remarks), but because the
  # flip reaches the state through a write this scenario itself makes and can reason about directly,
  # the same transition EventReminders.feature's own "Dog Groomer" row exercises. Relying on what a
  # bare create happens to persist would make this scenario's result depend on an implementation
  # detail of the create path rather than on the state it actually asks for.
  Scenario: An event following its calendar's usual reminders never appears
    Given I have a user like "RemindersViewUser"
    And the "Appointments" calendar is the active calendar
    And the user has a timed event "Checkup" starting in 65 minutes in "Appointments"
    And the user has a timed event "Swim Lesson" starting in 65 minutes in "Appointments"
    And the active calendar's usual reminders in Google are 45 minutes
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I open the event "Swim Lesson" for editing
    And I give the event a reminder 30 minutes before
    And I save the event
    And I change the event "Checkup" to reminders of its own
    And I change the event "Checkup" to the calendar's usual reminders
    And I show the reminders view
    Then the "Today" section has a row for "Swim Lesson"
    And no row names "Checkup" anywhere in the reminders view

  Scenario: An event whose reminders were removed never appears
    Given I have a user like "RemindersViewUser"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I create the event "Quiet Visit" in "Appointments" with no reminders
    And I show the reminders view
    Then no row names "Quiet Visit" anywhere in the reminders view

  # Same reasoning as the scenario above: the explicit flip reaches the follows-default state through
  # a write this scenario makes directly, rather than depending on what a bare create happens to
  # persist. Both scenarios land on that same follows-default state, which RemindersController.RowFor
  # excludes BEFORE ReminderPingCalculator.Compute is called — so neither one reaches Compute's own
  # empty-overrides branch, and this scenario does not cover a different branch from the one above.
  # That branch is pinned at the unit level, by
  # ReminderPingCalculatorTests.Compute_WhenTheEventInheritsAndTheCalendarHasNoDefaults_ProducesNothing.
  # A second origin for the same state is still worth having, which is why the scenario stays.
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
    And the inherited-reminders footnote is shown

  Scenario: The view always states that same-day all-day reminders cannot be shown
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Checkup" starting in 140 minutes in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I change the event "Checkup" to reminders of its own
    And I show the reminders view
    Then the all-day footnote is shown

  # The other half of the requirement the exclusion came with, and permanent for the same reason as
  # the all-day note: a timeline that looks complete and is not is the exact failure this view exists
  # to prevent. Asserted with a row on screen as well as on the empty view, because "nothing is
  # hidden right now" is precisely when the line looks like clutter worth tidying away.
  Scenario: The view always states that events on their calendar's usual reminders are left out
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Checkup" starting in 65 minutes in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I open the event "Checkup" for editing
    And I give the event a reminder 30 minutes before
    And I save the event
    And I show the reminders view
    Then the "Today" section has a row for "Checkup"
    And the inherited-reminders footnote is shown

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
