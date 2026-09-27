@kiosk
Feature: A series that spans the next daylight-saving change

  A recurring event is anchored to a zone, not to a list of instants. The family put it in at ten in the
  morning, and it stays at ten in the morning after the clocks change — which means the instant it happens
  at moves by an hour, and every implementation that steps a series forward in fixed units gets one side of
  the change wrong.

  Two expansions that agree all year and disagree on one weekend are not a difference of opinion; they are a
  latent bug with a date on it. So the series here is placed around whichever change comes next, worked out
  when the scenario runs, and the scenario fails rather than quietly stops testing anything if the
  occurrences do not in fact end up either side of it.

  Background:
    Given the preprod kiosk is open and signed in

  @RD1
  Scenario: A series created on the kiosk keeps its wall-clock time across the change
    When I create a bounded daily series spanning the next daylight-saving change on the kiosk
    Then Google's occurrences for it span the change at an unchanged wall-clock time
    And the occurrences preprod serves match Google's expansion

  @RD2
  Scenario: A series created in Google keeps its wall-clock time across the change
    When a bounded daily series spanning the next daylight-saving change is created in Google
    And the kiosk has received that event
    Then Google's occurrences for it span the change at an unchanged wall-clock time
    And the occurrences preprod serves match Google's expansion
