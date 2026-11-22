import Foundation
import HeraclesCore
import UserNotifications

/// Reading and writing the dates stored on tasks. They are UTC timestamps in
/// the form JavaScript's `toISOString` writes, which is what the Electron
/// app stored.
public enum TaskDates {
    private static let withFraction = Date.ISO8601FormatStyle(includingFractionalSeconds: true)
    private static let withoutFraction = Date.ISO8601FormatStyle()

    public static func date(from string: String, in zone: TimeZone = .current) -> Date? {
        if let date = try? withFraction.parse(string) { return date }
        if let date = try? withoutFraction.parse(string) { return date }
        // A bare day, such as "2026-12-01".
        return try? Date.ISO8601FormatStyle(timeZone: zone).year().month().day().parse(string)
    }

    public static func string(from date: Date) -> String {
        withFraction.format(date)
    }

    /// A due date: nine in the morning, local time, on the chosen day.
    public static func dueDate(on day: Date, in zone: TimeZone = .current) -> String {
        string(from: time(hour: 9, on: day, in: zone))
    }

    /// A deadline: five in the afternoon, local time, on the chosen day.
    public static func deadline(on day: Date, in zone: TimeZone = .current) -> String {
        string(from: time(hour: 17, on: day, in: zone))
    }

    private static func time(hour: Int, on day: Date, in zone: TimeZone) -> Date {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone
        return calendar.date(bySettingHour: hour, minute: 0, second: 0, of: day) ?? day
    }

    /// A stored date as a card shows it: "1 Dec", with the year if it is not
    /// this one.
    public static func label(for string: String) -> String {
        guard let date = date(from: string) else { return string }
        let sameYear = Calendar.current.isDate(date, equalTo: .now, toGranularity: .year)
        return date.formatted(sameYear ? .dateTime.day().month(.abbreviated) : .dateTime.day().month(.abbreviated).year())
    }
}

/// A notification to schedule for a task's reminder.
public struct PlannedReminder: Equatable, Sendable {
    public var identifier: String
    public var title: String
    /// The todo list to open when the notification is clicked.
    public var noteID: String
    public var date: Date
}

/// What to change so that the notifications the system holds match the
/// reminders the library has. Worked out apart from the system calls, so it
/// can be tested.
public struct ReminderPlan: Equatable, Sendable {
    /// Marks a notification as one of this app's reminders.
    public static let prefix = "heracles.reminder."

    public let toAdd: [PlannedReminder]
    public let toRemove: [String]

    public init(reminders: [Reminder], scheduled: [String], now: Date) {
        let wanted: [PlannedReminder] = reminders.compactMap { reminder in
            guard let date = TaskDates.date(from: reminder.remindAt), date > now else { return nil }
            let title = reminder.taskTitle.trimmingCharacters(in: .whitespaces)
            return PlannedReminder(
                identifier: "\(Self.prefix)\(reminder.taskId).\(reminder.remindAt)",
                title: title.isEmpty ? "Untitled task" : title,
                noteID: reminder.todoListId, date: date)
        }
        let wantedIDs = Set(wanted.map(\.identifier))
        let existing = Set(scheduled)
        toAdd = wanted.filter { !existing.contains($0.identifier) }
        toRemove = scheduled.filter { $0.hasPrefix(Self.prefix) && !wantedIDs.contains($0) }
    }
}

/// Keeps the system's scheduled notifications in step with the reminders on
/// tasks. The system delivers them whether or not the app is running.
@MainActor
final class ReminderScheduler: NSObject, UNUserNotificationCenterDelegate {
    private let model: AppModel
    private let center: UNUserNotificationCenter
    private var pending: Task<Void, Never>?

    /// `nil` when the process is not a bundled app. Notifications belong to
    /// a bundle, and asking for them without one stops the process.
    init?(model: AppModel) {
        guard Bundle.main.bundleIdentifier != nil else { return nil }
        self.model = model
        center = UNUserNotificationCenter.current()
        super.init()
        center.delegate = self
    }

    /// Sync soon. Changes arrive in bursts, and one sync covers a burst.
    func syncSoon() {
        pending?.cancel()
        pending = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(400))
            guard !Task.isCancelled else { return }
            await self?.sync()
        }
    }

    func sync() async {
        guard let reminders = try? model.store.listReminders() else { return }
        let scheduled = await center.pendingNotificationRequests().map(\.identifier)
        let plan = ReminderPlan(reminders: reminders, scheduled: scheduled, now: .now)
        center.removePendingNotificationRequests(withIdentifiers: plan.toRemove)
        guard !plan.toAdd.isEmpty else { return }
        // Asked the first time there is something to remind about, not at launch.
        guard (try? await center.requestAuthorization(options: [.alert, .sound])) == true else { return }
        for reminder in plan.toAdd {
            let content = UNMutableNotificationContent()
            content.title = reminder.title
            content.body = "Reminder"
            content.sound = .default
            content.userInfo = ["noteID": reminder.noteID]
            let parts = Calendar.current.dateComponents([.year, .month, .day, .hour, .minute, .second], from: reminder.date)
            let trigger = UNCalendarNotificationTrigger(dateMatching: parts, repeats: false)
            try? await center.add(UNNotificationRequest(identifier: reminder.identifier, content: content, trigger: trigger))
        }
    }

    nonisolated func userNotificationCenter(
        _ center: UNUserNotificationCenter, willPresent notification: UNNotification
    ) async -> UNNotificationPresentationOptions {
        // Show reminders while the app is in front, too.
        [.banner, .sound]
    }

    nonisolated func userNotificationCenter(
        _ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse
    ) async {
        guard let noteID = response.notification.request.content.userInfo["noteID"] as? String else { return }
        await MainActor.run { model.pendingLink = AppLink(noteID: noteID, blockID: nil) }
    }
}
