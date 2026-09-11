Feature: Whether this handset can actually be pushed to
  As a responder about to put the phone down for the night
  I want to be told what push is doing on this handset
  So that "it never rang" is something I find out before the shift

  Two separate things have to be true before Reach can ring a closed
  handset, and they fail for completely different reasons: the platform
  has to carry push at all, and the phone's owner has to have left
  notifications switched on for Hand. A single yes-or-no collapses those
  into one word and says nothing about which of them to go and fix.

  Scenario Outline: The three inputs collapse in the order they can fail
    Given a handset whose platform <transport> push
    And whose owner has <permission> notifications
    And which Reach <registration>
    Then push reads as <state>
    And the indicator is <colour>

    Examples:
      | transport | permission | registration              | state        | colour  |
      | carries   | left on    | has a registration for    | active       | #2E7D32 |
      | carries   | left on    | holds no registration for | unregistered | #F9A825 |
      | carries   | turned off | has a registration for    | blocked      | #B3261E |
      | has no    | left on    | has a registration for    | unsupported  | #757575 |

  Scenario: Permission is reported before registration
    Given a handset whose platform carries push
    And whose owner has turned off notifications
    And which Reach holds no registration for
    Then push reads as blocked

  Scenario: Before anything has been read, push reads as unavailable
    Given nothing has been read about push yet
    Then push reads as unsupported
    And push is not working

  Scenario Outline: Only the states somebody can act on ask for attention
    Given push reads as <state>
    Then it <attention> attention

    Examples:
      | state        | attention   |
      | active       | needs no    |
      | unregistered | needs       |
      | blocked      | needs       |
      | unsupported  | needs no    |

  Scenario Outline: Every state says something different, in the responder's terms
    Given push reads as <state>
    Then its headline is "<headline>"
    And its detail mentions "<detail>"

    Examples:
      | state        | headline                             | detail                    |
      | active       | Push notifications are on            | with Hand closed          |
      | unregistered | Push notifications are not connected | up to one interval late   |
      | blocked      | Push notifications are turned off    | this phone's own settings |
      | unsupported  | Push notifications are not available | collects its own alerts   |
