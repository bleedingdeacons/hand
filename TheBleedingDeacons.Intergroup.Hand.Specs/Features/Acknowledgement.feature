Feature: Acknowledge, or Close
  As the responder who answers
  I want the card I have taken on to stay where I can read it
  So that I have the reference and the contact at the moment I need them

  An alert either has to be taken on or it does not, and its button says
  which. Acknowledge means "I have this": it silences the alarm, tells
  Reach, and leaves the card on screen — the responder has just accepted a
  call and now has to make it. The second press closes it. Close means
  there was nothing to take on, so one press removes it and Reach is still
  told, because that is how the server learns this handset has dealt with
  its own copy.

  Background:
    Given this handset is signed in to Reach

  Scenario: Acknowledging keeps the card, silences it and tells Reach
    When a red alert 1 arrives by push
    And I press the button on alert 1
    Then alert 1 is on screen
    And alert 1 says "Acknowledged by you"
    And alert 1's button says "Close"
    And the alarm is silent
    And alert 1's notification was taken down
    And Reach was told that alert 1 was acknowledged
    And the history remembers alert 1 as acknowledged

  Scenario: The second press closes it, and does not tell Reach again
    When a red alert 1 arrives by push
    And I press the button on alert 1
    And I press the button on alert 1
    Then nothing is on screen
    And Reach was told about alert 1 once

  Scenario: An informational alert is closed outright, and Reach is still told
    When a blue informational alert 1 arrives by push
    Then alert 1's button says "Close"
    When I press the button on alert 1
    Then nothing is on screen
    And Reach was told that alert 1 was acknowledged
    And the history remembers alert 1 as closed

  Scenario: An ordinary alert offers Acknowledge
    When a red alert 1 arrives by push
    Then alert 1's button says "Acknowledge"
    And alert 1 says nothing about having been answered

  Scenario: The alarm stops even when Reach refuses the acknowledgement
    Given Reach will refuse the acknowledgement
    When a red alert 1 arrives by push
    And I press the button on alert 1
    Then the alarm is silent
    And alert 1 says "Acknowledged by you"

  Scenario: A handset with no token silences its own alarm and says nothing
    Given this handset has no device token
    When a red alert 1 arrives by push
    And I press the button on alert 1
    Then the alarm is silent
    And Reach was told nothing

  Rule: Acknowledge all is the other thing, and clears the screen

    Nobody takes on five jobs by pressing one button. A single press keeps
    its card because the responder needs it to make the call; this one is
    for clearing a screen, so leaving five settled cards behind would make
    the button's name a lie.

    Scenario: Acknowledging everything answers and clears every card
      When a red alert 1 arrives by push
      And a red alert 2 arrives by push
      When I acknowledge everything
      Then nothing is on screen
      And the alarm is silent
      And Reach was told that alert 1 was acknowledged
      And Reach was told that alert 2 was acknowledged

  Scenario: An acknowledged alert is not handed back by the next poll
    When a red alert 1 arrives by push
    And I press the button on alert 1
    And alert 1 arrives again by poll
    Then there is 1 alert on screen
    And the alarm was started once

  Rule: Contact details are fetched only when a responder asks

    They are personal data, so they are in neither the push nor the poll —
    they would otherwise pass through Google's servers and sit on a lock
    screen. Reach writes an audit entry for every read, which is why this
    happens on a tap rather than alongside the alert.

    Scenario: The contact is fetched on the tap, and only once
      When a red alert 1 with a contact arrives by push
      Then Reach was never asked for a contact
      When I ask to see the contact for alert 1
      Then the contact for alert 1 is on screen
      And Reach was asked for alert 1's contact once
      When I ask to see the contact for alert 1
      Then Reach was asked for alert 1's contact once

    Scenario: A contact there is none of is never asked for
      When a red alert 1 with no contact arrives by push
      And I ask to see the contact for alert 1
      Then Reach was never asked for a contact
