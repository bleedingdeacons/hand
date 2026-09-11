Feature: Where this handset talks to, and how often
  As the person who set this handset up
  I want its server address to be checked before it is used
  So that a bearer token and every alert body are never sent in the clear

  The settings page binds the address two-way and saves it straight
  through, so a responder who pastes a plaintext address would put the
  device token and every alert body on the wire with nothing in the app
  saying it happened. http:// is not a development convenience here.

  Scenario Outline: Only an https address is somewhere Hand will talk to
    Given a server address of "<address>"
    Then it <verdict> somewhere Hand will talk to

    Examples:
      | address                 | verdict |
      | https://aa-bristol.org/ | is      |
      | http://aa-bristol.org/  | is not  |
      | aa-bristol.org          | is not  |
      |                         | is not  |

  Rule: The trailing slash is not cosmetic

    Resolving a relative path against a base that does not end in one
    discards its last segment, so a WordPress install in a subdirectory
    would quietly lose the subdirectory.

    Scenario: A missing trailing slash is put back
      Given a server address of "https://aa-bristol.org/wp"
      When the settings are normalised
      Then the server address becomes "https://aa-bristol.org/wp/"

  Rule: The poll interval is clamped rather than trusted

    A mistyped value is a handset that either hammers the server or misses
    its shift.

    Scenario Outline: Out-of-range intervals are brought back in
      Given a poll interval of <given> seconds
      When the settings are normalised
      Then the poll interval is <clamped> seconds

      Examples:
        | given | clamped |
        | 20    | 20      |
        | 1     | 5       |
        | 86400 | 300     |

  Scenario: Twenty seconds is what a handset polls at unless told otherwise
    Given a handset nobody has configured
    Then the poll interval is 20 seconds
    And it is not in a meeting
    And it polls as well as listening

  Rule: Turning the poll off stops the asking, not the alerting

    The poll is what makes the app dependable — it covers Windows and
    macOS entirely and catches whatever FCM dropped while the handset was
    in a tunnel. A handset with it off is trusting push alone, which is
    the fast path and never the certain one.

    Background:
      Given this handset is signed in to Reach

    Scenario: No poll loop is started
      Given polling is turned off
      And Reach is holding a red alert 1
      When the handset starts listening
      Then Reach was never asked for pending alerts

    Scenario: A pushed alert still rings
      Given polling is turned off
      When a red alert 1 arrives by push
      Then alert 1 is on screen
      And the alarm is sounding

    Scenario: A responder pulling deliberately still gets an answer
      Given polling is turned off
      And Reach is holding a red alert 1
      When the handset polls
      Then alert 1 is on screen
