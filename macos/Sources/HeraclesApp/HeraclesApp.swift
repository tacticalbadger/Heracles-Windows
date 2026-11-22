import AppKit
import HeraclesCore
import SwiftUI

/// Where the library lives. Shared with the Electron app's folder, so its
/// database is found beside the new one on first launch.
public enum DataDirectory {
    public static var url: URL {
        if let override = ProcessInfo.processInfo.environment["HERACLES_DATA_DIR"], !override.isEmpty {
            return URL(fileURLWithPath: override, isDirectory: true)
        }
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return support.appendingPathComponent("Heracles", isDirectory: true)
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    /// Set by `HeraclesApp` once the library is open.
    static var model: AppModel?
    private var reminders: ReminderScheduler?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Run from a bare executable, as `swift run` does, the process is not
        // a regular app until it says so.
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)

        // Reminders are scheduled with the system, so they arrive whether or
        // not the app is running, and rescheduled whenever tasks change.
        if let model = Self.model, let scheduler = ReminderScheduler(model: model) {
            reminders = scheduler
            model.afterChange = { [weak scheduler] in scheduler?.syncSoon() }
            scheduler.syncSoon()
        }
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard let model = Self.model else { return .terminateNow }
        // Quitting waits for every editor to hand over what it is holding.
        Task {
            await model.flushEditors()
            sender.reply(toApplicationShouldTerminate: true)
        }
        return .terminateLater
    }
}

public struct HeraclesApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @State private var model: AppModel?
    @State private var startupError: String?
    @State private var importReport: ImportReport?

    public init() {
        do {
            let directory = DataDirectory.url
            let model = try AppModel(store: Store(dataDir: directory.path))
            let report = model.importLegacyLibraryIfNeeded(from: AppModel.legacyDatabase(in: directory))
            AppDelegate.model = model
            _model = State(initialValue: model)
            _importReport = State(initialValue: report)
        } catch {
            let reason = (error as? LocalizedError)?.errorDescription ?? String(describing: error)
            _startupError = State(initialValue: reason)
        }
    }

    public var body: some Scene {
        WindowGroup("Heracles") {
            if let model {
                RootView(model: model)
                    .sheet(item: $importReport) { report in
                        ImportSummaryView(report: report)
                    }
            } else {
                ContentUnavailableView(
                    "The library could not be opened", systemImage: "exclamationmark.triangle",
                    description: Text(startupError ?? ""))
            }
        }
        .defaultSize(width: 1200, height: 800)
        .commands { HeraclesCommands() }
    }
}

extension ImportReport: Identifiable {
    public var id: String { backupPath }
}

/// Shown once, after the Electron app's library has been brought across.
struct ImportSummaryView: View {
    let report: ImportReport
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Your library has been imported").font(.title2.bold())
            Text(
                "\(report.notes) notes, \(report.tasks) tasks and \(report.images) images were brought across from the previous version of Heracles."
            )
            Text("The previous library was not changed. A copy of it was saved at:")
            Text(report.backupPath).font(.caption.monospaced()).textSelection(.enabled)
            if !report.repairs.isEmpty {
                Text("Some items needed adjusting to fit:").padding(.top, 4)
                ScrollView {
                    VStack(alignment: .leading, spacing: 4) {
                        ForEach(report.repairs, id: \.self) { line in
                            Text("• \(line)").font(.caption)
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                .frame(maxHeight: 160)
            }
            HStack {
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 480)
    }
}
