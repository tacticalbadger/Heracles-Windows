#!/bin/sh
# Build Heracles.app for distribution: universal, signed with the Developer
# ID certificate under the hardened runtime, notarized by Apple, and stapled.
#
# Usage: macos/release.sh [--skip-notarize]
#   --skip-notarize   stop after signing. For checking a signed build locally.
#
# Signing needs the Developer ID Application certificate in the keychain.
# Set CODESIGN_IDENTITY to use a different one.
#
# Notarizing needs credentials, given in one of three ways:
#   - the same three variables the Electron release uses:
#       APPLE_API_KEY      path to the App Store Connect API key (.p8)
#       APPLE_API_KEY_ID   the key's id
#       APPLE_API_ISSUER   the issuer id
#   - an Apple ID and an app-specific password made for it at
#     account.apple.com:
#       APPLE_ID                      the Apple ID, an email address
#       APPLE_APP_SPECIFIC_PASSWORD   the app-specific password
#       APPLE_TEAM_ID                 the team id, if not the signing identity's
#   - or NOTARY_PROFILE, the name of a profile saved once with
#       xcrun notarytool store-credentials <name> ...
#
# Produces build/Heracles.app and build/Heracles-<version>-mac.zip.
set -eu

ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"

IDENTITY=${CODESIGN_IDENTITY:-"Developer ID Application: Stephen Dobbs (6G8AVA2QV8)"}
NOTARIZE=yes
[ "${1:-}" = "--skip-notarize" ] && NOTARIZE=no

if [ "$NOTARIZE" = yes ]; then
    if [ -n "${NOTARY_PROFILE:-}" ]; then
        set -- --keychain-profile "$NOTARY_PROFILE"
    elif [ -n "${APPLE_API_KEY:-}" ] && [ -n "${APPLE_API_KEY_ID:-}" ] && [ -n "${APPLE_API_ISSUER:-}" ]; then
        set -- --key "$APPLE_API_KEY" --key-id "$APPLE_API_KEY_ID" --issuer "$APPLE_API_ISSUER"
    elif [ -n "${APPLE_ID:-}" ] && [ -n "${APPLE_APP_SPECIFIC_PASSWORD:-}" ]; then
        # The team is the one named in brackets at the end of the identity.
        TEAM=${APPLE_TEAM_ID:-$(printf '%s' "$IDENTITY" | sed -n 's/.*(\([A-Z0-9]*\))$/\1/p')}
        set -- --apple-id "$APPLE_ID" --team-id "$TEAM" --password "$APPLE_APP_SPECIFIC_PASSWORD"
    else
        echo "No notary credentials. Set NOTARY_PROFILE, or APPLE_API_KEY, APPLE_API_KEY_ID" >&2
        echo "and APPLE_API_ISSUER, or APPLE_ID and APPLE_APP_SPECIFIC_PASSWORD." >&2
        echo "To stop after signing, pass --skip-notarize." >&2
        exit 2
    fi
    # Checked with Apple before the long build, so a missing or mistyped
    # credential fails at once and not after ten minutes.
    if ! xcrun notarytool history "$@" >/dev/null 2>&1; then
        echo "Apple did not accept the notary credentials." >&2
        exit 2
    fi
fi

# The universal build: Apple silicon and Intel.
macos/build-app.sh

APP=build/Heracles.app
VERSION=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$APP/Contents/Info.plist")
ZIP=build/Heracles-$VERSION-mac.zip

echo "==> Signing as $IDENTITY"
# The hardened runtime with no exceptions: the app loads no unsigned code and
# makes no executable memory of its own. It is one executable, with the Rust
# core linked into it.
codesign --force --options runtime --timestamp --sign "$IDENTITY" "$APP"
codesign --verify --strict --verbose=2 "$APP"

# What a signed build must be able to do, checked on the build itself.
echo "==> Checking the signed app"
"$APP/Contents/MacOS/Heracles" --self-test >/dev/null || {
    echo "The signed app failed its self-test. Run it by hand to see why:" >&2
    echo "  $APP/Contents/MacOS/Heracles --self-test" >&2
    exit 1
}

rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"

if [ "$NOTARIZE" = no ]; then
    echo "Signed, not notarized: $APP"
    exit 0
fi

echo "==> Notarizing (this waits for Apple, usually a few minutes)"
xcrun notarytool submit "$ZIP" "$@" --wait

echo "==> Stapling"
xcrun stapler staple "$APP"
# The zip is made again so that it holds the stapled app, which opens on a
# Mac that is offline.
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"

spctl --assess --type execute --verbose=2 "$APP"
echo "Ready to distribute: $ZIP"
