@webui @dashboard @home-view
Feature: The kiosk's home view
  As a family using a wall-mounted kiosk
  I want the dashboard to open on what we have asked to be reminded about, and to come back to it
  So that the screen on the wall is showing that whoever walks past, not wherever the last person left it

  The reminders timeline is both the first tab and the view a load lands on, and those are one
  decision rather than two: a wall display whose leftmost tab was not the one it opens on would read
  as a bug. Nobody walks up to a kiosk to navigate home, so coming back is the kiosk's own job after
  fifteen minutes with nothing touching it — the same fifteen minutes that pulls a stale date back to
  today, because it is one judgement about whether anybody is in the room.

  Landing has to do the work entering the tab does. A load that only picked the view would render the
  timeline with no rows in it, which is why the first scenario seeds a reminder and then asserts the
  ROW rather than the panel: an empty timeline is also what a view that never fetched looks like, so
  the panel alone could not tell the two apart.

  No Background. The seeded scenario needs its event in place before the login that triggers the
  first sync, and folding the login into a Background would fix that order the wrong way round — the
  same reason RemindersView.feature gives for having none.

  Scenario: The reminders timeline is the first of the dashboard's tabs
    Given I have a user like "RemindersViewUser"
    And I login as the user "RemindersViewUser"
    And I view the dashboard
    Then the dashboard's view tabs read "Reminders, Month View, Agenda, Day View"

  Scenario: A page load opens on the reminders timeline with its rows already filed
    Given I have a user like "RemindersViewUser"
    And the user has a timed event "Dentist Visit" starting in 140 minutes in "Appointments"
    And I login as the user "RemindersViewUser"
    And the event "Dentist Visit" on "today" has been given a reminder 2 hours before
    When the kiosk loads the dashboard
    Then the reminders view is showing
    And the "Today" section has a row for "Dentist Visit"

  Scenario: A kiosk left on another view returns to the reminders timeline once nobody is at it
    Given I have a user like "RemindersViewUser"
    And I login as the user "RemindersViewUser"
    And I am on the Month view showing the current month
    When the kiosk's idle timer reads 16 minutes
    And the idle check runs
    Then the reminders view is showing

  # The gate that matters most: an edit left half-finished on the kiosk must not be carried off the
  # screen by the return. The modal is still open and unsaved when the idle check runs.
  Scenario: An open event modal keeps the kiosk where the family left it
    Given I have a user like "RemindersViewUser"
    And I login as the user "RemindersViewUser"
    And I am on the Month view showing the current month
    And the create-event modal is open
    When the kiosk's idle timer reads 16 minutes
    And the idle check runs
    Then the dashboard is showing the month view
