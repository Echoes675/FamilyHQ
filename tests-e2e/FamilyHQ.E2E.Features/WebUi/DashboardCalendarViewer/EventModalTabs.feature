Feature: Event Editor Tabs
  As a family member using the kiosk
  I want the event editor split into tabs
  So that the form stays short enough to use without scrolling, and nothing I need to know is hidden

  Background:
    Given I have a user like "TimedEventsUser"
    And the "Appointments" calendar is the active calendar
    And I login as the user "TimedEventsUser"

  Scenario: The event editor opens on the Details tab
    When I begin creating an event in "Appointments"
    Then the event editor is showing the "Details" tab

  Scenario: An unfinished repeat choice is flagged while the Details tab is showing
    When I begin creating an event in "Appointments"
    And I turn on repeat for the event
    And I show the "Details" tab of the event editor
    Then the Repeat tab is marked as incomplete
    And the event editor explains that the repeat settings must be finished before saving

  Scenario: An unfinished repeat choice survives a visit to the Details tab
    When I begin creating an event in "Appointments"
    And I turn on repeat for the event
    And I show the "Details" tab of the event editor
    And I show the "Repeat" tab of the event editor
    Then a repeat frequency must be chosen and the event cannot yet be saved

  Scenario: A chosen repeat frequency is summarised on the Repeat tab
    When I begin creating an event in "Appointments"
    And I turn on repeat for the event
    And I choose the weekly repeat frequency
    And I show the "Details" tab of the event editor
    Then the Repeat tab is labelled "Weekly"
    And the event can be saved

  Scenario: A validation error is visible from the Repeat tab
    When I begin creating an event in "Appointments"
    And I show the "Repeat" tab of the event editor
    And I attempt to save the event
    Then the event modal shows the error "Title is required."
    And the event editor is showing the "Repeat" tab

  Scenario: A missing calendar is flagged on the Details tab while the Repeat tab is showing
    When I open the create-event modal
    And I show the "Repeat" tab of the event editor
    Then the Details tab is marked as incomplete
    When I select the "Appointments" calendar for the event
    Then the Details tab is not marked as incomplete

  Scenario: The event editor does not move when the tab changes
    When I begin creating an event in "Appointments"
    Then the event editor stays in the same place as the tab changes
