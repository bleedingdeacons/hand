Feature: When somebody else answers first
  As one of thirty responders whose phones all rang
  I want the job somebody else took to leave my handset
  So that I am not dismissing cards about calls that are already handled

  A broadcast rings every certified handset at once, and whoever answers
  silences only their own. So Reach sends a second message to everybody
  else it went to, saying who picked it up. That notice never alarms —
  waking a second responder at three in the morning to tell them the first
  one answered would be worse than saying nothing — and none of that is
  special-cased: Reach raises it as blue and Close, and Hand reads those
  two fields exactly as it reads them on any other alert.

  Background:
    Given this handset is signed in to Reach

  Scenario: The notice arrives quietly and stays to be read
    When a red alert 1 arrives by push
    And Jo B acknowledges message 1 elsewhere, and notice 9 arrives
    Then notice 9 is on screen
    And notice 9's button says "Close"
    And the alarm was started once

  Scenario: The message somebody answered comes off this handset
    When a red alert 1 arrives by push
    And Jo B acknowledges message 1 elsewhere, and notice 9 arrives
    Then alert 1 is no longer on screen
    And the history says alert 1 was answered by "Jo B"

  Scenario: That was the last outstanding job, so the alarm stops
    When a red alert 1 arrives by push
    Then the alarm is sounding
    When Jo B acknowledges message 1 elsewhere, and notice 9 arrives
    Then the alarm is silent

  Scenario: Every copy of the message goes, because one responder can hold two handsets
    When a red alert 1 arrives by push
    And a red alert 2 arrives by push, as another copy of message 1
    And Jo B acknowledges message 1 elsewhere, and notice 9 arrives
    Then alert 1 is no longer on screen
    And alert 2 is no longer on screen

  Scenario: An unrelated message is left alone
    When a red alert 1 arrives by push
    And a red alert 2 arrives by push
    And Jo B acknowledges message 1 elsewhere, and notice 9 arrives
    Then alert 2 is on screen
    And the alarm is sounding

  Scenario: A notice about a message this handset never had is harmless
    When a red alert 1 arrives by push
    And Jo B acknowledges message 7 elsewhere, and notice 9 arrives
    Then alert 1 is on screen
    And notice 9 is on screen

  Scenario: A notice naming no message at all matches nothing
    When a red alert 1 arrives by push
    And a notice 9 arrives naming no message
    Then alert 1 is on screen
    And there are 2 alerts on screen

  Scenario: A notice that names nobody still names somebody
    When a red alert 1 arrives by push
    And an anonymous notice 9 about message 1 arrives
    Then notice 9 reports that "Another responder" answered

  Rule: A notice is a statement about the past, so a late one still applies

    Its whole content is that somebody has already dealt with the thing
    that did alarm, and that goes on being true. So the clearing happens
    before the expiry test, even where the notice itself is too stale to
    be worth showing again.

    Scenario: An expired notice still clears the message it reports on
      When a red alert 1 arrives by push
      And an expired notice 9 about message 1 arrives
      Then alert 1 is no longer on screen
      And notice 9 is no longer on screen
      And the alarm is silent

  Scenario: An alert arriving after a notice still rings
    When a red alert 1 arrives by push
    And Jo B acknowledges message 1 elsewhere, and notice 9 arrives
    Then the alarm is silent
    When a red alert 2 arrives by push
    Then the alarm is sounding
