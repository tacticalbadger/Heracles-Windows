#!/bin/sh
# Build Heracles.app, the native Mac app, into build/.
#
# Usage: macos/build-app.sh [--host-only]
#   --host-only   build for this Mac's processor only. Faster, for development.
#
# The app is signed ad hoc, which is enough to run it on this Mac. For a
# build to give to someone else, see release.sh.
set -eu

ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"

if ! xcodebuild -version >/dev/null 2>&1; then
    export DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer
fi

if [ "${1:-}" = "--host-only" ]; then
    ARCH_FLAGS=""
else
    ARCH_FLAGS="--arch arm64 --arch x86_64"
fi

macos/build-core.sh "$@"

echo "==> Building the app"
# shellcheck disable=SC2086  # the flags are meant to split into arguments
swift build --package-path macos --configuration release $ARCH_FLAGS
# shellcheck disable=SC2086
PRODUCTS=$(swift build --package-path macos --configuration release $ARCH_FLAGS --show-bin-path)

APP=build/Heracles.app
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp macos/Info.plist "$APP/Contents/Info.plist"
cp "$PRODUCTS/Heracles" "$APP/Contents/MacOS/Heracles"
codesign --force --sign - "$APP" >/dev/null 2>&1

echo "Built $APP"
