# Hand

[![CI](https://github.com/bleedingdeacons/hand/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/bleedingdeacons/hand/actions/workflows/ci.yml) [![Semgrep](https://github.com/bleedingdeacons/hand/actions/workflows/semgrep.yml/badge.svg?branch=main)](https://github.com/bleedingdeacons/hand/actions/workflows/semgrep.yml) [![Coverage Status](https://coveralls.io/repos/github/bleedingdeacons/hand/badge.svg?branch=main)](https://coveralls.io/github/bleedingdeacons/hand?branch=main)

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

### Levels

Every alert arrives at one of three levels, which is both how loud the
handset is about it and what colour its card is — so a responder can
tell a callback from a reminder across a room, before reading a word.

| Level | Card | On the handset |
| --- | --- | --- |
| **Red** | Red | Takes the screen over with a full-screen intent and rings like an incoming call until somebody answers. The looping siren, the alarm category, the Do Not Disturb bypass. |
| **Yellow** | Amber | A heads-up notification with a sound. It gets attention and it can be missed — no siren, no screen takeover, and it can be swiped away. |
| **Blue** | Blue | The tray, at ordinary importance. Reminders and information; it wakes nobody. |

**Only red sounds the alarm**, and only red keeps it going. A yellow
reminder arriving mid-call must not leave the siren running after the
callback it was actually ringing for has been answered.

Three Android channels — `reach_alerts`, `reach_warnings`,
`reach_notices` — because a channel's importance and sound are fixed
when it is created and cannot be changed afterwards. A level added later
has to be a new channel, not an edit to one of these.

**An alert with no level falls back to its priority.** A Reach that
predates the level sends only `normal`/`urgent`, and reading its absent
level as "call it yellow" would demote every urgent alert that server
raises — on the one route where the handset is newer than the server,
which is the ordinary way round for an app that updates itself.

### Acknowledge, or Close

An alert either has to be taken on or it does not, and its button says
which.

**Acknowledge** means "I have this". The first responder to press it
takes the job: Reach tells everybody else who answered and the alert
comes off their handsets.

**Close** means there was nothing to take on. The message is
information — a reminder, an announcement, the notice saying somebody
else already answered — so every handset reads and closes its own copy,
and closing it leaves it on everybody else's screen.

Closing still tells Reach. That is how the server learns this handset
has dealt with its own copy, and what stops the next poll handing it
straight back.

The two are independent of the level: a red alert can be informational
(a drill everybody must see) and a blue one can still be somebody's job.

### When somebody else answers first

A broadcast rings every certified handset at once, and whoever answers
silences only their own. So Reach sends a second message to everybody
else it went to, saying who picked it up, and Hand does three things
with it.

It **never alarms.** Waking a second responder at three in the morning
to tell them the first one answered would be worse than saying nothing.
None of that is special-cased, though: Reach raises the notice as
**blue** and **Close**, and Hand reads those two fields exactly as it
reads them on any other alert. What used to be this one hard-coded
exception is now something anything can ask for.

It **takes the message off this handset**, where somebody was meant to
take it on. Not marks it — removes it.
An answered message is over: the responder who took it has the job, and
leaving everybody else a card to dismiss one by one is work invented for
no reason. Reach stops serving that message at the same moment, which is
what keeps the next poll from handing it straight back. Matched on the
message uuid rather than the alert id: one message to a responder
holding two handsets is two alerts with two ids, and the uuid is the
only thing the copies share.

If that was the last outstanding alert, **the alarm stops** — a handset
left ringing about a job somebody else has taken is the thing this
exists to remove.

The notice itself stays, with a **Close** button. It is the whole of
what this handset still needs to know.

### Acknowledging keeps your own card

The handset that answers is the exception: its card stays on screen,
says "Acknowledged by you", and its button becomes Close.

Acknowledging used to remove it, which took the reference and the Show
contact button away at exactly the moment they started to matter — the
responder has just accepted a call and now has to make it. So
Acknowledge silences the alarm, drops the tray notification and tells
Reach; the second press closes the card. **Acknowledge all** is the
other thing and still clears the screen: nobody takes on five jobs by
pressing one button.

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

### The Core split

Three projects:

| Project | Frameworks | What is in it |
| --- | --- | --- |
| `TheBleedingDeacons.Intergroup.Hand` | the four platform heads | The MAUI app: views, view-models, the platform partials, and every service that touches `Preferences`, `SecureStorage`, `FileSystem`, `MainThread`, `Vibration`, `AppInfo`, `DeviceInfo` or `WebAuthenticator`. |
| `TheBleedingDeacons.Intergroup.Hand.Core` | `net10.0` | The half with no MAUI in it: the wire models, `ReachClient`, `AlertService`, the Serilog and Better Stack glue. |
| `TheBleedingDeacons.Intergroup.Hand.Tests` | `net10.0` | xUnit v3 over Hand.Core. |

The split exists so the code can be tested at all. A test project cannot
reference the app — its target frameworks are `net10.0-android`, `-ios`,
`-maccatalyst` and `-windows`, a `net10.0` test host has no compatible one to
resolve against, and there is no `net10.0` head of a MAUI app. So the testable
code has to live somewhere a test project can see it.

The line is not a matter of taste: the moment a file references a MAUI type it
stops compiling in Hand.Core, because that project does not have the workload.
Where a piece of logic was worth having on the testable side, the MAUI call it
depended on became an interface instead — `AlertService` takes an
`IUiDispatcher` rather than calling `MainThread` directly, which is what let
the alert loop come across. The services listed in the app row above are each a
candidate for the same treatment; none was worth inventing a seam for in the
pass that introduced the split.

Register arrived at the same arrangement from the other direction: its testable
code was already a separate library because it was shared with other consumers,
and that is what its test project references.

### Compiling the heads

```bash
dotnet build TheBleedingDeacons.Intergroup.Hand -p:HandAndroidOnly=true
```

```bash
dotnet build TheBleedingDeacons.Intergroup.Hand -p:HandWindowsOnly=true
```

```bash
dotnet build TheBleedingDeacons.Intergroup.Hand -p:HandAppleOnly=true
```

`HandAppleOnly` builds iOS and Mac Catalyst together. Either can be had on its
own with `-p:HandIosOnly=true` or `-p:HandMacCatalystOnly=true`; CI's iOS job
uses the first, because bundling the head and its extension is the point there
and Mac Catalyst would only be paid for twice.

Use those flags rather than `-f`: `-f` sets `TargetFramework` as a global
property, which forces the chosen TFM onto every project in the tree.

Release Android builds ship `android-arm64` only — the default also builds
`android-x64`, which doubles the download for an ABI only an emulator
loads. Signing is opt-in: pass a keystore or the build stays unsigned.

### The quality gate

CI builds the Android and iOS heads and runs the tests on every push and pull
request, and those three *are* the gate. There are no manual jobs: the Windows
and Mac Catalyst ones this section used to describe were deleted rather than
gated, and both heads now build only on a developer's machine.
`Directory.Build.props` wires in StyleCop.Analyzers
and Meziantou.Analyzer, `.editorconfig` escalates the rules that matter to
`error`, and the csproj promotes the compiled-binding warnings (XC0022–XC0045)
alongside them — so a style violation or a binding that quietly fell back to
reflection fails the build rather than scrolling past in the output. All three
files are byte-identical to Register's; the two apps share one house style
deliberately.

Heads do not report the same things, and in places they do not even compile the
same files — so a head nobody builds is a head nobody knows is broken, which is
how Register's Apple heads came to rot. `Apple/**/*.cs` compiles only into the
iOS and Mac Catalyst heads, and for a while after the Windows and Mac Catalyst
jobs were deleted it compiled into nothing at all. The iOS job buys that back.

**What still compiles nowhere.** `Platforms/Windows/**` and
`Platforms/MacCatalyst/**`. The WinRT and CsWinRT analyzers (MVVMTK0045 and
friends) fire only on the Windows head, which in turn only exists when building
on Windows at all, so they are reported nowhere. Build those two locally before
anything ships.

**What the iOS job does not prove.** It builds unsigned
(`-p:HandUnsigned=true`), so it compiles the head and the notification service
extension and exercises neither the shared keychain group nor the App Group.
Only a Mac with a real identity does that.

The csproj used to claim this job was impossible — that the entitlements the
extension needs force signing-identity detection, which no runner can satisfy.
That was wrong, and it left the iOS head unbuilt for weeks:
`_DetectSigningIdentity` is conditioned on `EnableCodeSigning`, not on the
entitlements. Switching signing off is sufficient, and the entitlements stay
declared unconditionally, which is where a setting like that belongs.

CI passes `-p:UseDevCredentials=false`, so what it analyses is the code that
ships rather than the `USE_DEV_CREDENTIALS` convenience path. Reproduce a CI
build locally with:

```bash
dotnet build TheBleedingDeacons.Intergroup.Hand -p:HandAndroidOnly=true -p:UseDevCredentials=false
```

The iOS job, which needs a Mac:

```bash
dotnet build TheBleedingDeacons.Intergroup.Hand -c Release -p:HandIosOnly=true -p:HandUnsigned=true -p:UseDevCredentials=false -p:RuntimeIdentifier=ios-arm64
```

### What CI produces

Two artifacts on every run, `hand-apk` and `hand-ipa-unsigned`, kept for
30 days. Open the run from **Actions** and they are in the Artifacts section at
the bottom of the summary.

**And a GitHub Release on every merge to `main`.** Run artifacts expire after
30 days, so a version older than a month had nothing to show for itself at all;
the `version` job now tags `vX.Y.Z` and publishes a release with an APK
attached. It is under **Releases**, and it does not expire.

That APK is *rebuilt* after the version is written rather than taken from the
build job. The build job's artifact came from the commit before the bump, so
its manifest carries the previous version — and, more to the point, the
previous Android `versionCode`, which is what decides whether a build can
update an existing install. Attaching it to a tag naming the new version would
ship an APK that disagrees with its own release.

Only the APK is attached. The `.ipa` is built on a macOS runner and the release
job is Ubuntu, and carrying it across would buy nothing: it is unsigned, so it
installs on nothing until somebody re-signs it. It stays on the run.

Tags are new here and are not load-bearing: `bump-version.sh` reads the current
version out of the csproj, not from `git describe`. Worth knowing before
assuming it and Link's script of the same name are interchangeable — Link's
tags *are* load-bearing.

**To cut a release without a merge**, run the `CI` workflow by hand on `main`
(Actions → CI → Run workflow). It publishes exactly as a merge does. That
exists because a merge is the release and a merge cannot be repeated, so a run
that never survives — a wedged queue, a runner outage — would otherwise leave
no way to release that commit at all. Running it after a successful release is
a no-op: `bump-version.sh` refuses to bump on top of a `chore: version` commit,
and the tag and release steps both check for the thing already existing.

Both are built against the **`HAND_BASE_URL`** repository variable, which CI
writes into `appsettings.json` before compiling. Without it they come out with
no idea which intergroup they belong to — fine for a build nobody installs, and
useless the moment one reaches a phone, as Link's first sideloaded `.ipa`
demonstrated by opening, offering sign-in, and failing on every call after it.

Hand needs this more than Link did. Link's server address can be typed in on
the device; Hand's cannot — `SettingsPage.xaml` marks the Reach server address
`IsReadOnly="True"`, and `ConfigurationService` falls back to this file when
the device preference is empty, which on a fresh install it is. So an
unconfigured artifact cannot be pointed anywhere after the fact.

Only `Reach:BaseUrl` is written. `PollSeconds` comes from the default of 20 in
`ConfigurationService`, and the `BetterStack` section is deliberately left
out — an artifact anybody can download has no business carrying a log-shipping
token. The file still never enters git.

**Push needs the `GOOGLE_SERVICES_JSON` secret, and its absence is silent.**
Both Android builds write `Platforms/Android/google-services.json` from it.
Without it the build succeeds and the artifact enrols **poll-only**: alerts
arrive on the poll interval while the app is running, and a handset with the
app closed does not ring. Since that is the one thing this app exists to do,
the log now says which of the two states every build is in.

It is a secret rather than a variable because the repository is public and the
file carries an API key and a project number. Neither is a password — Google
expects this file to ship inside an APK — but an Actions log is a worse place
for it than the inside of a binary. So, unlike `appsettings.json`, its contents
are never printed. The script validates it instead: malformed JSON fails the
build, and so does a file whose `package_name` is not
`com.thebleedingdeacons.intergroup.hand`, because a config for the wrong app
produces a build that looks completely healthy and never receives a push.

`v1.17.1` was released before this existed and is poll-only. It was found by
unzipping the released APK and seeing no `AIza…` key in its `resources.arsc`
where a locally built one had both that and the app id.

Unset the variable and the old behaviour returns: a build that succeeds and an
app that cannot reach a server. That is what a fork gets.

**Neither is a shipping artifact**, and both are easy to mistake for one:

* **The .ipa is unsigned.** It installs on nothing until it is re-signed with
  an Apple Developer Program certificate. It is a compile gate for the iOS head
  and an input to whatever does the signing later.
* **The APK is signed with the runner's throwaway debug keystore**, which is
  not the one on any developer machine. It will refuse to install over a
  locally built Hand with `INSTALL_FAILED_UPDATE_INCOMPATIBLE`, and the only
  way past that is `adb uninstall` — which drops the handset's enrolment and
  its local alert history. Nothing becomes permanently unreadable the way it
  does in Link, because Reach holds the payload key and a re-enrolled handset
  gets a fresh one; but the handset is off the rota until somebody signs it
  back in. Not something to do to a duty phone mid-shift.

### Tests and coverage

```bash
dotnet test TheBleedingDeacons.Intergroup.Hand.Tests
```

The suite is xUnit v3 on Microsoft.Testing.Platform, the same arrangement
Register uses. It runs on every push and pull request alongside the Android
build, on a plain Ubuntu runner with no MAUI workload to install.

To reproduce the coverage number CI reports:

```bash
dotnet tool restore
```

```bash
dotnet coverlet TheBleedingDeacons.Intergroup.Hand.Tests/bin/Debug/net10.0/TheBleedingDeacons.Intergroup.Hand.Tests.dll --target dotnet --targetargs "TheBleedingDeacons.Intergroup.Hand.Tests/bin/Debug/net10.0/TheBleedingDeacons.Intergroup.Hand.Tests.dll" --format cobertura --output coverage/coverage.cobertura.xml --include "[TheBleedingDeacons.Intergroup.Hand.Core]*" --exclude-by-attribute Obsolete
```

Coverage is collected with `coverlet.console` rather than coverlet's collector
or msbuild integration, because those hook VSTest and Microsoft.Testing.Platform
does not use it.

**What the badge covers.** `TheBleedingDeacons.Intergroup.Hand.Core`, minus
anything marked `[Obsolete]` — see [what is in it](#the-core-split) below. The app project is not in
the figure, and cannot be: nothing can reference a project whose only target
frameworks are platform heads. Read the percentage as "the MAUI-free half is
this well covered", not "the app is". Register's badge has exactly the same
scope for the same reason.

Deprecated code is left out deliberately: writing tests for something already
marked for removal would move the number without improving anything, and
counting it would penalise the deprecation rather than the debt.

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
