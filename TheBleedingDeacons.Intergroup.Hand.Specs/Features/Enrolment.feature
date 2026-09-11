Feature: Who is still on the rota
  As an intergroup
  I want a handset to leave the rota when its responder does
  So that an alert is never addressed to a phone nobody is carrying

  Reach re-checks the responder's role and certification against Unity on
  every request, so a lapsed certification stops the handset at its next
  call without anyone remembering to revoke the device. A refused handset
  clears its token and returns to sign-in carrying the reason, so the
  responder is told rather than left with a phone that has quietly gone
  silent.

  Background:
    Given this handset is signed in to Reach

  Rule: A removal notice is a prompt to check, never an instruction to obey

    A push registration token outlives the device row it was registered
    against, so a notice can be delivered late to a handset whose
    responder has already signed in again — and signing that one out would
    take a working phone off the rota on the strength of a message about a
    pairing that no longer exists. Reach deleted the row before sending,
    so the session check is decisive.

    Scenario: A removal notice never rings and is never shown
      When a removal notice arrives
      Then the alarm never sounded
      And nothing is on screen

    Scenario: It is ignored when this handset is still enrolled
      When a removal notice arrives
      Then Reach was asked whether this handset is still enrolled
      And the handset is still signed in

    Scenario: It signs the handset out when Reach agrees
      Given Reach no longer knows this handset
      When a removal notice arrives
      Then the handset has signed itself out
      And the responder is told something that mentions "taken this handset off the alert rota"

    Scenario: A handset that cannot reach Reach stays signed in
      Given Reach cannot be reached at all
      When a removal notice arrives
      Then the handset is still signed in

    Scenario: A handset with no token has nothing to check and nothing to lose
      Given this handset has no device token
      When a removal notice arrives
      Then Reach was never asked whether this handset is still enrolled

    Scenario: A late removal notice is still checked
      Given Reach no longer knows this handset
      When a removal notice that expired 10 minutes ago arrives
      Then Reach was asked whether this handset is still enrolled
      And the handset has signed itself out

  Rule: Only a refusal signs a handset out

    A network or server failure is ordinary — the next poll tries again,
    and there is nothing to tell a responder about a handset that is
    briefly out of signal.

    Scenario: A revoked token signs the handset out and clears the screen
      When a red alert 1 arrives by push
      And Reach no longer knows this handset
      And the handset polls
      Then the handset has signed itself out
      And nothing is on screen
      And the alarm is silent

    Scenario: A lapsed certification says so, in those words
      Given this responder is no longer a certified telephone responder
      When the handset polls
      Then the handset has signed itself out
      And the responder is told something that mentions "certified telephone responder"

    Scenario: A handset out of signal stays signed in
      Given Reach cannot be reached at all
      When the handset polls
      Then the handset is still signed in
