# Hand domain model — alerts, levels, responses

The living specification is the set of Reqnroll `.feature` files under
[`TheBleedingDeacons.Intergroup.Hand.Specs/Features`](../TheBleedingDeacons.Intergroup.Hand.Specs/Features).
This document is the narrative overview and glossary behind them: what the
words mean, which decisions are settled, and which are still assumptions.

It was written by reading the code rather than the other way round — the app
came first and the executable specification was reverse-engineered from it —
so where this and the feature files disagree, the feature files are what runs
and this is what needs correcting.

## The core idea

A responder signs in, goes on duty, and puts the handset down. When a plugin
raises an alert through Reach, the handset rings — loudly, repeatedly, and
whether or not the app is on screen — until somebody acknowledges it.

Everything else here is qualification of that sentence: *which* alerts ring
(only red), *who* has to answer (the response requirement), and *what happens
to everybody else's copy* when one responder does.

```
Reach stores the alert
   ├──(push: sealed, fast, not guaranteed)──┐
   └──(poll: HTTPS, slower, always right)───┴──▶ AdmitAsync ──▶ card + notification + alarm
```

Both routes go through one door. That is where an alert becomes a card, where
the history remembers it, and where the alarm starts — so a push and the poll
that follows it produce one entry and one alarm rather than two.

## Ubiquitous language

- **Alert** — one delivery to one handset: `(id, message uuid, kind, level,
  response, title, body, reference, expiry, payload)`. Carries no personal
  data, by design and by the server's own stripping.
- **Message** — the send an alert is one delivery *of*. A broadcast is one
  message and many alerts; a responder holding a phone and a tablet gets two
  alerts with two ids and one **message uuid**, which is the only thing the
  copies share.
- **Level** — **red**, **yellow** or **blue**: how loud the handset is about
  it, and what colour the card is.
- **Response requirement** — **first** (somebody takes this on) or **none**
  (everybody reads and closes their own copy).
- **Kind** — what sort of thing happened (`shift_uncovered`, `call_request`).
  Two kinds are not alerts at all: `device_removed` and `message_acknowledged`.
- **Notice** — Reach talking about another alert rather than raising one:
  an acknowledgement notice or a reply. Cannot itself be replied to.
- **Outstanding / settled** — a card is settled when this handset has taken
  it on, or when it was never anybody's job. The alarm counts outstanding
  **red** cards, and stops when the last of them is gone.
- **Meeting mode** — everything still happens, silently.
- **Payload key** — the secret Reach seals pushes to, issued once at
  enrolment and never reissued.
- **Device token** — the bearer token that proves who this handset is, held
  in platform secure storage.

## Levels

| Level | Card | On the handset |
| --- | --- | --- |
| **Red** | `#B3261E` | Full-screen intent, alarm category, looping siren, Do Not Disturb bypass. Rings like an incoming call until somebody answers. |
| **Yellow** | `#F9A825` | A heads-up notification with a sound. Gets attention and *can be missed* — no siren, no screen takeover, swipeable. |
| **Blue** | `#1565C0` | The tray, at ordinary importance. Reminders and information; it wakes nobody. |

**Only red sounds the alarm, and only red keeps it going.** A yellow reminder
arriving mid-call must not leave the siren running after the callback it was
actually ringing for has been answered.

**An alert with no level falls back to its priority** — `urgent` reads as red,
anything else as yellow. A Reach that predates the level sends only the old
two-value field, and reading its absent level as "unrecognised, call it
yellow" would demote every urgent alert that server raises, on the one route
where the handset is newer than the server.

Three Android channels — `reach_alerts`, `reach_warnings`, `reach_notices` —
because a channel's importance and sound are fixed when it is created. A level
added later has to be a **new channel**, never an edit to one of these.

## Acknowledge, or Close

| Response | Button | Pressing it |
| --- | --- | --- |
| **first** | Acknowledge | Silences the alarm, tells Reach, **keeps the card** and marks it "Acknowledged by you". The second press closes it. |
| **none** | Close | Removes the card outright, and still tells Reach — that is how the server learns this handset has dealt with its own copy, and what stops the next poll handing it back. |

