#!/bin/sh
# Build heracles-core for the Mac app.
#
# Produces the two things the Swift package needs and git does not track:
#   macos/Frameworks/HeraclesCoreFFI.xcframework        the Rust static library
#   macos/Sources/HeraclesCore/Generated/heracles_core.swift   its Swift bindings
#
# Usage: macos/build-core.sh [--host-only]
#   --host-only   build for this Mac's processor only. Faster, for development.
#                 The default builds a universal library (Apple silicon + Intel).
set -eu

ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"

# Must match `platforms` in macos/Package.swift.
export MACOSX_DEPLOYMENT_TARGET=14.0

HOST=$(rustc -vV | sed -n 's/^host: //p')
if [ "${1:-}" = "--host-only" ]; then
    TARGETS=$HOST
else
    TARGETS="aarch64-apple-darwin x86_64-apple-darwin"
fi

# xcodebuild needs a full Xcode. Use the installed one if the command line
# tools are what is selected.
if ! xcodebuild -version >/dev/null 2>&1; then
    export DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer
fi

WORK=target/xcframework
GENERATED=macos/Sources/HeraclesCore/Generated
FRAMEWORK=macos/Frameworks/HeraclesCoreFFI.xcframework
rm -rf "$WORK" "$FRAMEWORK" "$GENERATED"
mkdir -p "$WORK/Headers" "$GENERATED" macos/Frameworks

LIBRARIES=""
for TARGET in $TARGETS; do
    echo "==> Building heracles-core for $TARGET"
    cargo build --quiet --release --package heracles-core --target "$TARGET"
    LIBRARIES="$LIBRARIES target/$TARGET/release/libheracles_core.a"
done
# shellcheck disable=SC2086  # the list is meant to split into arguments
lipo -create $LIBRARIES -output "$WORK/libheracles_core.a"

echo "==> Generating Swift bindings"
BINDGEN="cargo run --quiet --package heracles-bindgen --bin uniffi-bindgen-swift --"
DYLIB=target/$HOST/release/libheracles_core.dylib
$BINDGEN --swift-sources "$DYLIB" "$GENERATED"
# SwiftPM finds a library's C module through a file named module.modulemap.
# The module is named for what the generated Swift imports.
$BINDGEN --headers --modulemap --modulemap-filename module.modulemap \
    --module-name heracles_coreFFI "$DYLIB" "$WORK/Headers"

echo "==> Assembling $FRAMEWORK"
xcodebuild -create-xcframework \
    -library "$WORK/libheracles_core.a" \
    -headers "$WORK/Headers" \
    -output "$FRAMEWORK" >/dev/null

echo "Done. Build the app with: swift build --package-path macos"
