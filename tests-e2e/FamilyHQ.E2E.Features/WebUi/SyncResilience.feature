Feature: Sync resilience and diagnostics
  As a family member
  I want the app to surface Google-side failures and keep syncing what it can
  So that one broken event or revoked token does not leave me stranded

  Background:
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And I login as the user "TestFamilyMember"

  Scenario: Reauth banner appears when Google revokes the refresh token
    Given Google rejects refresh tokens with "invalid_grant"
    When I trigger a manual sync
    Then I see the reauth banner on the dashboard
    And the reauth banner shows a reconnect link

  Scenario: Reauth banner shows the Google-supplied reason when Calendar API returns 403
    Given the Google Calendar API rejects requests with "403"
    When I trigger a manual sync
    Then I see the reauth banner on the dashboard
    And the reauth banner shows the Google-supplied reason

  Scenario: Diagnostics page shows needs-reauth status with reconnect button
    Given Google rejects refresh tokens with "invalid_grant"
    And I trigger a manual sync
    When I view the diagnostics page
    Then I see the needs-reauth status badge
    And I see a reconnect button

  Scenario: Diagnostics page lists a sync event failure when one event in a sync throws
    Given the user has an all-day event "Soccer practice" tomorrow
    And a sync event failure has been recorded
    When I view the diagnostics page
    Then I see the failure in the recent sync failures table
    And my other events still appear on the dashboard
