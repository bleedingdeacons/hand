Feature: The level is how loud the handset is
  As a responder with a phone on the table
  I want to tell a callback from a reminder across the room
  So that I know whether it can wait until I have finished what I am doing

  Every alert arrives at one of three levels, which decides both the noise
  it makes and the colour of its card. Only red sounds the alarm, and only
  red keeps it going: a yellow reminder arriving mid-call must not leave
  the siren running after the callback it was actually ringing for has
  been answered.

  Background:
    Given this handset is signed in to Reach

  Scenario Outline: Only red sounds the alarm
    When a <level> alert 1 arrives by push
    Then alert 1 is on screen
    And alert 1 was shown in the notification tray
    And the alarm <alarm>

    Examples:
      | level  | alarm         |
      | red    | is sounding   |
      | yellow | never sounded |
      | blue   | never sounded |

  Scenario: An outstanding yellow alert does not keep the alarm going
    When a red alert 1 arrives by push
    And a yellow alert 2 arrives by push
    And I press the button on alert 1
    Then the alarm is silent

  Scenario: The alarm keeps going until the last red alert is answered
    When a red alert 1 arrives by push
    And a red alert 2 arrives by push
    And I press the button on alert 1
    Then the alarm is sounding
    When I press the button on alert 2
    Then the alarm is silent

  Rule: An alert with no level falls back to its priority

    A Reach that predates the level sends only normal or urgent. Reading
    its absent level as "unrecognised, call it yellow" would demote every
    urgent alert that server raises — on the one route where the handset
    is newer than the server, which is the ordinary way round for an app
    that updates itself.

    Scenario Outline: An older server's priority decides the level
      Given an alert with no level and priority "<priority>"
      Then its level reads as <derived>

      Examples:
        | priority | derived |
        | urgent   | red     |
        | normal   | yellow  |

    Scenario: A level this build has never heard of reads as the middle rung
      Given an alert at level "puce"
      Then its level reads as yellow

  Scenario Outline: The card's colour is the level
    Given an alert at level "<level>"
    Then its card is <colour> on white

    Examples:
      | level  | colour  |
      | red    | #B3261E |
      | yellow | #F9A825 |
      | blue   | #1565C0 |

  Rule: A secure lock screen is offered words that give nothing away

    Reach already refuses to put personal data in an alert, and this does
    not rely on that. The one field a human writes freehand is validated
    for length and markup but not for meaning, and a responder's phone
    lies face-up in a room with other people in it. Urgency is the only
    thing kept, because urgency is not a secret and it is what says
    whether the phone can wait.

    Scenario Outline: The lock screen says only whether it is urgent
      Given an alert at level "<level>"
      Then a redacted lock screen would read "<title>" / "Unlock to read"

      Examples:
        | level  | title                 |
        | red    | Urgent helpline alert |
        | yellow | Helpline alert        |
        | blue   | Helpline alert        |

    Scenario: The lock screen carries nothing from the alert itself
      Given an alert at level "red"
      Then the redacted lock screen mentions neither its subject nor its reference
