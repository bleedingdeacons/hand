Feature: Choosing who to message
  As a responder raising a message from the handset
  I want to pick a person or a committee by name
  So that I never have to learn anybody's contact details to reach them

  The directory carries a name, a home group and an id, and no address at
  all: a recipient is chosen by id and resolved on the server, which is
  what makes a directory sitting on every handset acceptable in the first
  place. Phone numbers are the one exception, and they are not in the row
  when it arrives — Reach writes an audit entry for each one and counts it
  against an hourly cap, so they are fetched only when a responder asks.

  Scenario: The row is a name and a home group, because a rota has more than one Dave
    Given a member "Dave S" of "Thursday Nighters"
    Then the picker shows "Dave S — Thursday Nighters"

  Scenario: A member Unity holds no group for is just a name
    Given a member "Dave S" of nothing
    Then the picker shows "Dave S"

  Scenario: A member with no handset is listed, and says why they cannot be picked
    Given a member "Dave S" of "Thursday Nighters"
    And nobody has enrolled a handset for them
    Then the row reads "No handset enrolled"

  Scenario: The wire carries no way to hold an address
    Then a directory row has no field an email address could go in

  Rule: The number the member asked to be rung on leads

    Reach keeps a member's last saved choice even for a landline since
    deleted, so the preference is read together with the numbers rather
    than trusted on its own.

    Scenario: A mobile and a landline, with the mobile preferred
      Given a member with mobile "07700 900123" and landline "0117 496 0123"
      And they asked to be rung on their "Mobile"
      Then the first line reads "07700 900123 (preferred)"
      And the second line reads "Landline 0117 496 0123"

    Scenario: The same member preferring the landline
      Given a member with mobile "07700 900123" and landline "0117 496 0123"
      And they asked to be rung on their "Landline"
      Then the first line reads "Landline 0117 496 0123 (preferred)"
      And the second line reads "07700 900123"

    Scenario: A preference for a landline that is no longer on file is ignored
      Given a member with mobile "07700 900123" and no landline
      And they asked to be rung on their "Landline"
      Then the first line reads "07700 900123"

    Scenario: One number has nothing to be preferred over, so it is not tagged
      Given a member with mobile "07700 900123" and no landline
      Then the first line reads "07700 900123"

  Scenario: A member with nothing on file says so rather than offering to ask again
    Given a member with no numbers at all
    When their numbers have been asked for
    Then the row reads "No number on file"

  Scenario: Nothing is said about numbers before anybody has asked
    Given a member with no numbers at all
    Then the row reads ""

  Scenario Outline: A committee says how many handsets sending to it would reach
    Given a committee of <handsets> handsets
    Then it reads "<line>"
    And it <reachable> be sent to

    Examples:
      | handsets | line        | reachable |
      | 0        | 0 handsets  | cannot    |
      | 1        | 1 handset   | can       |
      | 12       | 12 handsets | can       |

  Scenario: The tree is indented rather than punctuated into the name
    Given a committee two deep
    Then its name carries no dashes
    And its row is indented by 32
