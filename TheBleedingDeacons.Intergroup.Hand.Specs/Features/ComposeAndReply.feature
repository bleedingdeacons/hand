Feature: Answering back
  As a responder who has read an alert
  I want to reply in my own words, or hand the job back
  So that the person who raised it hears something other than silence

  A reply settles nothing: it is not a second person taking the job on, so
  an alert still outstanding stays outstanding and its Acknowledge button
  stays where it was. Passing a job back is the other thing — the job has
  gone back to the rota, so the card goes with it.

  Background:
    Given this handset is signed in to Reach

  Scenario: A reply reaches Reach and leaves the alert alone
    When a red alert 1 arrives by push
    And I reply "On my way, ten minutes" to alert 1
    Then Reach was sent "On my way, ten minutes" about alert 1
    And the reply was accepted
    And alert 1 is on screen
    And alert 1 is still outstanding
    And alert 1's button says "Acknowledge"

  Scenario: An empty reply is refused here rather than at the server
    When a red alert 1 arrives by push
    And I reply "   " to alert 1
    Then Reach was sent no reply
    And the reply was refused

  Scenario: A reply the server refuses is reported as refused
    Given Reach will refuse the reply
    When a red alert 1 arrives by push
    And I reply "Sorry, cannot take this" to alert 1
    Then the reply was refused

  Scenario: A handset with no token sends nothing
    Given this handset has no device token
    When a red alert 1 arrives by push
    And I reply "Anybody there" to alert 1
    Then Reach was sent no reply
    And the reply was refused

  Rule: A notice cannot be replied to

    The server refuses a reply to one — otherwise an answered call becomes
    an unbounded exchange between two handsets — so offering the button
    anyway gave a responder something that looked like it worked, took
    their words, and threw them away against a 404.

    Scenario: An ordinary alert offers Reply
      Given an alert at level "red"
      Then it can be replied to

    Scenario: An acknowledgement notice does not
      Given a notice about somebody else's acknowledgement
      Then it cannot be replied to

    Scenario: Neither does a reply
      Given an alert of kind "message_reply"
      Then it cannot be replied to

  Rule: Passing a job back is finished here

    Reach raises the new message with this handset excluded, so the next
    poll does not hand the same job straight back. It is recorded as
    passed back rather than closed, because the morning-after question has
    different answers and a history that collapsed them would say the
    wrong one.

    Scenario: The card goes and the history says where the job went
      When a red alert 1 arrives by push
      And I press the button on alert 1
      And I pass alert 1 back to the rota
      Then Reach was asked to pass alert 1 back
      And alert 1 is no longer on screen
      And the history remembers alert 1 as passed back

    Scenario: A refusal keeps the card, because the job is still this handset's
      Given Reach will refuse the pass-back
      When a red alert 1 arrives by push
      And I press the button on alert 1
      And I pass alert 1 back to the rota
      Then alert 1 is on screen
      And the history remembers alert 1 as acknowledged
