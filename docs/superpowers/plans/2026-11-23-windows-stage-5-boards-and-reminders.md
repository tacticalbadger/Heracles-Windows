# Heracles for Windows, Stage 5: Boards, Task Details and Reminders — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A todo list opens as a kanban board, a card opens as a task with everything the Mac's task has, and a task's reminders are delivered by Windows whether or not the app is running.

**Architecture:** As on the Mac: board actions are one write each in the core (`AppModel.Boards.cs`), and a board reloads when the change feed says its todo list changed (`AppModel.BoardVersion`). What a view decides is in a class with no view in it (`Board`, `TaskDetail`, `TaskDates`, `ReminderPlan`), tested against a real core; the views (`BoardView`, `TaskDetailWindow`) only draw and call. Reminders go to Windows as scheduled toasts through one small interface, so the plan of what to add and remove is tested without Windows.

**Tech Stack:** C#, .NET 10, WPF, `Windows.UI.Notifications` (scheduled toasts), the registry under `HKCU`, xunit.

**Spec:** `docs/superpowers/specs/2026-11-22-native-windows-app-design.md` ("Boards and tasks", "Reminders", "Links"; stage 5 of "Order of work"). Read with `CLAUDE.md`, "Scheduled toasts".

## Global Constraints

- The Mac sources at `..\Heracles` (commit `5dd0252`) decide behaviour: `BoardView.swift`, `TaskDetailView.swift`, `TaskSupport.swift`, `AppModel+Boards.swift`, and `Tests/HeraclesAppTests/TaskSupportTests.swift` (7 tests).
- Dates are stored as the Mac stores them: UTC, ISO 8601 with milliseconds (`2026-11-30T09:00:00.000Z`). A due date is 09:00 local on the chosen day, a deadline 17:00.
- A reminder's identifier is `heracles.reminder.<taskId>.<remindAt>`. Only reminders this app scheduled are ever removed.
- The application identity is `com.heracles.notes.native`. The app writes its own registry keys under `HKCU` at start: the identity, and the `heracles` link scheme pointing at the running copy. **Neither is written, and nothing is scheduled, when the library is a test library** (`HERACLES_DATA_DIR` set): a test build must not take the owner's links or put toasts on his screen.
- A toast is `activationType="protocol"` with a `heracles://note/<todoListId>` link, so a click needs no code of the app's running.
- Tests never show a window, take the keyboard, touch the clipboard, write outside a throwaway registry key, or schedule a toast. The one test that talks to Windows' notification platform is opt-in (`HERACLES_TEST_TOASTS=1`).
- The Mac's board has no way to rename or reorder a column, and the design asks for both: a column's menu has Rename, Move Left and Move Right as well as Delete.
- Commit locally at the end of each task, as `tacticalbadger`, message ending `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`. Do not push. Update `CLAUDE.md` in every commit. Builds use `tools\build.ps1`.

## Review Focus

1. **A board open in two windows.** Expected: a card moved, ticked or renamed in one shows in the other. Task 1 (the model), Task 2 (the scripted check).
2. **A task deleted while its detail window is open**, here or in another window. Expected: the window closes, or goes back up to a task that still exists. Task 1.
3. **Typing in a task's notes while the board reloads.** Expected: what is being typed is not replaced by what is stored. Task 2.
4. **Reminders across a change of clock or zone, and ones already past.** Expected: stored instants are compared as instants; a past reminder is never scheduled. Task 1.
5. **Dragging a card.** No test can. The code is the pattern the tabs use; it stays under "Not verified".

---

### Task 1: Dates, the reminder plan, and what a board and a task decide

**Files:** Create `app/Heracles/Model/AppModel.Boards.cs`, `app/Heracles/Boards/TaskSupport.cs` (`TaskDates`, `PlannedReminder`, `ReminderPlan`), `app/Heracles/Boards/Board.cs`, `app/Heracles/Boards/TaskDetail.cs`, `app/Heracles/Boards/ReminderScheduler.cs` (`IReminderCenter`, `ReminderScheduler`), `app/Heracles/Support/LinkRegistration.cs`; Test `app/Heracles.Tests/TaskSupportTests.cs`, `BoardTests.cs`, `ReminderSchedulerTests.cs`.

