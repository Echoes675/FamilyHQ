@RemindersView
Feature: Reminders view
  As a family member glancing at the kiosk
  I want to see what will ping our phones and when
  So that nothing we rely on being told about passes unnoticed

  Every row is one EVENT whose reminders were set ON THE EVENT, filed by when the EVENT happens —
  never one row per reminder, and never filed by when any one reminder's own trigger instant falls.
  An event with several reminders due at very different lead times still appears exactly once, in the
  panel containing its own start, and nowhere else.

  The timeline is two panels side by side: "This month" runs from today — today included — to the end
  of this month, and "Next month" spans the whole month after it. There is no Today, Tomorrow or This
  week panel any more. Each of those could be left with no day at all to claim depending on where
  today sat in its own period, and an empty panel on the view a wall-mounted kiosk opens on reads as
  a fault rather than as good news. What those panels said for the near rows is now said by the rows
  themselves, which is why "[when]" below names a day.

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
  stays put, and reads no differently for it.

  A row says "[when] · title · [who]" and nothing about the reminders it exists because of: the
  family asked for the bell glyph and the "1 reminder · next 45 min before" line to go, because on
  a wall display they crowded out the three things a row is glanced at for. The FILTER above is what
  makes this a reminders view, not anything printed on a row. "[when]" is the row's own day and then
  its event's start — "Today · 09:00", "Tomorrow · 21:30", "Wed 7 Oct · 08:00", "Today · all day".
  The day is read off the row's OWN date against today, never off the panel holding it: on the last
  day of a month tomorrow is filed under "Next month" and must still read "Tomorrow" there, which is
  the day the family can least afford to read as a bare date. That is why a scenario about what a row
  SAYS never names a panel, while a scenario about which panel holds a row names one only for a date
  whose panel is the same on every run date — "today", or an event resolved into next month.

  Losing the Today panel lost the at-a-glance signal that something is on today, so the Reminders
  TAB carries a bell and a count in its place. Its whole requirement is being right while the family
  is standing in front of a different view, which is why the scenario for it reads the tab from the
  Month view; and the absence case is the badge not being there at all, because a pill reading "0" is
  a standing claim where silence is the signal.

  No Background: scenarios need different seeding orders (a backdoor-seeded event has to exist
  BEFORE the login that triggers the first sync; a calendar's defaults have to be set AFTER any
  backdoor seed but BEFORE that same login, or the seed's own re-post wipes them) — see
  CalendarDefaultRemindersSteps for why. Folding all of that into one shared Background would hide
  the ordering each scenario actually needs.

  A "today" event is seeded at a FIXED time — 09:00 — not at an offset from now. That is not a
  convenience: a row is filed by its event's start DATE and stays under today for the whole of that
  day, after the event has begun and after every one of its reminders has fired.
  RemindersController's window opens at local midnight rather than at now, and RowFor reads no clock
  at all, so a 09:00 event is in the response at 23:00 just as it is at 09:30. Seeding against now
  instead made every one of these scenarios refuse to run inside its own offset of local midnight,
  and they failed on a CI pass that happened to start thirteen minutes later than the one before it.
  A fixed seed is also the louder of the two if that near edge ever moves back to now: a 09:00 event
  would drop out of the response, where a future one would not.

  No scenario here is seeded relative to "now" at all, and the one whose subject IS where "now" sits
  — a reminder that has already fired on an event that has not happened yet — does not need to be:
  it puts the reminder in the past instead of putting the event near the present, and its own comment
  has the arithmetic. So nothing in this file refuses to run near local midnight.

  Scenario: The timeline and the month view do not displace each other
    Given I have a user like "RemindersViewUser"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    Then the dashboard is showing the month view
    When I show the reminders view
    Then the reminders view is showing

  # Both panels span a whole month, so every row names its own day — and the two days a family reads
  # first are named in words, which is what the Today and Tomorrow panels used to do for them.
  #
  # The assertion is an EQUALITY on the row's leading column, and that is the scenario: a row that
  # lost its day and reverted to the bare "09:00" is the reversion this guards, and only equality
  # catches it. It names no panel, because a row's day is a fact about the row's own date rather than
  # about the heading above it.
  #
  # The expected text is written out here rather than read back from what the seeding step computed,
  # which is what the fixed seed buys. It is the stronger form: a seeding bug that derived the wrong
  # start used to produce the same wrong value on both sides of the comparison and pass.
  Scenario: A row leads with its event's own day and start time and nothing else
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Dentist Visit" at "09:00" on "today" in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I open the event "Dentist Visit" for editing
    And I give the event a reminder 2 hours before
    And I save the event
    And I show the reminders view
    Then the row for "Dentist Visit" leads with "Today · 09:00"

  # The ordinary state of a row on the day of its own event, and the one the view used to get wrong by
  # dropping the row entirely. A reminder 3 hours before an event 65 minutes away has already fired,
  # so there is nothing left pending — but the event itself is still ahead, which is the whole reason
  # the family is looking at the panel. The row's PRESENCE is the whole assertion now: it used to also
  # say "all sent", and with nothing on a row describing its reminders there is no longer any wording
  # for firing to change. The scenario stays because what it pins is that the row exists at all.
  #
  # "Fired, but not yet happened" is a statement about where NOW sits, which is why this scenario was
  # once seeded relative to the clock — and why it could not run in the last 65 minutes before local
  # midnight. It does not need to be. An event TOMORROW with a reminder 48 hours before it puts that
  # reminder YESTERDAY: fired at every hour of today, on an event that has not happened at any hour of
  # today. Both halves hold whenever the suite runs, so the clock is out of the precondition entirely
  # rather than merely given a wider margin.
  #
  # Which panel holds the row is incidental to what it pins, so the assertion reads the row's own
  # label instead of naming one: tomorrow is in "This month" on every day but a month's last, when it
  # is in "Next month", and that difference has nothing to do with whether a row survives its
  # reminders firing.
  Scenario: An event keeps its row after its last reminder has fired
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Eye Test" at "09:00" on "tomorrow" in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    And the event "Eye Test" on "tomorrow" has been given a reminder 48 hours before
    When I show the reminders view
    Then the row for "Eye Test" leads with "Tomorrow · 09:00"

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
  # is what is on screen. Seeded at the same 09:00 as "Checkup", which costs nothing now that neither
  # start is measured from the clock: two rows at the same instant are ordered by title
  # (RemindersController's ThenBy), and both assertions address their row by event id regardless.
  #
  # "This month" is safe to name here, and in every other scenario seeded on "today": today is the
  # near edge of that panel on every run date.
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
    And the user has a timed event "Checkup" at "09:00" on "today" in "Appointments"
    And the user has a timed event "Swim Lesson" at "09:00" on "today" in "Appointments"
    And the active calendar's usual reminders in Google are 45 minutes
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I open the event "Swim Lesson" for editing
    And I give the event a reminder 30 minutes before
    And I save the event
    And I change the event "Checkup" to reminders of its own
    And I change the event "Checkup" to the calendar's usual reminders
    And I show the reminders view
    Then the "This month" section has a row for "Swim Lesson"
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
    And the user has a timed event "Bin Day" at "09:00" on "today" in "Chores"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I change the event "Bin Day" to reminders of its own
    And I change the event "Bin Day" to the calendar's usual reminders
    And I show the reminders view
    Then no row names "Bin Day" anywhere in the reminders view

  # The family's own motivating example: an event with reminders due at very different lead times —
  # one soon, one more than two weeks out — must still appear exactly once, under its own start, and
  # NOT in whichever panel one of its reminders' own trigger instants happens to fall in. 360 hours
  # (15 days) before a "next month" event always lands its trigger on or before the LAST day of the
  # CURRENT month — "next month" is always day 15 of the following month (DateExpressionResolver), so
  # 15 days before that is never later than the current month's final day — whatever day of the month
  # this suite happens to run on. So the early trigger always falls in the OTHER panel from the
  # event, which is what makes this a scattering check at all: that is the exact scattering the old
  # per-ping rows produced and the user explicitly asked to stop.
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

  # The other form "[when]" takes, and the failure the family actually reported: three occurrences of
  # one recurring series in the same panel all read "10:00 · Series from Google" and were
  # indistinguishable. Both panels cover a whole month, so a row that is neither today nor tomorrow
  # has to say which day it is.
  #
  # Seeded "next month" because this Then NAMES a panel — it reads the date off the same snapshot
  # that places the row — and that is the only relative expression whose panel is the same on every
  # run date: "next month" resolves to the 15th of the following month, always inside Next month's
  # span. An "in N days" event lands in This month or Next month depending on what day of the month
  # the suite happens to run on, and both show the date, but only one of them could be named here.
  Scenario: A row further out than tomorrow leads with the day and date
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Blood Test" at "10:00" on "next month" in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I navigate to the next month
    And I open the event "Blood Test" for editing
    And I give the event a reminder 2 hours before
    And I save the event
    And I show the reminders view
    Then the "Next month" section has a row for "Blood Test" leading with the day and date of "next month"

  Scenario: A shared event's row names each person rather than the shared calendar
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Family Movie Night" at "09:00" on "today" in "Work Calendar"
    And the user has the event "Family Movie Night" also in "Personal Calendar"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I open the event "Family Movie Night" for editing
    And I give the event a reminder 30 minutes before
    And I save the event
    And I show the reminders view
    Then the "This month" section has a row for "Family Movie Night" naming "Work Calendar" and "Personal Calendar"

  # The boundary this scenario guards is the month's. There used to be a week boundary to worry about
  # too; there is no week-aligned panel left, and tomorrow is now the only day that can be filed in
  # one panel while naming itself in words. On the last day of a month tomorrow IS next month, so
  # tomorrow's row is filed under "Next month" and must still read "Tomorrow" there.
  #
  # E2E cannot force today to BE a month's last day: nothing in this stack can move the WebApi's
  # TimeProvider, unlike the client-only kiosk day-rollover hook. What this proves on every run is
  # the rule that makes the month-end case work — a row's day comes from its own local date against
  # today, never from the panel holding it — and on the one run in thirty that lands on a month's
  # last day it takes the edge itself, with no change to the scenario. The deterministic edge is
  # pinned with a controlled DateOnly in
  # ReminderBucketingTests.File_OnTheLastDayOfAMonth_TomorrowLandsInNextMonthAndIsStillKept.
  #
  # Its reminder is set through the date-aware precondition step rather than the three-step modal
  # chain the "today" scenarios use, and that is not a tidying-up: the month grid stops as soon as a
  # row completes past the month's end, so on a month ending on a Saturday "tomorrow" is not rendered
  # at all and the click to open the event finds nothing. Once every seven months or so is worse than
  # never — it surfaces long after whatever change exposed it, with no obvious cause. The step
  # navigates to the date when the grid does not already show it, and is a no-op when it does. The
  # "next month" scenarios above reach the same safety by navigating explicitly; the ones seeded
  # today need nothing, because today is always on the grid.
  Scenario: Tomorrow's row reads Tomorrow whichever panel it is filed in
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Bin Collection" at "08:00" on "tomorrow" in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    And the event "Bin Collection" on "tomorrow" has been given a reminder 15 minutes before
    When I show the reminders view
    Then the row for "Bin Collection" leads with "Tomorrow · 08:00"

  # The kiosk's own day rollover, asserted where the family actually looks. It sits in this file
  # rather than in DayRollover.feature for two reasons. What it pins is how a row reads and where it
  # is filed, which is this file's whole subject; and that file has a Background whose login runs
  # before any scenario step, while a backdoor-seeded event has to exist BEFORE that login to reach
  # FamilyHQ at all — the same ordering this file has no Background for.
  #
  # It is also the rollover's strongest remaining assertion, which is why it is worth the seeding.
  # The Day view's own scenarios can no longer prove the date moved: tapping the Day View tab without
  # a date opens on today by design, so what it shows afterwards is a statement about the clock. The
  # Month and Agenda ones still prove it, but on views a kiosk is rarely left on. Here the row has to
  # RE-READ ITSELF: its label is an equality, so "Today · 08:00" cannot pass on a row still reading
  # "Tomorrow · 08:00" — the way a bare presence check under a second heading once could. The second
  # Then is the other half, and it is a real one at a month's end: the row has to change panel as
  # well as label on the day the rollover carries it from "Next month" into "This month", and a copy
  # left behind in the panel it came from would be a filing bug the label alone would pass.
  #
  # Nothing is tapped after the idle check, and nothing needs to be: the kiosk brings itself back to
  # the timeline, and that return is what re-anchors the view's "today"
  # (Index.EnterRemindersViewAsync) and so what refiles the row. The server's clock does not move and
  # its window returns the same event either way, so a row that did not move means the re-anchor did
  # not happen. Leaving the timeline first is what makes the return fire at all — an idle check on a
  # kiosk already at home has nothing to do, which is exactly why this cannot be asserted without
  # going somewhere else first.
  @day-rollover
  Scenario: An idle kiosk re-reads tomorrow's row as today's once the day rolls over
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Bin Collection" at "08:00" on "tomorrow" in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    And the event "Bin Collection" on "tomorrow" has been given a reminder 15 minutes before
    When I show the reminders view
    Then the row for "Bin Collection" leads with "Tomorrow · 08:00"
    When I switch to the Month View tab
    And the date rolls over by 1 day
    And the kiosk's idle timer reads 16 minutes
    And the idle check runs
    Then the row for "Bin Collection" leads with "Today · 08:00"
    And the row for "Bin Collection" appears only in the "This month" section

  # "Next month" is the panel that can genuinely be empty while the view has something to show: an
  # event seeded today is in "This month" by definition, so nothing files into the panel beside it.
  Scenario: A panel with nothing in it collapses to one line
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Checkup" at "09:00" on "today" in "Appointments"
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
    And the user has a timed event "Checkup" at "09:00" on "today" in "Appointments"
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
    And the user has a timed event "Checkup" at "09:00" on "today" in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I open the event "Checkup" for editing
    And I give the event a reminder 30 minutes before
    And I save the event
    And I show the reminders view
    Then the "This month" section has a row for "Checkup"
    And the inherited-reminders footnote is shown

  # A tap drills into the event's day, as every other view's rows do. The careful case is an event
  # OUTSIDE the month the dashboard has loaded, which is what exercises the month fetch rather than
  # just the view switch — so this navigates BACK to the current month after editing. Without that
  # step the edit itself has already loaded next month and the tap never has anything to fetch,
  # which is how the scenario this replaces came to claim a case it was not actually taking.
  #
  # The Then asserts the event's own TILE, not that a day view appeared: DayView renders only the
  # events filed under the day it is showing, so a tile for this event can only be there if the
  # right day opened and its month was loaded.
  Scenario: Tapping a row opens the day view on that event's own day
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Flu Clinic" at "10:00" on "next month" in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I navigate to the next month
    And I open the event "Flu Clinic" for editing
    And I give the event a reminder 2 hours before
    And I save the event
    And I navigate back to the current month
    And I show the reminders view
    And I tap the reminders row for "Flu Clinic"
    Then the day view is showing "Flu Clinic"

  # The enter/leave path, which nothing else covers end to end: there is no bUnit in this repo, and
  # the only other scenarios that leave this tab leave it to go on asserting about the timeline or
  # the tab strip rather than about the view they land on. Entering is not a no-op — it re-anchors
  # the view's "today" and refetches the rows — so leaving immediately afterwards is worth one
  # scenario that says the destination still renders.
  #
  # It used to be aimed at an orphaned-timer race: the per-minute tick loop was started on entering
  # this tab and stopped on leaving it, and the enter re-checked the current view after its own fetch
  # so a switch mid-fetch could not leave a timer running on a tab nobody was on. The loop now runs
  # for the page's whole lifetime and is stopped only on disposal, because the tab strip's count has
  # to keep up while the family is on another view — so there is no start/stop to interleave with a
  # tab switch any more, and that race is gone rather than untested.
  Scenario: Leaving the Reminders view for another tab renders the destination cleanly
    Given I have a user like "RemindersViewUser"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I show the reminders view
    And I switch to the Month View tab
    Then the dashboard is showing the month view

  # The badge's whole requirement is that it is right from somewhere else, so this asserts it with
  # the Month view on screen and nothing of the timeline rendered at all. No other scenario would
  # catch it: every one above reads the rows, which are exactly what the family cannot see when they
  # need the badge.
  #
  # TWO events today, not one, and that is deliberate — a badge hardcoded to "1", or one rendering a
  # yes/no flag, passes a count of one. The second event also puts the plural wording under
  # assertion, which the singular form cannot reach. The count is of EVENTS: each of these has one
  # reminder of its own, so a badge counting reminder pings instead would still read 2 here, and the
  # unit tests are what pin that distinction (TodayCount reads each row's event start; a row carries
  # no ping instant at all any more).
  Scenario: The Reminders tab says how much falls today while the family is on another view
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Dentist Visit" at "09:00" on "today" in "Appointments"
    And the user has a timed event "Swim Lesson" at "11:00" on "today" in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    When I open the event "Dentist Visit" for editing
    And I give the event a reminder 2 hours before
    And I save the event
    And I open the event "Swim Lesson" for editing
    And I give the event a reminder 30 minutes before
    And I save the event
    Then the dashboard is showing the month view
    And the Reminders tab shows a bell and reads "2 reminders today"

  # The absence half, and it needs a timeline with something in it: an assertion that an element is
  # not there passes just as well against a page whose reminders fetch has not come back, so the row
  # is read off the timeline first to prove the fetch landed and returned an event. That event is
  # TOMORROW, so what the badge must not do is count it — which is also why the row's label is read
  # here rather than its panel, since tomorrow changes panel at a month's end and the point stands
  # either way.
  Scenario: The Reminders tab carries no count when nothing falls today
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Bin Collection" at "08:00" on "tomorrow" in "Appointments"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    And the event "Bin Collection" on "tomorrow" has been given a reminder 15 minutes before
    When I show the reminders view
    Then the row for "Bin Collection" leads with "Tomorrow · 08:00"
    When I switch to the Month View tab
    Then the Reminders tab shows no bell at all
