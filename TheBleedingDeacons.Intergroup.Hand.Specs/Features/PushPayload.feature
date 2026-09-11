Feature: Opening a pushed alert
  As an intergroup whose alerts cross Google's servers
  I want the push to carry ciphertext and nothing else
  So that what is on the wire says nothing about who rang

  Reach encrypts the whole data payload to a secret this handset was given
  once, at enrolment, and sends the result as a single ciphertext field.
  Not the readable half of it — all of it, including the id, the kind and
  whatever extras the raising plugin attached. An alert is supposed to
  carry no personal data, but that is a convention the server enforces by
  capping and stripping rather than by reading meaning, and encrypting the
  lot removes the question instead of policing it.

  Background:
    Given this handset was given a payload key at enrolment

  Scenario: A sealed push opens into an alert
    Given Reach has sealed an alert to this handset
    When the push is opened
    Then it opens into alert 12
    And the alert's subject is "Callback wanted CR-000123"
    And the alert's reference is "CR-000123"
    And its level reads as red

  Rule: Null is the answer to every fault, and the caller says so to Reach

    A push with no ciphertext did not come from a server that knows this
    handset's key. One that will not open means the key here is wrong. One
    that opens without a usable id could never be acknowledged and would
    ring until the battery went. None of the three is shown to a
    responder, and the poll delivers the alert by the slower route
    regardless.

    Scenario: A push carrying no ciphertext at all
      Given a push with no ciphertext
      When the push is opened
      Then nothing was opened

    Scenario: A push sealed to a key this handset does not hold
      Given Reach has sealed an alert to somebody else's handset
      When the push is opened
      Then nothing was opened

    Scenario: A payload altered on the way
      Given Reach has sealed an alert to this handset
      And the sealed payload is tampered with
      When the push is opened
      Then nothing was opened

    Scenario: A payload with no usable alert id
      Given Reach has sealed an alert with no alert id
      When the push is opened
      Then nothing was opened

  Scenario: Whatever the raising plugin attached comes through
    Given Reach has sealed an alert carrying "shift_ref" of "SHIFT-2026-08-15-N"
    When the push is opened
    Then the alert's payload has "shift_ref" of "SHIFT-2026-08-15-N"

  Scenario: The server's own fields do not reappear as payload entries
    Given Reach has sealed an alert to this handset
    When the push is opened
    Then the alert's payload does not carry "title"
    And the alert's payload does not carry "ciphertext"

  Scenario: An older server that sends no response still raises a job
    Given Reach has sealed an alert with no response field
    When the push is opened
    Then the alert's button says "Acknowledge"

  Scenario Outline: Whether there are contact details to ask for
    Given Reach has sealed an alert whose has_contact is "<flag>"
    When the push is opened
    Then the alert <offers> contact details

    Examples:
      | flag | offers    |
      | 1    | offers    |
      | true | offers    |
      |      | offers no |
      | 0    | offers no |

  Rule: A handset that cannot read its alerts tells Reach, once per run

    Reach can see a device row with no key. It cannot see a handset whose
    own copy has gone, so the handset has to say — and until it does, the
    only symptom is a responder who does not answer. Once per run rather
    than once per push: the fault is a property of the handset, the server
    records the same thing every time, and a handset that can read nothing
    would otherwise report on every message it receives.

    Scenario: It says so
      Given this handset is signed in to Reach
      When the handset reports that it cannot read its alerts
      Then Reach was told this handset cannot read its alerts once

    Scenario: And does not go on saying it
      Given this handset is signed in to Reach
      When the handset reports that it cannot read its alerts
      And the handset reports that it cannot read its alerts
      Then Reach was told this handset cannot read its alerts once

    Scenario: A handset with no token has nothing to say it with
      Given this handset has no device token
      When the handset reports that it cannot read its alerts
      Then Reach was never told this handset cannot read its alerts
