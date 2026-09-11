@manual @ignore
Feature: Permissions, and the fingerprint in front of the app
  On-device permission behaviour, verified on a device or an emulator.
  Every one of these decides whether an alert is seen at all, and none of
  them is reachable from a test host.

  Scenario: First run asks for what it needs, and says why
    Given the app is opened for the first time on Android 13 or later
    When it needs to post notifications
    Then it asks for POST_NOTIFICATIONS
    And it explains that without it an alert arrives and nothing appears

  Scenario: A handset whose notifications were switched off says so on Settings
    Given notifications are turned off for Hand in the phone's own settings
    When Settings is opened
    Then push reads as turned off, in red
    And the words name the phone's own settings as the place to fix it

  Scenario: An iPhone without the critical-alerts entitlement uses time-sensitive
    Given the provisioning profile does not carry the critical-alerts entitlement
    When an urgent alert arrives
    Then it is delivered at the time-sensitive level
    And it gets through a Focus mode
    # Turning the switch on in Reach's settings without the entitlement in
    # the profile makes Apple REJECT the notification rather than downgrade
    # it — silencing the very alerts it is meant to make louder.

  Rule: The app lock never stands between a responder and an alert

    It is not authentication. Reach decided who this handset belongs to
    when it was signed in, and the device token is what proves it on every
    request. The lock answers a smaller and more domestic question:
    whether the person holding the phone right now is the responder it was
    handed to.

    Scenario: A cold start asks for a fingerprint
      Given the app lock is on and nothing is outstanding
      When the app is started cold
      Then it asks for a fingerprint before showing anything

    Scenario: An outstanding alert skips the lock outright
      Given the app lock is on
      And a red alert is outstanding
      When the app is opened from the notification
      Then no fingerprint is asked for
      # A responder woken at four in the morning presses acknowledge, not a
      # fingerprint sensor.

    Scenario: A sensor that cannot answer opens the app
      Given the app lock is on
      And the fingerprint enrolments were lost when the screen lock changed
      When the app is started cold
      Then the app opens
      # Failing closed would take a certified responder off the rota over a
      # hardware fault nobody can fix at midnight, which is worse than the
      # thing the lock exists to prevent.
