@kiosk
Feature: All-day events and Google's exclusive end date

  Google describes an all-day event with a pair of calendar dates rather than a pair of instants, and its
  end date is the day AFTER the last day the event covers. That one convention is this whole feature.
  Write the last day back as the end date and the event is over before it begins; read the end date as
  inclusive and every all-day event on the kiosk runs a day longer than the family put it in for.

  It is also the one shape that carries no time zone at all, which is the point of checking it against
  real Google rather than a double: a date-anchored event must not acquire a zone on the way out.

  Background:
    Given the preprod kiosk is open and signed in

  @AD1
  Scenario: A one-day all-day event created on the kiosk is dated the way Google dates one
    When I create a one-day all-day event on the kiosk
    Then Google holds it as a start date with the end date on the following day
    And Google holds no time and no time zone for it

  @AD2
  Scenario: A one-day all-day event created in Google covers that day alone on the kiosk
    Given a one-day all-day event is created in Google on a member's calendar
    And the kiosk has received that event
    Then preprod serves it as an all-day event ending at the next day's boundary
    And the kiosk shows it on that day and not on the next
