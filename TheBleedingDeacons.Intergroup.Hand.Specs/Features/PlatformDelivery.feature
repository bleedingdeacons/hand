@manual @ignore
Feature: Ringing a handset nobody is holding
  These scenarios are the whole reason this app exists, and not one of
  them can be verified by a test host: they are Android, Apple and Windows
  behaviour, checked on a real device before a release. They are excluded
  from the automated run, and written down here because the alternative is
  that they are written down nowhere.

  Push is the fast path, never the certain one. Every alert is stored by
  Reach before any push is attempted and every handset polls as well as
  listening, so a phone in a tunnel catches up when it surfaces and a
  handset whose FCM token has silently rotated still gets its alerts.

  Rule: Android rings with the app closed

    Scenario: A red alert reaches a handset with the app closed and the screen off
      Given the app is not running
      And the handset is face-down on a table
      When Reach raises a red alert
      Then a data-only FCM message wakes the app
      And a full-screen-intent notification takes the screen over the lock screen
      And the handset rings like an incoming call until somebody answers
      # Alarm-category channel, SetFullScreenIntent(highPriority), SetOngoing.

    Scenario: The siren is on the alarm stream rather than the media one
      Given the phone's media volume is at zero
      When a red alert arrives
      Then the siren is still audible
      # AudioUsageKind.Alarm, on a looping player.

    Scenario: A yellow alert is a heads-up notification that can be missed
      When a yellow alert arrives with the app closed
      Then it appears as a heads-up notification with a sound
      And it can be swiped away
      And the screen is not taken over

    Scenario: A blue alert wakes nobody
      When a blue alert arrives with the app closed
      Then it appears in the tray at ordinary importance
      And nothing sounds

    Scenario: The three channels keep the importance they were created with
      Given Hand has been installed before
      When the app starts
      Then reach_alerts, reach_warnings and reach_notices are unchanged
      # A channel's importance and sound are fixed when it is created and
      # cannot be edited afterwards. A level added later has to be a new
      # channel, never a change to one of these.

  Rule: Apple rings with the app closed, once the account work is done

    Scenario: A pushed alert sounds for thirty seconds while the app wakes behind it
      Given this build carries a GoogleService-Info.plist and an APNs key
      And the app is not running
      When Reach raises a red alert
      Then the sound named in the APNs payload plays
      And content-available wakes the app, which starts the looping alarm behind it

    Scenario: The lock screen shows text the extension decrypted
      When a pushed alert arrives on a locked iPhone
      Then the notification service extension has opened the payload before the lock screen renders it

    Scenario: The alarm sounds through the ring/silent switch
      Given the switch is set to silent
      When a red alert arrives with the app open
      Then the looping alarm is audible
      # AVAudioSession category Playback, NumberOfLoops = -1.

    Scenario: A build without Firebase enrols poll-only, and says so
      Given this build has no GoogleService-Info.plist
      When the handset enrols
      Then it reports no push transport
      And the log says which of the two states this build is in
      And Settings shows push as not available

  Rule: Windows and macOS are resident rather than woken

    FCM does not cover these platforms and nothing can wake a terminated
    process, so "closed" here means not on screen rather than not running.

    Scenario: The app runs from login and stays in the tray
      Given Windows has been restarted and the responder has signed in
      When nothing has been opened by hand
      Then Hand is running in the tray and polling

    Scenario: An alert arrives within one poll interval
      Given Hand is in the tray
      When Reach raises a red alert
      Then a toast with the alarm scenario appears within the poll interval
      And the looping alarm sounds

  Rule: The lock screen redaction is offered, not imposed

    Android substitutes the public version only where the phone's owner
    has chosen to hide sensitive content. Where they have chosen to show
    everything — the default on many devices — the alert's own words go on
    the lock screen and nothing in the app can stop it. So Hand reports
    which it is getting rather than claiming to have prevented it.

    Scenario: A handset set to hide sensitive content shows the redacted line
      Given the phone is set to hide sensitive notification content
      When a red alert arrives with the screen locked
      Then the lock screen reads "Urgent helpline alert" and "Unlock to read"
      And Hand reports its lock screen as hidden to Reach

    Scenario: A handset set to show everything is reported, not corrected
      Given the phone is set to show all notification content
      When a red alert arrives with the screen locked
      Then the alert's own subject is readable on the lock screen
      And Hand reports its lock screen as shown to Reach
      # Which is what puts that handset on Reach's devices screen as one
      # reading helpline alerts out to the room.

  Rule: Installing over a Hand that is already there

    Scenario: A CI-built APK refuses to install over a locally built one
      Given a handset carrying a locally built Hand
      When the APK from a GitHub Release is installed over it
      Then the install fails with INSTALL_FAILED_UPDATE_INCOMPATIBLE
      # The runner signs with a throwaway debug keystore. The only way past
      # it is adb uninstall, which drops the handset's enrolment and its
      # local alert history — the handset is off the rota until somebody
      # signs it back in. Not something to do to a duty phone mid-shift.
