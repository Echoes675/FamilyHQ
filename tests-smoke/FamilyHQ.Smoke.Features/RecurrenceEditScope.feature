@kiosk
Feature: Editing a series from the kiosk, at each scope

  "This event", "this and following" and "all events" are not three settings of one operation. They reach
  Google by three entirely different routes: one turns a single occurrence into an exception recorded
  against the slot it came from, one ends the original series just before the split and creates a
  replacement carrying the rest, and one changes the master. Getting the wrong one is how a family loses a
  fortnight of history to a change they meant for one afternoon.

  Background:
    Given the preprod kiosk is open and signed in
    And the kiosk has created a bounded weekly series

  @RK5
  Scenario: Changing one occurrence leaves the master and the other occurrences alone
    When I rename the middle occurrence on the kiosk for this event only
    Then Google records that occurrence as an exception against its original slot
    And the occurrences preprod serves match Google's expansion, title by title

  @RK6
  Scenario: Changing this and following ends the original series and starts a replacement
    When I rename the middle occurrence on the kiosk for this and following events
    Then Google holds the original series ending before the split and a replacement from it
    And the occurrences preprod serves match Google's expansion, title by title

  @RK7
  Scenario: Changing all events over an occurrence that was already singled out
    Given the first occurrence has already been renamed on the kiosk for this event only
    When I rename the middle occurrence on the kiosk for all events
    Then Google still holds that occurrence as one exception against its original slot
    And the series keeps its original dates
    And the occurrences preprod serves match Google's expansion, title by title
