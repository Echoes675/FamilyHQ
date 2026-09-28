@kiosk
Feature: A phone-made series edited on the kiosk keeps everything nobody asked to change

  Most events are created, changed and deleted in the Google Calendar app on a phone. The kiosk is a peer
  client, and it may validly change or remove anything on the family's calendars — but a request that
  changes one field and silently rewrites another alongside it has broken the rule that matters, however
  correct the requested change was.

  A series is where that is most expensive. A series is anchored to a time zone, and the zone decides where
  every later occurrence falls across a daylight-saving change. Replacing the zone Google supplied with the
  family's configured one moves every future occurrence — and the damage shows up months later, on the
  phone, not here.

  Background:
    Given the preprod kiosk is open and signed in

  @RK11
  Scenario: Renaming a series made in another time zone leaves its zone, its rule and its other fields alone
    Given a bounded weekly series created in Google in another time zone has reached the kiosk
    When I rename the middle occurrence on the kiosk for all events
    Then Google holds the new title on that series
    And Google still holds that series' own time zone, its rule and every other field