**Interfaces (produces):**
- `AppModel`: `Columns(todoListId)`, `Tasks(todoListId)`, `AddColumn`, `RenameColumn`, `DeleteColumn`, `MoveColumn(todoListId, columnId, int by)`, `AddCard(todoListId, columnId, title)`, `SetCompleted`, `RenameTask`, `MoveCard(taskId, columnId, beforeId = null)`, `DeleteTask`, `UpdateTask(taskId, TaskPatch)`, `AddSubtask(TodoTask parent, title)`, `Comments(taskId)`, `AddComment`, `UpdateComment`, `DeleteComment`.
- `static class TaskDates`: `DateTimeOffset? Parse(string text, TimeZoneInfo? zone = null)`, `string Format(DateTimeOffset date)`, `DateTimeOffset At(DateOnly day, TimeOnly time, zone)`, `string DueDate(DateOnly|DateTimeOffset day, zone)`, `string Deadline(...)`, `DateOnly? Day(string stored, zone)`, `string Label(string stored, now, culture, zone)`, `string DateAndTime(string stored, culture, zone)`, `TimeOnly? TimeOfDay(string typed, culture)`.
- `record PlannedReminder(string Identifier, string Title, string NoteId, DateTimeOffset Date)`; `sealed class ReminderPlan(IEnumerable<Reminder> reminders, IEnumerable<string> scheduled, DateTimeOffset now)` with `ToAdd`, `ToRemove`, `const Prefix`.
- `sealed class Board(AppModel model, string todoListId)`: `Columns`, `Tasks`, `Reload()`, `Cards(columnId)`, `Subtasks(taskId)` (done and total, or null), `Facts(card, now, culture, zone)` (`CardFact(Kind, Text)`), `AddColumn(name)`, `bool AddCard(columnId, title)`, `CanMoveColumn(columnId, by)`, `MoveColumn(columnId, by)`, `bool Drop(taskId, columnId, beforeId)`.
- `sealed class TaskDetail(AppModel model, string todoListId, string rootTaskId, later)`: `RootTaskId`, `TaskId`, `Task`, `Subtasks`, `Columns`, `Comments`, `Breadcrumb`, `CanGoBack`, `CanMakeCard`, `Notes`, `bool Reload()` (false when the root task has gone), `Open(subtaskId)`, `Back()`, `Close()`, `bool Delete()` (true when the window should close), and the edits `SetCompleted(bool)`, `SetCompleted(taskId, bool)`, `SetTitle`, `SetLocation`, `SetPriority`, `SetDue(DateOnly? day)`, `SetDeadline(DateOnly? day)`, `AddLabel`, `RemoveLabel`, `bool AddReminder(DateTimeOffset)`, `RemoveReminder`, `TypeNotes(text)` (saved `NotesDelay` after typing pauses), `SaveNotes()`, `AddSubtask`, `DeleteTask(subtaskId)`, `MakeCard(columnId)`, `AddComment`, `EditComment`, `DeleteComment`.
- `interface IReminderCenter { IReadOnlyList<string> Scheduled(); void Remove(IReadOnlyList<string> identifiers); void Add(PlannedReminder reminder); }`; `ReminderScheduler(AppModel model, IReminderCenter center, Action<Action, TimeSpan>? later = null, Func<DateTimeOffset>? now = null)` with `Sync()` and `SyncSoon()`.
- `static class LinkRegistration`: `Register(RegistryKey classes, string exePath)`, `IsRegistered(RegistryKey classes, string exePath)`. `static class AppIdentity`: `const Id`, `Register(classes)`, `IsRegistered(classes)`, `bool Claim()`. `static class ReminderToast`: `Link(reminder)`, `Xml(reminder)`, `Identifier(xml)`, `Tag(identifier)`. `AppPaths.IsTestLibrary`.

