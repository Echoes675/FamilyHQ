@kiosk
Feature: The shape of a series the kiosk writes to Google

  A recurrence rule is a contract with Google: FamilyHQ states the rule, Google decides what the
  occurrences are, and the Google Calendar app on the family's phones shows Google's answer. So the shape
  of the rule that arrives is what matters — an interval dropped, a weekday re-derived from the start date,
  a yearly rule that acquires a time — and each of those is invisible in FamilyHQ's own account of itself.

  Every series here is bounded, and the occurrence set is always compared against Google's own expansion
  rather than against a calculation of ours.

  Background:
    Given the preprod kiosk is open and signed in

  @RK2
  Scenario: A fortnightly series carries both its interval and its chosen weekday to Google
    When I create a bounded fortnightly series on a chosen weekday on the kiosk
    Then Google's rule for it repeats every 2 weeks on that weekday
    And the occurrences preprod serves match Google's expansion

  @RK3
  Scenario: A yearly all-day series falls on the same date each year
    When I create a bounded yearly all-day series on the kiosk
    Then Google's rule for it repeats yearly and its occurrences are dates rather than times
    And the occurrences preprod serves match Google's expansion

  @RK4
  Scenario: A two-member series is written once, to the shared calendar, and belongs to both members
    When I create a bounded weekly series on the kiosk for two members
    Then Google holds one series master on the shared calendar naming both members
    And preprod serves every occurrence for both members

  @RK12
  Scenario: Switching the repeat off collapses the series to a single event
    Given the kiosk has created a bounded weekly series
    When I switch the repeat off on the kiosk
    Then Google holds the event with no recurrence rule
    And preprod serves exactly one occurrence of it
