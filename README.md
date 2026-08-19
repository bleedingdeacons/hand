# Hand

[![CI](https://github.com/bleedingdeacons/hand/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/bleedingdeacons/hand/actions/workflows/ci.yml)

A .NET MAUI app that alerts certified telephone responders when the
helpline needs them. The server side is the
[Reach](https://github.com/bleedingdeacons/reach) WordPress plugin, which
holds the alerts and pushes them.

Runs on Android, iOS, Mac Catalyst and Windows from one codebase.

## What it does

A responder signs in, goes on duty, and puts the handset down. When a
plugin raises an alert through Reach's alerting API, the handset rings —
loudly, repeatedly, and whether or not the app is on screen — until
somebody acknowledges it.

That last part is the whole design, and it is worth being precise about
what each platform can actually do.

| Head | With the app open | With the app closed |
| --- | --- | --- |
| **Android** | Looping alarm on the alarm audio stream, vibration, full-screen alert | **Yes.** A data-only FCM message wakes the app, which raises a full-screen-intent notification on an alarm-category channel. The handset behaves like an incoming call, over the lock screen. |
| **iOS** | Looping alarm (audio session set to `Playback`, so it sounds through the silent switch) | **Not yet — see Known gaps.** The design is a 30-second system-played sound named in the APNs payload, but push is disabled on this head until the Firebase iOS SDK is in place. iOS handsets currently enrol poll-only. |
| **Windows / macOS** | Looping alarm, toast with alarm scenario | **Only while resident.** FCM does not cover these platforms and nothing can wake a terminated process, so Hand runs from login and stays in the tray, polling. "Closed" means not on screen. |

Push is the fast path, not the reliable one. Every alert is stored by
Reach before any push is attempted, and every handset polls as well as
listening — so a phone in a tunnel catches up when it surfaces, and a
handset whose FCM token has silently rotated still gets its alerts.

## Known gaps

**iOS and Mac Catalyst do not receive push yet.** `PushRegistrar` on those
heads can obtain an *APNs device token*, but Reach sends through FCM and
`message.token` requires an *FCM registration token* — a different
identifier, which FCM rejects. Producing one needs the Firebase iOS SDK,
which is not referenced yet.

Rather than enrol a handset that looks push-capable and silently never
rings, the Apple heads report no transport and enrol **poll-only**. Alerts
still arrive while the app is running; they will not wake a closed app.
Android is unaffected and is the head being proven first.

To close it: add `Xamarin.Firebase.iOS.CloudMessaging`, configure Firebase
in `AppDelegate`, and return `Fcm` plus `Messaging.SharedInstance.FcmToken`
from `PushRegistrar`. The APNs registration plumbing is already written.

## Who can use it

Certified telephone responders, and nobody else.

This is stricter than the Reach website, which also admits 12th-steppers.
Reach re-checks the responder's role and certification against Unity on
**every** request, so a lapsed certification stops the handset at its next
call without anyone remembering to revoke the device. A refused handset
clears its token and returns to the sign-in screen carrying the reason, so
the responder is told rather than left with a phone that has quietly gone
silent.

An admin removing a handset from Reach's Devices page deletes the pairing
outright, and Reach pushes a `device_removed` notice as it does. That kind
is the one thing on the alert loop that is an instruction rather than an
alert: it never reaches the alarm, the tray or the alerts list. Hand takes
it as a prompt to check rather than an order to obey — it asks Reach who
it is, and only signs out if Reach no longer knows it. That matters
because an FCM registration token outlives the device row it was
registered against, so a notice can arrive at a handset whose responder
has already signed in again, and signing *that* one out would take a
working phone off the rota. A handset that cannot reach Reach stays signed
in; its next successful poll finds the 401 anyway.

## Signing in

Two routes, both ending in the same long-lived device token held in
platform secure storage (Android keystore, Apple keychain, DPAPI):

- **SSO** — Google, Microsoft, Apple or Facebook, through the system
  browser. Reach's callback returns a *one-time code* to `hand://auth`,
  which Hand trades for the token over TLS. The code rather than the token
  travels through the browser, because a redirect lands in history and can
  be read by anything else registered for the scheme (RFC 8252).
- **Password** — straight to Reach, no browser. This is what the Windows
  head uses when it is not packaged as MSIX and so cannot claim a custom
  URI scheme, and it is the fallback anywhere the browser flow fails.

## Contact details

An alert can carry contact details for the person to call. They are
**not** in the push and **not** in the poll — they would otherwise pass
through Google's servers and sit on a lock screen. Hand shows a *Show
contact* button, and fetches them over TLS only when a responder taps it.
Reach writes an audit entry for every such read.

## Configuration

Settings → the Reach server address, how often to poll, and a name for
this handset (shown in Reach's admin device list).

Build-time configuration follows Register's arrangement: `appsettings.json`
is embedded, and `devsettings.json` is layered on top when built with
`UseDevCredentials=true` (the default). Production builds pass
`-p:UseDevCredentials=false`, so real credentials cannot reach a shipped
package.

**Both files are git-ignored, and neither is required to build.** Copy
`appsettings.example.json` to `appsettings.json` and fill it in. The
example is the only one tracked: `appsettings.json` is the production
settings file, so it is where real values get typed — a Better Stack
token was once committed through it to this public repo, which is why the
build now treats it as optional rather than tracking it.

## Logging

Deliberately identical to Register's, because a duty handset that loses
its diagnostics the moment it goes out of signal is worse than useless —
and out of signal is exactly when it will misbehave.

- Serilog, configured before the DI container so startup itself is logged.
- A **durable** HTTP sink: events are written to a rolling on-disk buffer
  first and shipped to Better Stack in the background. Offline, the buffer
  grows until connectivity returns. Events survive hard app kills.
- `BetterStackLoggerController` rebuilds the whole pipeline atomically when
  settings change, rather than stacking sinks or leaking the old shipper.
- Enrichers for application, environment, platform, device label, app
  version and process id, plus `ExceptionEnricher` for demystified stack
  traces and the full inner-exception chain.
- Global handlers for unhandled AppDomain exceptions, unobserved tasks and
  Android's Java bridge, each with a bounded, never-throwing flush.

`BetterStackConfiguration.Endpoint` normalises a scheme-less value to
`https://` in its setter. Better Stack's dashboard shows the ingest
address as a bare hostname, but `IsValid()` needs an absolute URI — and
without normalisation the config reads as invalid, the controller removes
the sink, and the app ships nothing at all, silently. Register carried
exactly that bug and now has the same fix.

## Building

```bash
dotnet build TheBleedingDeacons.Intergroup.Hand -p:HandAndroidOnly=true
```

```bash
dotnet build TheBleedingDeacons.Intergroup.Hand -p:HandWindowsOnly=true
```

```bash
dotnet build TheBleedingDeacons.Intergroup.Hand -p:HandAppleOnly=true
```

Use those flags rather than `-f`: `-f` sets `TargetFramework` as a global
property, which forces the chosen TFM onto every project in the tree.

Release Android builds ship `android-arm64` only — the default also builds
`android-x64`, which doubles the download for an ABI only an emulator
loads. Signing is opt-in: pass a keystore or the build stays unsigned.

### The quality gate

CI builds the Android head on every push and pull request, and that build *is*
the gate. The Windows and Apple heads have jobs of their own, currently gated
to a manual run (Actions → CI → Run workflow) so an ordinary push pays for one
runner rather than three. `Directory.Build.props` wires in StyleCop.Analyzers
and Meziantou.Analyzer, `.editorconfig` escalates the rules that matter to
`error`, and the csproj promotes the compiled-binding warnings (XC0022–XC0045)
alongside them — so a style violation or a binding that quietly fell back to
reflection fails the build rather than scrolling past in the output. All three
files are byte-identical to Register's; the two apps share one house style
deliberately.

Every head is built, because they do not report the same things and in places
they do not even compile the same files. The WinRT and CsWinRT analyzers
(MVVMTK0045 and friends) only fire on the Windows head, which in turn only
exists when building on Windows at all; and `Apple/**/*.cs` is compiled only
into the iOS and Mac Catalyst heads, so nothing but the macOS job builds those
sources. A head nobody builds is a head nobody knows is broken — which is how
Register's Apple heads came to rot.

One job per platform family, not per head: `-p:HandAppleOnly=true` builds iOS
and Mac Catalyst together, since they need the same runner and share nearly all
their code. MSBuild tags each diagnostic with its target framework, so the log
still says which head broke.

**Run the manual jobs before anything ships.** Gating them is a speed trade,
not a judgement that they stopped mattering — the reasoning above is exactly
why they exist, and the Apple job is the only thing anywhere that compiles
`Apple/**/*.cs`.

The macOS job signs nothing and needs no provisioning profile — a Debug build
with no `RuntimeIdentifier` targets the simulator.

CI passes `-p:UseDevCredentials=false`, so what it analyses is the code that
ships rather than the `USE_DEV_CREDENTIALS` convenience path. Reproduce a CI
build locally with:

```bash
dotnet build TheBleedingDeacons.Intergroup.Hand -p:HandAndroidOnly=true -p:UseDevCredentials=false
```

Swap in `HandWindowsOnly` or `HandAppleOnly` for the other two jobs. The Apple
one needs a Mac.

There is no test project yet, so there is no test job. When one lands it
belongs in the same workflow, shaped like Register's.

## Setup you have to do yourself

These need accounts I cannot act for:

1. **Firebase** — create a project, add an Android app with the id
   `com.thebleedingdeacons.intergroup.hand`, and drop `google-services.json`
   into `Platforms/Android/`. Then paste the service-account key file
   (*Project settings → Service accounts → Generate new private key*) into
   **Reach → Settings**. Without this everything still works by polling.
2. **APNs** — create an APNs key in the Apple Developer portal and upload
   it to Firebase, so FCM can deliver to iOS.
3. **Critical alerts (optional)** — to break through the iOS silent switch
   and Do Not Disturb you need Apple's
   `com.apple.developer.usernotifications.critical-alerts` entitlement,
   granted only on application. **Do not enable the switch in Reach's
   settings until it is in the provisioning profile:** without the
   entitlement Apple *rejects* the notification rather than downgrading it,
   which would silence the very alerts it is meant to make louder. Until
   then urgent alerts use the time-sensitive level, which gets through a
   Focus mode.

## Licence

MIT (Modified).