- [ ] **Step 1:** Tests: the Mac's `TaskDatesTests` (4) and `ReminderPlanTests` (3); a board against a real core (columns added, renamed, moved, deleted; cards added, ticked, moved before another and to the end of another column; subtask counts; a second board on the same list sees it after `Reload`); a task (drilling in and back, the breadcrumb, a deleted task and a deleted root, every field edit with the Mac's trimming and no duplicates, a subtask made a card); the scheduler with a made-up centre (adds what is to come, removes what is no longer wanted, leaves others' alone, one sync for a burst); the link scheme written under a throwaway key.
- [ ] **Step 2:** Run; expected: does not compile.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** `tools\build.ps1 app test`; all pass. Commit: `Boards: dates, reminders planned, and what a board and a task decide`.

### Task 2: The board, the task window, and reminders handed to Windows

**Files:** Create `app/Heracles/Boards/BoardView.cs`, `app/Heracles/Boards/TaskDetailWindow.cs`, `app/Heracles/Boards/Fields.cs`, `app/Heracles/Boards/ToastCenter.cs`; Modify `Notes/NoteDetailView.cs` (a todo list shows its board), `App.xaml.cs` (the identity, the link scheme, the scheduler), `Support/E2eSeed.cs` (a board with cards); Create `tools/e2e/check-boards.ps1`, `tools/e2e/look-boards.ps1`; Test `app/Heracles.Tests/ToastCenterTests.cs` (opt-in).

**Interfaces (produces):** automation ids. Board: `Board:<leafId>`, `Column:<columnId>` (named by the column), `ColumnName:<columnId>`, `ColumnMenu:<columnId>` then `ColumnLeft:`, `ColumnRight:`, `ColumnDelete:<columnId>`, `NewColumn:<leafId>`, `NewCard:<columnId>`, `Card:<taskId>` (named by the card; invoking it opens the task; its item status is the card in words), `CardDone:<taskId>`. Task window: `TaskDetail`, `TaskFields`, `TaskTitle`, `TaskDone`, `TaskNotes`, `TaskPriority:<n>`, `TaskDueSet`, `TaskDue`, `TaskDueClear` (and the same for `Deadline`), `TaskLocation`, `TaskMakeCard` then `MakeCard:<columnId>`, `TaskNewLabel`, `Label:<text>`, `LabelRemove:<text>`, `TaskReminderDay`, `TaskReminderTime`, `TaskAddReminder`, `TaskReminderProblem`, `Reminder:<stored>`, `ReminderRemove:<stored>`, `TaskNewSubtask`, `Subtask:<taskId>`, `SubtaskDone:<taskId>`, `SubtaskRemove:<taskId>`, `TaskNewComment`, `Comment:<commentId>`, `CommentEdit:`, `CommentDelete:<commentId>`, `TaskPath`, `TaskBack`, `TaskDelete`, `TaskClose`. A box for something new has a button `Add:<the box's id>` once it holds text.

- [ ] **Step 1:** `check-boards.ps1`, through automation only: the seeded board shows its columns and cards; a column and a card are added; a card is ticked; its task opens, is renamed, given a priority, a label, a subtask and a comment; the subtask is opened and the way back works; the board shows the facts on the card; a second pane on the same list follows; it is all there after a restart; a list in the trash cannot be changed.
- [ ] **Step 2:** Run; expected: fails (a todo list still says its board is to come).
- [ ] **Step 3:** Implement. A text box that takes a new column, card, label, subtask or comment hands it over on Enter, or by a button that appears at its end once it holds text. (First written as "at once when it is changed without having the keyboard". In the window, UI Automation's set-value brings the keyboard into the box before the text, so that never happens.)
- [ ] **Step 4:** `tools\build.ps1 app test`, all three e2e scripts, and pictures of the board and of a task in dark and in light, looked at. Run the opt-in toast test once and record what it showed. Commit: `Boards: the board, the task window, and reminders handed to Windows`.

### Task 3: Verify the stage

- [ ] **Step 1:** `tools\build.ps1 clean`, `tools\build.ps1`, `tools\build.ps1 test`, the e2e scripts; expected: all pass, no warnings.
- [ ] **Step 2:** Update `CLAUDE.md` (Layout, the parity checklist, Not verified, Known faults, Current state). Commit: `Stage 5 verified: boards, tasks and reminders`.
