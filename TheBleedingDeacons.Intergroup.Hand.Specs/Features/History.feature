Feature: What came in last night
  As a responder asked at an intergroup meeting what happened
  I want the handset to remember what arrived and what became of it
  So that the answer is not "the app forgot when I closed it"

  A record of what happened, not a copy of the alert. It holds no contact
  details and must not learn to: those are fetched on demand, audited by
  Reach, and never stored here. The subject, body and reference are the
  same text that already reached the lock screen, so keeping them adds no
  exposure the alert did not already carry.

  Background:
    Given this handset is signed in to Reach

  Scenario: An arriving alert is remembered, outstanding
    When a red alert 1 arrives by push
    Then the history has 1 row
    And the history remembers alert 1 as outstanding

  Scenario: The same alert arriving twice is one row
    When a red alert 1 arrives by push
    And alert 1 arrives again by poll
    Then the history has 1 row

  Scenario: Newest first
    When a red alert 1 arrives by push
    And a red alert 2 arrives by push
    Then the newest row in the history is alert 2

  Scenario: An informational alert was never anybody's to take on
    When a blue informational alert 1 arrives by push
    Then the history remembers alert 1 as closed

  Scenario: An alert this handset took on says so
    When a red alert 1 arrives by push
    And I press the button on alert 1
    Then the history remembers alert 1 as acknowledged
    And alert 1's history row reads "Acknowledged by you"

  Scenario: A notice updates the row it is about rather than adding one
    When a red alert 1 arrives by push
    And Jo B acknowledges message 1 elsewhere, and notice 9 arrives
    Then the history has 1 row
    And the history says alert 1 was answered by "Jo B"

  Scenario: An alert that went stale in a tunnel is still worth a row
    When a red alert 1 that expired 10 minutes ago arrives by push
    Then the history remembers alert 1 as expired

  Rule: The first outcome wins, with one exception

    An alert this responder acknowledged, and which is then reported
    answered because the notice arrived from their own other handset, was
    still answered here — a row that changed its mind about what happened
    would be worse than no row. The exception is acknowledged then passed
    back, which is not a row changing its mind but a second thing
    genuinely happening afterwards.

    Scenario: A late notice does not overwrite what this handset did
      When a red alert 1 arrives by push
      And I press the button on alert 1
      And Jo B acknowledges message 1 elsewhere, and notice 9 arrives
      Then the history remembers alert 1 as acknowledged

    Scenario: A job taken here and then given back says so
      When a red alert 1 arrives by push
      And I press the button on alert 1
      And I pass alert 1 back to the rota
      Then the history remembers alert 1 as passed back
      And alert 1's history row reads "Passed back to the rota"

  Rule: It survives the app being killed

    A duty handset is killed rather than closed — swiped away, or taken
    for memory — and none of those give the app a chance to flush. So the
    file is written on every change rather than on a timer.

    Scenario: Last night's alerts are still there this morning
      When a red alert 1 arrives by push
      And I press the button on alert 1
      And the app is killed and opened again
      Then the history has 1 row
      And the history remembers alert 1 as acknowledged

    Scenario: Rows come back closed, whatever was open when the app died
      When a red alert 1 arrives by push
      And the app is killed and opened again
      Then alert 1's history row is closed

    Scenario: Clearing it forgets everything
      When a red alert 1 arrives by push
      And the history is cleared
      Then the history remembers nothing at all

    Scenario: The oldest are dropped past the cap
      Given 520 alerts have already been remembered
      Then the history has 500 rows

  Rule: Reply is offered from the history, and that is the point of it

    When somebody else takes a job, Reach stops serving the message and
    Hand removes every card — so the history row is the only place left to
    say anything about it. Reach authorises a reply on whether the alert
    could have been sent here, never on who answered, so it lands.

    Scenario: An alert somebody else answered can still be replied to
      When a red alert 1 arrives by push
      And Jo B acknowledges message 1 elsewhere, and notice 9 arrives
      Then alert 1 can be replied to from the history

    Scenario: An expired alert cannot — Reach has already purged it
      When a red alert 1 that expired 10 minutes ago arrives by push
      Then alert 1 cannot be replied to from the history

    Scenario: A subject-only alert still opens, because Reply lives inside
      When a red alert 1 with nothing but a subject arrives by push
      Then alert 1's history row opens