Acknowledging used to remove the card, which took the reference and the *Show
contact* button away at exactly the moment they started to matter: the
responder has just accepted a call and now has to make it.

**Acknowledge all is the other thing** and does clear the screen. Nobody takes
on five jobs by pressing one button.

The two are independent of the level: a red alert can be informational (a
drill everybody must see) and a blue one can still be somebody's job.

## When somebody else answers first

Reach sends a second message to every other handset the original went to,
saying who picked it up. Hand does three things with it, and **none of them is
special-cased**: Reach raises the notice as blue and Close, and Hand reads
those two fields exactly as it reads them on any other alert.

1. It **never alarms** — blue.
2. It **removes** the message it reports on, matched on the **message uuid**
   and removing every copy. Not marks — removes: an answered message is over,
   and leaving thirty people a card to dismiss is work invented for no reason.
3. If that was the last outstanding red card, **the alarm stops.**

The notice itself stays, with a Close button. Finding nothing to remove is
normal — the alert may have been acknowledged here, or expired, or never
arrived — and the notice is shown either way, because "Jo answered the 3am
callback" is worth reading regardless.

## The two notices that are not alerts

| Kind | What it is | What Hand does |
| --- | --- | --- |
| `device_removed` | An administrator has taken this handset off the rota | **A prompt to check, never an instruction to obey.** Asks Reach who it is, and signs out only if Reach no longer knows it. A handset that cannot reach Reach stays signed in. |
| `message_acknowledged` | Somebody else answered | Admitted like anything else so it can be read; clears the message it reports on. |

A push registration token outlives the device row it was registered against,
so a removal notice can arrive at a handset whose responder has already signed
in again — and signing *that* one out would take a working phone off the rota.

## What is remembered

An alert exists to be acted on and then to go away; the history answers "what
came in last night, and what became of it", which is a question a responder
gets asked at an intergroup meeting.

| Status | Means |
| --- | --- |
| `outstanding` | Arrived, nobody has dealt with it |
| `acknowledged` | This handset took it on |
| `answered` | Another responder took it, and this handset was told |
| `closed` | Read and closed here; nobody had to take it on |
| `expired` | Its window shut with nothing done about it |
| `passed_on` | Taken on here and then put back out to the rota |

**The first outcome wins**, with one exception: acknowledged → passed back,
which is not a row changing its mind but a second thing genuinely happening
afterwards. It holds **no contact details** and must not learn to.

## Locked design decisions

- **The push carries ciphertext and nothing else.** The whole data map is
  sealed to the handset's payload key — AES-256-GCM over gzip — so what
  crosses Google says nothing about who rang. A push that will not open is
  ignored, reported to Reach once per run, and delivered by the poll instead.
- **The poll is not optional in spirit.** It covers Windows and macOS
  entirely, catches what FCM dropped, and is the only route left when a push
  token rotates silently. It can be turned off, and that is a real loss.
- **Contact details are fetched on a tap**, never pushed or polled, because
  Reach audits every read and they would otherwise sit on a lock screen.
- **HTTPS only.** A plaintext address would put the bearer token and every
  alert body on the wire in the clear.
- **Meeting mode changes the volume, not the rota.** It replaced an on/off
  duty switch that stopped the poll — which meant a responder who forgot to
  come back on was simply missing, with nobody told.
- **The lock screen redaction is offered, not imposed.** Android substitutes
  the public version only where the phone's owner has chosen to hide sensitive
  content; Hand reports which it is getting rather than claiming to prevent it.

## Assumptions still open to change

- The redacted lock screen keeps **urgency** and nothing else — not even the
  reference, which is non-identifying by design but "by design" is the
  assurance this deliberately does not depend on.
- The handled-id set is bounded at **200**, and the history at **500** rows.
  Both are floors under a promise rather than policy.
- **Twenty seconds** is the default poll interval, clamped to 5–300.
- Yellow takes white text on `#F9A825`. If it needs more contrast the answer
  is a deeper amber, not an inverted card.
