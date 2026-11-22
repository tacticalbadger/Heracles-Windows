import AppKit
import HeraclesApp

if CommandLine.arguments.contains("--self-test") {
    // Drives the editor in a window against a throwaway library, then exits
    // 0 if every check passed. See SelfTest.swift.
    let app = NSApplication.shared
    app.setActivationPolicy(.accessory)
    Task { @MainActor in
        let passed = await SelfTest.run()
        exit(passed ? 0 : 1)
    }
    app.run()
} else if let flag = CommandLine.arguments.firstIndex(of: "--snapshot"), CommandLine.arguments.count > flag + 1 {
    // Draws the app's own windows, filled with sample notes, into PNG files
    // in the given folder. See Snapshot.swift.
    let folder = CommandLine.arguments[flag + 1]
    let app = NSApplication.shared
    app.setActivationPolicy(.accessory)
    Task { @MainActor in
        exit(await Snapshot.write(to: folder) ? 0 : 1)
    }
    app.run()
} else {
    HeraclesApp.main()
}
