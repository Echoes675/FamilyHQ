@webui @dashboard @day-rollover
Feature: Kiosk auto-advances to the current day after idle
  As a family using a wall-mounted kiosk
  The dashboard should roll forward to the new day when left idle overnight
  Without interrupting anyone who is actively making changes

  Fifteen idle minutes do two things, and the family asked for both: the displayed date is pulled
  back to today, and the kiosk goes home to the reminders timeline. This feature is about the first.
  The second is covered by KioskHomeView.feature and is the reason the scenarios below look the way
  they do — after an idle check the view under test is no longer the one on screen, so each scenario
  has to go back to it before it can read anything off it.

  That return is navigation, not a workaround for a broken assertion. For the Month and Agenda views
  it costs the scenarios nothing: their tabs preserve the displayed month, so the label read after
  re-selecting the view is still the month the snap put there.

  The Day view is the exception, and its scenarios say so out loud. Tapping the Day View tab without
  a date deliberately opens on today (Index.SwitchToView), so the date it shows afterwards is a
  statement about the clock rather than about the snap, and the honest assertion for an idle Day view
  is now the one that can still fail: that the kiosk went home. The second half is kept because a Day
  view that will not open on the new day is still worth catching — it is a weaker claim than it was,
  not an empty one.

  The rollover's strongest assertion is therefore not in this file. It is in RemindersView.feature,
  where a seeded row has to MOVE from the Tomorrow section to the Today section once the day turns —
  on the home view, which is the one the kiosk is actually left showing. Look there as well as here
  when a rollover regresses; its event has to be seeded before the login, which is why it cannot live
  behind this file's Background.

  Background:
    Given I have a user like "KioskRolloverUser"
    And I login as the user "KioskRolloverUser"

  Scenario: An idle kiosk on the Day view goes home and the day it reopens on has rolled over
    Given I am on the Day view showing today
    When the kiosk has been idle for 16 minutes
    And the date rolls over by 1 day
    And the idle check runs
    Then the reminders view is showing
    When I switch to the Day View tab
    Then the Day view shows the new current day

  Scenario: Month view advances to the new month after rollover while idle
    Given I am on the Month view showing the current month
    When the kiosk has been idle for 16 minutes
    And the date rolls over by 1 month
    And the idle check runs
    And I switch to the Month View tab
    Then the Month view shows the new current month

  Scenario: Agenda view advances to the new month after rollover while idle
    Given I am on the Agenda view showing the current month
    When the kiosk has been idle for 16 minutes
    And the date rolls over by 1 month
    And the idle check runs
    And I switch to the Agenda View tab
    Then the Agenda view shows the new current month

  Scenario: A kiosk left on a future day goes home and reopens the Day view on today
    Given I am on the Day view navigated 5 days into the future
    When the kiosk has been idle for 16 minutes
    And the idle check runs
    Then the reminders view is showing
    When I switch to the Day View tab
    Then the Day view shows today

  # The first half is the gate that matters most and is unchanged: with a modal open neither idle
  # rule fires, so the kiosk neither moves the date nor leaves the view, and an unsaved edit is still
  # on screen to go back to. The second half shows both resuming once the modal is gone.
  Scenario: An open event modal defers the rollover until it is closed
    Given I am on the Day view showing today
    And the create-event modal is open
    When the kiosk has been idle for 16 minutes
    And the date rolls over by 1 day
    And the idle check runs
    Then the Day view still shows the previous day
    When I cancel the event modal
    And the kiosk has been idle for 16 minutes
    And the idle check runs
    Then the reminders view is showing
    When I switch to the Day View tab
    Then the Day view shows the new current day

  # Two minutes is below the threshold both rules share, so nothing fires and the Day view is still
  # the view on screen — no re-selection, and the assertion reads exactly as it always did.
  Scenario: Recent interaction defers the rollover
    Given I am on the Day view showing today
    When the date rolls over by 1 day
    And the user interacted 2 minutes ago
    And the idle check runs
    Then the Day view still shows the previous day
