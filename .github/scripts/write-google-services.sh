#!/usr/bin/env bash
#
# Write Platforms/Android/google-services.json, which is what makes push work.
#
# <b>Without this the build succeeds and the app polls.</b> The csproj includes
# the file only Condition="Exists(...)", so a CI checkout — where it is
# git-ignored and therefore absent — produces an APK in which GetToken() fails,
# PushRegistrar reports no transport, and the handset enrols poll-only. Alerts
# still arrive, on the poll interval, while the app is running. A phone with the
# app closed does not ring.
#
# That is the one thing this app exists to do, so an artifact somebody installs
# on a duty handset must not be built without this. It went unnoticed until the
# v1.17.1 release APK was installed on a handset and compared with a local
# build: no AIza… key and no 1:…:android:… app id in its resources.arsc, where
# the local Debug build had both.
#
# A SECRET, not a variable, which is the difference from write-appsettings.sh.
# google-services.json carries an API key and a project number. Neither is a
# password — Google expects this file to ship inside an APK, and anyone can
# unzip one — but a public repository's Actions log is a worse place for it
# than the inside of a binary, and there is no reason to put it there.
#
# So this script NEVER prints the file. write-appsettings.sh cats its output on
# purpose, because seeing which intergroup a build points at is worth having in
# the log; this one prints only what it can safely say.
#
# Unset is not a failure. A fork has no Firebase project and must still build;
# what it gets is the old poll-only behaviour, said loudly rather than
# discovered on a handset at three in the morning.

set -euo pipefail

target=TheBleedingDeacons.Intergroup.Hand/Platforms/Android/google-services.json
expected_package=com.thebleedingdeacons.intergroup.hand

if [ -z "${GOOGLE_SERVICES_JSON:-}" ]; then
	echo 'GOOGLE_SERVICES_JSON is not set; building without Firebase.'
	echo 'The app will enrol POLL-ONLY: alerts arrive on the poll interval while'
	echo 'it is running, and a handset with the app closed will not ring.'
	echo 'Set it as a repository secret to build an artifact with working push.'
	exit 0
fi

mkdir -p "$(dirname "$target")"
printf '%s' "$GOOGLE_SERVICES_JSON" > "$target"

# Validate without echoing. A malformed or wrong-project file is worse than an
# absent one: absent is a documented degraded state that says so, whereas a file
# for the wrong app produces a build that looks completely healthy, registers a
# token against somebody else's project, and silently never receives a push.
python3 - "$target" "$expected_package" <<'PY'
import json, sys

path, expected = sys.argv[1], sys.argv[2]

try:
    with open(path, encoding="utf-8") as handle:
        data = json.load(handle)
except json.JSONDecodeError as error:
    # Position only. The message can quote the document.
    sys.exit(f"::error::GOOGLE_SERVICES_JSON is not valid JSON (line {error.lineno}, column {error.colno}).")

packages = [
    client.get("client_info", {}).get("android_client_info", {}).get("package_name")
    for client in data.get("client", [])
]

if not packages:
    sys.exit("::error::GOOGLE_SERVICES_JSON has no client entries; this is not a google-services.json.")

if expected not in packages:
    # Naming the expected package is fine — it is in the csproj and the
    # manifest. What is in the secret stays unprinted.
    sys.exit(
        f"::error::GOOGLE_SERVICES_JSON has no client for {expected}. "
        f"It contains {len(packages)} client entry/entries for other package(s). "
        "Download the file for this app from the Firebase console."
    )

print(f"google-services.json validated: {len(packages)} client entry/entries, including {expected}.")
PY

echo "Wrote $target ($(wc -c < "$target") bytes). Contents deliberately not logged."
