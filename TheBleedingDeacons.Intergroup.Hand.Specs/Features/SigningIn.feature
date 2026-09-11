@manual @ignore
Feature: Getting a handset onto the rota
  Enrolment, verified by hand. Both routes end in the same long-lived
  device token held in platform secure storage — the Android keystore, the
  Apple keychain, DPAPI on Windows — and none of it is reachable from a
  test host, because every piece of it is a MAUI or platform API.

  Scenario: SSO through the system browser returns a code, never the token
    Given a responder who is certified in Unity
    When they sign in with Google, Microsoft, Apple or Facebook
    Then the system browser is used rather than an embedded web view
    And Reach's callback returns a one-time code to hand://auth
    And Hand trades that code for a device token over TLS
    # The code rather than the token travels through the browser, because a
    # redirect lands in history and can be read by anything else registered
    # for the scheme (RFC 8252).

  Scenario: The password route, for a head that cannot claim a URI scheme
    Given a Windows build that is not packaged as MSIX
    When the responder signs in
    Then they are asked for an email and a password
    And nothing goes through the browser

  Scenario: Somebody who is not a certified telephone responder is refused
    Given a member who is a 12th-stepper but not a certified responder
    When they try to sign in
    Then Reach refuses the enrolment
    And the reason names the certification rather than the password
    # Stricter than the Reach website, which also admits 12th-steppers.

  Scenario: A lapsed certification stops the handset at its next call
    Given a responder whose certification has lapsed since they signed in
    When the handset next polls
    Then Reach refuses it
    And the handset clears its token and returns to sign-in carrying the reason
    # Nobody has to remember to revoke the device.

  Scenario: The payload key is issued once and never reissued
    When a handset enrols
    Then the response carries the device token and the payload key
    And both go straight into secure storage
    And a later session check returns neither

  Scenario: An unconfigured artifact cannot be pointed anywhere afterwards
    Given a build made without the HAND_BASE_URL variable
    When it is installed and opened
    Then it does not know which intergroup it belongs to
    And the Reach server address on Settings is read-only
    # Which is why CI writes appsettings.json before compiling, and why an
    # artifact built without it is only good for proving the head compiles.
