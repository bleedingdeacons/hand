Feature: Meeting mode
  As a responder sitting in a meeting
  I want my handset to go on taking alerts without making a sound
  So that staying on the rota does not mean disrupting the room

  This replaced an on-and-off duty switch, and the difference matters. Off
  duty stopped the poll, so alerts did not arrive at all — a responder who
  forgot to come back on was simply missing from the rota with nobody
  told. Meeting mode changes only the volume: the poll runs, the push
  arrives, the card is listed, the notification is posted, and a red alert
  still takes the screen. What goes is the noise.

  Background:
    Given this handset is signed in to Reach
    And the handset is in a meeting

  Scenario: A red alert still raises the alarm, silently
    When a red alert 1 arrives by push
    Then the alarm is sounding
    And the alarm was raised silently

  Scenario: Out of a meeting the alarm is audible
    Given the handset is not in a meeting
    When a red alert 1 arrives by push
    Then the alarm was raised audibly

  Scenario: The notification is posted, silently
    When a red alert 1 arrives by push
    Then alert 1 was shown in the notification tray
    And the notification was posted silently

  Scenario: The alert is still listed, still outstanding and still remembered
    When a red alert 1 arrives by push
    Then alert 1 is on screen
    And alert 1 is still outstanding
    And the history remembers alert 1 as outstanding

  Scenario: Acknowledging still works and still tells Reach
    When a red alert 1 arrives by push
    And I press the button on alert 1
    Then the alarm is silent
    And Reach was told that alert 1 was acknowledged

  Scenario: The handset goes on asking for alerts
    Given Reach is holding a red alert 1
    When the handset polls
    Then Reach was asked for pending alerts
    And alert 1 is on screen

  Rule: Silencing stops the noise and nothing else

    The poll is untouched and so is every outstanding alert. A red alert
    arriving afterwards starts the alarm again — silently, while meeting
    mode is on.

    Scenario: Silencing keeps the alert and stops the alarm
      When a red alert 1 arrives by push
      And the handset is silenced
      Then the alarm is silent
      And alert 1 is on screen
      And alert 1 is still outstanding

    Scenario: Silencing leaves the poll running
      Given Reach is holding a red alert 2
      When the handset is silenced
      And the handset polls
      Then alert 2 is on screen

    Scenario: Silencing when nothing is sounding is harmless
      When the handset is silenced
      Then the alarm is silent
      And nothing is on screen
