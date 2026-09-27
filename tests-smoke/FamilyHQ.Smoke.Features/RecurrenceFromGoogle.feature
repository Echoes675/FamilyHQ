@kiosk
Feature: Series made and unmade in Google

  These series are created the way the Google Calendar app creates them, and the kiosk then has to agree
  with Google about what the rule means. The two bounds Google offers are not interchangeable: a count and
  an end date stop a series in different places, and an end date is inclusive and stated in UTC, which is
  how a series quietly loses its last occurrence.

  Nothing here triggers a sync. Every change waits for the live push, because a scenario that asked for a
  sync would pass on an environment whose push path is completely dead.

  Background:
    Given the preprod kiosk is open and signed in

  @RG2
  Scenario: A series Google bounds by an end date stops exactly where Google stops it
    When a weekly series bounded by an end date is created in Google
    And the kiosk has received that event
    Then the occurrences preprod serves match Google's expansion
    And the last occurrence preprod serves is the last one Google gives

  @RG3
  Scenario: A yearly all-day series created in Google lands on the same date each year
    When a yearly all-day series is created in Google
    And the kiosk has received that event
    Then Google's expansion of it falls on the same date in consecutive years
    And the occurrences preprod serves match Google's expansion

  @RG8
  Scenario: Changing the members named on a Google series changes who every occurrence belongs to
    Given a bounded weekly series naming two members exists in Google's shared calendar
    When the series' members are changed in Google to a different pair
    Then preprod serves every occurrence for the newly named members and no others

  @RG9
  Scenario: A series deleted in Google disappears from the kiosk
    Given a bounded weekly series created in Google has reached the kiosk
    When that series is deleted in Google
    Then preprod serves nothing for this scenario
