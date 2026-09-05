#!/usr/bin/env bash
#
# Write the appsettings.json that CI's artifacts are built against.
#
# appsettings.json is git-ignored, so a checkout never has one — and until the
# APK and .ipa jobs existed, no artifact needed one either. That was correct
# for a build nobody installed and wrong the moment these became something to
# put on a handset.
#
# <b>Hand needs this more than Link does.</b> Link's server address can be
# typed in on the device; Hand's cannot. SettingsPage.xaml marks the Reach
# server address IsReadOnly="True", and ConfigurationService falls back to
# this file when the device preference is empty — which on a fresh install it
# is. So an artifact built without this step has no Reach address and no way
# to be given one: it installs, opens, and fails on everything after sign-in.
#
# Written from a repository *variable*, not a secret. The address is the
# intergroup's public WordPress site and appears in this repo's README;
# pretending otherwise would buy nothing and make it harder to change. What
# the variable buys is that the file still never enters git — which is the
# actual rule, because appsettings.json is where a credential naturally gets
# typed and this repo published a Better Stack token exactly that way.
#
# Only Reach:BaseUrl is written. PollSeconds defaults to 20 in
# ConfigurationService, and the BetterStack section is deliberately left out:
# an artifact anybody can download has no business carrying a log-shipping
# token, and without one the app simply logs locally.
#
# Unset is not a failure. A fork, or this repo before the variable existed,
# builds exactly as it did before and produces an artifact that cannot reach
# a server — which is worth a loud line in the log rather than a broken build.

set -euo pipefail

target=TheBleedingDeacons.Intergroup.Hand/appsettings.json

if [ -z "${BASE_URL:-}" ]; then
	echo 'HAND_BASE_URL is not set; building unconfigured.'
	echo 'The artifact will install and open, and every call after sign-in will fail.'
	echo 'Set it as a repository variable to produce an installable build.'
	exit 0
fi

case "$BASE_URL" in
	https://*) ;;
	*) echo "HAND_BASE_URL must start with https:// -- got '$BASE_URL'" >&2; exit 1 ;;
esac

# A quote or a backslash would break out of the JSON string below. Rejected
# rather than escaped: neither belongs in a URL, so one is a typo worth
# stopping for.
case "$BASE_URL" in
	*'"'*|*'\'*) echo 'HAND_BASE_URL must not contain quotes or backslashes.' >&2; exit 1 ;;
esac

printf '{\n  "Reach": {\n    "BaseUrl": "%s"\n  }\n}\n' "$BASE_URL" > "$target"
echo "Wrote $target:"
cat "$target"
