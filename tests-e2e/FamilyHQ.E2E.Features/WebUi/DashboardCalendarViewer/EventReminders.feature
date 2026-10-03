@EventReminders
Feature: Event Reminders
  As a family member using the kiosk
  I want to set an event's reminders here as I would in the Google Calendar app
  So that we are told about what matters, and nothing anyone set on a phone is quietly rewritten

  Google is the system of record, and three reminder states have to stay distinguishable: following
  the calendar's usual reminders, carrying reminders of its own, and carrying none. Collapsing any
  two of them writes the wrong thing back. The scenarios that assert what a save SENT are the ones
  that matter most: an edit that touches nothing else must say nothing about reminders at all, or an
  ordinary title change made here would rewrite a set made on somebody's phone.

  Background:
    Given I have a user like "TimedEventsUser"
    And the "Appointments" calendar is the active calendar
    And the active calendar's usual reminders in Google are 45 minutes
    And I login as the user "TimedEventsUser"
    And I view the dashboard

  Scenario: A reminder added on the kiosk is still there after a reload
    When I create the event "Dentist Visit" in "Appointments" with a reminder 2 hours before
    And I view the dashboard
    And I open the event "Dentist Visit" for editing
    Then the event has reminders 45 minutes and 2 hours before

  Scenario Outline: An event's reminders move between all three states in either direction
    When I create the event "<title>" in "Appointments" with <from>
    And I change the event "<title>" to <to>
    And I open the event "<title>" for editing
    Then the Reminders tab is labelled "<badge>"

    Examples:
      | title          | from                          | to                            | badge   |
      | Book Club      | the calendar's usual reminders | reminders of its own          | 1       |
      | Choir Practice | the calendar's usual reminders | no reminders                  | none    |
      | Dog Groomer    | reminders of its own           | the calendar's usual reminders | default |
      | Eye Test       | reminders of its own           | no reminders                  | none    |
      | Flu Jab        | no reminders                   | the calendar's usual reminders | default |
      | Guitar Lesson  | no reminders                   | reminders of its own          | 1       |

  Scenario: Renaming an event says nothing about its reminders
    Given the event "Sports Day" in "Appointments" has a reminder 2 hours before
    When I rename the event "Sports Day" to "Sports Afternoon"
    Then the event was saved without mentioning reminders
    And the event "Sports Afternoon" still has a reminder 2 hours before

  Scenario: A reminder added and taken away again says nothing about the event's reminders
    Given the event "Book Fair" in "Appointments" has a reminder 2 hours before
    When I open the event "Book Fair" for editing
    And I give the event a reminder 30 minutes before and take it away again
    And I save the event
    Then the event was saved without mentioning reminders

  # The defect the wording change above was not enough to fix: a family member who takes an event off
  # its calendar's reminders, configures one on the form and saves without pressing Add gets an event
  # with no reminders, and nothing tells them. The save now commits what the form describes, because
  # the alternative is silence they did not ask for. Exactly one reminder, and it is the configured
  # one — the calendar's usual reminder they took away must not come back with it.
  Scenario: A reminder configured on the form but never added is saved with the event
    Given the event "Allergy Jab" in "Appointments" follows the calendar's usual reminders
    When I take the event "Allergy Jab" off its calendar's reminders without adding one
    And I configure a reminder 2 hours before without adding it
    And I save the event
    Then the event "Allergy Jab" has one reminder 2 hours before when it is opened again

  # The sequence a family reported as a reminder that had been set and then did not arrive. Nothing
  # had been set: the Add form describes a reminder from the moment the tab opens, and that
  # description used to sit on a line of its own, in the same voice as the note above it saying the
  # event had none. Saving none was what the family asked for by turning the toggle off, so the save
  # is correct and stays correct — the last step pins that down. What had to change is that the
  # screen stops presenting the form's own starting value as something the event already carries.
  #
  # It is also the guard on the scenario above, and the reason that fix is a commit rather than an
  # unconditional one: the form here is never touched, so the save commits nothing and the event
  # keeps the no-reminders state the family asked for. If committing ever stopped depending on the
  # form having been touched, this is the scenario that would fail.
  Scenario: An event taken off its calendar's reminders without adding one is left with none, and says so
    Given the event "Parents Evening" in "Appointments" follows the calendar's usual reminders
    When I take the event "Parents Evening" off its calendar's reminders without adding one
    Then the Reminders tab states the event has no reminders
    And the Add control offers "30 minutes before" by "Notification" rather than stating it
    And the event "Parents Evening" still has no reminders after it is saved and opened again

  # The twin of the preprod scenario that asks the same question of real Google. What is provable here
  # is that the SAVE carries the other fields back unchanged; what is not is whether fields FamilyHQ
  # never models survive, because the Simulator does not store them to begin with. That half stays in
  # the preprod suite, where Google is the oracle.
  Scenario: A reminder change on its own says nothing else about the event
    Given the event "Village Fete" in "Appointments" has a reminder 2 hours before and the note "Bring the raffle tickets"
    When I change the event "Village Fete" to the calendar's usual reminders
    Then the event was saved carrying everything else exactly as it opened

  Scenario: Deleting an event says nothing about its reminders
    Given the event "Cancelled Outing" in "Appointments" has a reminder 2 hours before
    When I delete the event "Cancelled Outing"
    Then the event was deleted without mentioning reminders
    And I do not see the event "Cancelled Outing" displayed on the calendar

  Scenario: Switching an event to all day starts its reminders again, and says so
    Given the event "School Fair" in "Appointments" has a reminder 2 hours before
    When I open the event "School Fair" for editing
    And I switch the event to all day
    Then the Reminders tab says the reminders were started again
    And the Reminders tab is labelled "default"

  Scenario: Saving an event switched to all day asks for the calendar's usual reminders
    Given the event "Summer Fete" in "Appointments" has a reminder 2 hours before
    When I open the event "Summer Fete" for editing
    And I switch the event to all day
    And I save the event
    Then the event was saved asking for the calendar's usual reminders

  Scenario: Saving an inheriting event switched to all day says nothing about reminders
    Given the event "Fun Run" in "Appointments" follows the calendar's usual reminders
    When I open the event "Fun Run" for editing
    And I switch the event to all day
    And I save the event
    Then the event was saved without mentioning reminders

  Scenario: An all-day event's reminder cannot be set on the day of the event itself
    Given the all-day event "Bank Holiday" exists in "Appointments"
    When I open the event "Bank Holiday" for editing
    And I give the event reminders of its own
    And I ask for a reminder 0 days before
    Then the reminder holds at 1 day before and will not go lower

  Scenario: A reminder change on a repeating event warns that every occurrence's is replaced
    When I create a weekly recurring event "Swim Club" in "Appointments"
    And I open the event "Swim Club" for editing
    And I give the event a reminder 2 hours before
    And I attempt to save the event
    Then I am warned that the change replaces the reminders on every occurrence
