// swift-tools-version: 6.0
import PackageDescription

// The Mac app. Run `macos/build-core.sh` first: it builds the Rust core into
// Frameworks/ and writes its Swift bindings into Sources/HeraclesCore/Generated/.
let package = Package(
    name: "Heracles",
    // Must match MACOSX_DEPLOYMENT_TARGET in build-core.sh.
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "HeraclesCore", targets: ["HeraclesCore"]),
    ],
    targets: [
        // heracles-core, the Rust static library, with its C header.
        .binaryTarget(name: "HeraclesCoreFFI", path: "Frameworks/HeraclesCoreFFI.xcframework"),
        // The Swift face of the core: generated bindings plus a few extensions.
        .target(name: "HeraclesCore", dependencies: ["HeraclesCoreFFI"]),
        .testTarget(name: "HeraclesCoreTests", dependencies: ["HeraclesCore"]),
        // Everything in the app except its entry point, so it can be tested.
        .target(name: "HeraclesApp", dependencies: ["HeraclesCore"]),
        .testTarget(name: "HeraclesAppTests", dependencies: ["HeraclesApp"]),
        .executableTarget(name: "Heracles", dependencies: ["HeraclesApp"]),
    ]
)
