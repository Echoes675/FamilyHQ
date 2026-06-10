@webui @dashboard @day-rollover
Feature: Kiosk auto-advances to the current day after idle
  As a family using a wall-mounted kiosk
  The dashboard should roll forward to the new day when left idle overnight
  Without interrupting anyone who is actively making changes

  Background:
    Given I have a user like "KioskRolloverUser"
    And I login as the user "KioskRolloverUser"

  Scenario: Day view advances to the new day after the kiosk is idle past midnight
    Given I am on the Day view showing today
    When the kiosk has been idle for 16 minutes
    And the date rolls over by 1 day
    And the idle check runs
    Then the Day view shows the new current day

  Scenario: Month view advances to the new month after rollover while idle
    Given I am on the Month view showing the current month
    When the kiosk has been idle for 16 minutes
    And the date rolls over by 1 month
    And the idle check runs
    Then the Month view shows the new current month

  Scenario: Agenda view advances to the new month after rollover while idle
    Given I am on the Agenda view showing the current month
    When the kiosk has been idle for 16 minutes
    And the date rolls over by 1 month
    And the idle check runs
    Then the Agenda view shows the new current month

  Scenario: A view navigated away from snaps back to today after idle
    Given I am on the Day view navigated 5 days into the future
    When the kiosk has been idle for 16 minutes
    And the idle check runs
    Then the Day view shows today

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
    Then the Day view shows the new current day

  Scenario: Recent interaction defers the rollover
    Given I am on the Day view showing today
    When the date rolls over by 1 day
    And the user interacted 2 minutes ago
    And the idle check runs
    Then the Day view still shows the previous day
