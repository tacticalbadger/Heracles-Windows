import Foundation
import HeraclesCore
import Testing

@testable import HeraclesApp

struct TaskDatesTests {
    private let utc = TimeZone(identifier: "UTC")!
    private let newYork = TimeZone(identifier: "America/New_York")!

    @Test func storedTimesAreReadWithOrWithoutFractionsOfASecond() {
        let expected = Date(timeIntervalSince1970: 1_796_029_200)  // 2026-11-30 09:00:00 UTC
        #expect(TaskDates.date(from: "2026-11-30T09:00:00.000Z") == expected)
        #expect(TaskDates.date(from: "2026-11-30T09:00:00Z") == expected)
        #expect(TaskDates.date(from: "nonsense") == nil)
        #expect(TaskDates.date(from: "") == nil)
    }

    @Test func aBareDateIsReadAsThatDay() {
        let date = TaskDates.date(from: "2026-12-01", in: utc)
        #expect(date == Date(timeIntervalSince1970: 1_796_083_200))  // 2026-12-01 00:00 UTC
    }

    @Test func timesAreWrittenTheWayTheElectronAppWroteThem() {
        let date = Date(timeIntervalSince1970: 1_796_029_200.25)
        #expect(TaskDates.string(from: date) == "2026-11-30T09:00:00.250Z")
        #expect(TaskDates.date(from: TaskDates.string(from: date)) == date)
    }

    @Test func aDueDateIsNineInTheMorningAndADeadlineFiveInTheAfternoonLocalTime() {
        let day = TaskDates.date(from: "2026-12-01T15:30:00Z")!  // 10:30 in New York
        #expect(TaskDates.dueDate(on: day, in: newYork) == "2026-12-01T14:00:00.000Z")
        #expect(TaskDates.deadline(on: day, in: newYork) == "2026-12-01T22:00:00.000Z")
    }
}

struct ReminderPlanTests {
    private let now = Date(timeIntervalSince1970: 1_796_029_200)  // 2026-11-30 09:00 UTC

    private func reminder(_ task: String, _ at: String, title: String = "Task") -> Reminder {
        Reminder(taskId: task, todoListId: "list", taskTitle: title, remindAt: at)
    }

    @Test func futureRemindersAreScheduledAndPastOnesAreNot() {
        let plan = ReminderPlan(
            reminders: [
                reminder("t1", "2026-11-30T10:00:00.000Z", title: "Call"),
                reminder("t2", "2026-11-30T08:00:00.000Z"),
                reminder("t3", "not a date"),
            ],
            scheduled: [], now: now)

        #expect(plan.toAdd.map(\.identifier) == ["heracles.reminder.t1.2026-11-30T10:00:00.000Z"])
        #expect(plan.toAdd[0].title == "Call")
        #expect(plan.toAdd[0].noteID == "list")
        #expect(plan.toAdd[0].date == Date(timeIntervalSince1970: 1_796_032_800))
        #expect(plan.toRemove.isEmpty)
    }

    @Test func whatIsAlreadyScheduledIsLeftAloneAndWhatIsNoLongerWantedIsRemoved() {
        let kept = "heracles.reminder.t1.2026-11-30T10:00:00.000Z"
        let stale = "heracles.reminder.t9.2026-11-30T11:00:00.000Z"
        let plan = ReminderPlan(
            reminders: [reminder("t1", "2026-11-30T10:00:00.000Z")],
            scheduled: [kept, stale, "someone.elses.notification"], now: now)

        #expect(plan.toAdd.isEmpty)
        #expect(plan.toRemove == [stale], "only this app's reminders are ever removed")
    }

    @Test func anUntitledTaskStillSaysSomething() {
        let plan = ReminderPlan(reminders: [reminder("t1", "2026-11-30T10:00:00.000Z", title: " ")], scheduled: [], now: now)
        #expect(plan.toAdd[0].title == "Untitled task")
    }
}
