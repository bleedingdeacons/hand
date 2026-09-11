Feature: Alerts reach the handset
  As a certified telephone responder
  I want an alert to reach my handset whichever route it took
  So that a push that never arrived is not a shift nobody covered

  Push is the fast path, never the certain one. Every alert is stored by
  Reach before any push is attempted and every handset polls as well as
  listening, so the two routes overlap on purpose — and both come through
  one door, which is where an alert becomes a card and where the alarm
  starts.

  Background:
    Given this handset is signed in to Reach

  Scenario: A pushed alert is listed, shown and rings
    When a red alert 1 arrives by push
    Then alert 1 is on screen
    And alert 1 was shown in the notification tray
    And the alarm is sounding

  Scenario: A polled alert is admitted the same way
    Given Reach is holding a red alert 1
    When the handset polls
    Then alert 1 is on screen
    And alert 1 was shown in the notification tray
    And the alarm is sounding

  Scenario: The same alert arriving by both routes is one card and one alarm
    When a red alert 1 arrives by push
    And alert 1 arrives again by poll
    Then there is 1 alert on screen
    And the alarm was started once

  Scenario: An alert whose window has already shut is ignored
    When a red alert 1 that expired 10 minutes ago arrives by push
    Then nothing is on screen
    And the alarm never sounded
    And the history remembers alert 1 as expired

  Scenario: An alert with no expiry at all is admitted
    When a red alert 1 that never expires arrives by push
    Then alert 1 is on screen

  Scenario: It still rings when the notification cannot be posted
    Given the notification tray refuses everything
    When a red alert 1 arrives by push
    Then alert 1 is on screen
    And the alarm is sounding

  Scenario: A handset with no token neither asks nor is told anything
    Given this handset has no device token
    And Reach is holding a red alert 1
    When the handset polls
    Then nothing is on screen
    And Reach was never asked for pending alerts

  Rule: The poll can correct what a push could not say

    A push cannot carry contact details and an older Reach did not even
    carry the flag saying they exist, so the poll copy arriving seconds
    later may know better. It is promoted in one direction only: the poll
    joins the contacts table and is authoritative that a contact exists,
    and nothing may take one away, because a push that simply omitted the
    flag would erase a button the responder can already see.

    Scenario: A polled copy gives a pushed alert its contact flag
      When a red alert 1 with no contact arrives by push
      And alert 1 arrives again by poll, this time with a contact
      Then alert 1 offers its contact details

    Scenario: A later copy saying nothing does not take the contact away
      When a red alert 1 with a contact arrives by push
      And alert 1 arrives again by poll, this time with no contact
      Then alert 1 offers its contact details

  Rule: An alert's window is judged against the clock, not against arrival

    Checked before alarming as well as when polling: a push can be
    delivered late, and a handset that has been out of signal should not
    start shouting about something that stopped mattering an hour ago.

    Scenario: Inside its window
      Given the time is 2026-09-11 20:00
      And an alert whose window shuts at 2026-09-11 20:30
      Then it has not expired

    Scenario: On the second its window shuts
      Given the time is 2026-09-11 20:30
      And an alert whose window shuts at 2026-09-11 20:30
      Then it has expired

    Scenario: An alert with no window at all never expires
      Given the time is 2035-01-01 00:00
      And an alert with no window
      Then it has not expired
