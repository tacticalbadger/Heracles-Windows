import HeraclesCore
import SwiftUI

/// Everything about one task: its text, dates, labels and reminders, its
/// subtasks, and the comments on it. Opening a subtask shows that subtask
/// in the same sheet, with a way back.
struct TaskDetailView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let todoListID: String
    let rootTaskID: String
    /// The subtasks drilled into, outermost first.
    @State private var path: [String] = []
    @State private var tasks: [TodoTask] = []
    @State private var columns: [TaskColumn] = []
    @State private var comments: [TaskComment] = []

    private var taskID: String { path.last ?? rootTaskID }
    private var task: TodoTask? { tasks.first { $0.id == taskID } }

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            if let task {
                ScrollView {
                    TaskFields(task: task, subtasks: tasks.filter { $0.parentTaskId == task.id }, columns: columns, comments: comments) {
                        path.append($0)
                    }
                    .padding(20)
                    // A different task gets fresh fields, not the last one's drafts.
                    .id(task.id)
                }
            } else {
                ContentUnavailableView("This task no longer exists", systemImage: "checklist")
            }
        }
        .frame(width: 560, height: 640)
        // Reloads when another task is shown and whenever the board changes,
        // in this window or any other.
        .task(id: Reload(task: taskID, version: model.boardVersion(for: todoListID))) {
            tasks = model.tasks(of: todoListID)
            columns = model.columns(of: todoListID)
            comments = model.comments(on: taskID)
            // The task, or one it was reached through, may have been deleted.
            if !tasks.contains(where: { $0.id == rootTaskID }) {
                dismiss()
            } else {
                path = Array(path.prefix { id in tasks.contains { $0.id == id } })
            }
        }
    }

    private var header: some View {
        HStack {
            if !path.isEmpty {
                Button("Back", systemImage: "chevron.left") { path.removeLast() }
            }
            // The chain of tasks above this one.
            Text(([rootTaskID] + path).dropLast().compactMap { id in tasks.first { $0.id == id }?.title }.joined(separator: " › "))
                .font(.caption)
                .foregroundStyle(.secondary)
                .lineLimit(1)
            Spacer()
            if let task {
                Button("Delete", systemImage: "trash", role: .destructive) {
                    model.deleteTask(task.id)
                    if path.isEmpty { dismiss() } else { path.removeLast() }
                }
            }
            Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
        }
        .padding(12)
    }

    private struct Reload: Hashable {
        let task: String
        let version: Int
    }
}

/// The editable fields of one task. Each change is saved as it is made.
private struct TaskFields: View {
    @Environment(AppModel.self) private var model
    let task: TodoTask
    let subtasks: [TodoTask]
    let columns: [TaskColumn]
    let comments: [TaskComment]
    let openSubtask: (String) -> Void

    @State private var title = ""
    @State private var notes = ""
    @State private var location = ""
    @State private var newLabel = ""
    @State private var newSubtask = ""
    @State private var newComment = ""
    @State private var newReminder = Date.now.addingTimeInterval(3600)
    @State private var editing: TaskComment?
    @State private var editedBody = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(alignment: .firstTextBaseline) {
                Toggle("Done", isOn: Binding(get: { task.completed }, set: { model.setCompleted(task.id, $0) }))
                    .toggleStyle(.checkbox)
                    .labelsHidden()
                TextField("Task", text: $title, axis: .vertical)
                    .textFieldStyle(.plain)
                    .font(.title2.bold())
                    .onSubmit { save(title: title) }
            }
            section("Notes") {
                TextEditor(text: $notes)
                    .font(.body)
                    .frame(minHeight: 70)
                    .scrollContentBackground(.hidden)
                    .padding(6)
                    .background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 6))
            }
            Grid(alignment: .leadingFirstTextBaseline, horizontalSpacing: 12, verticalSpacing: 10) {
                GridRow {
                    Text("Priority").foregroundStyle(.secondary)
                    Picker("Priority", selection: Binding(get: { task.priority }, set: { model.updateTask(task.id, TaskPatch(priority: $0)) })) {
                        ForEach(UInt8(1)...4, id: \.self) { Text("P\($0)").tag($0) }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .frame(width: 200)
                }
                dateRow("Due", stored: task.dueDate) { day in
                    model.updateTask(task.id, TaskPatch(dueDate: .some(day.map { TaskDates.dueDate(on: $0) })))
                }
                dateRow("Deadline", stored: task.deadline) { day in
                    model.updateTask(task.id, TaskPatch(deadline: .some(day.map { TaskDates.deadline(on: $0) })))
                }
                GridRow {
                    Text("Location").foregroundStyle(.secondary)
                    TextField("None", text: $location)
                        .textFieldStyle(.roundedBorder)
                        .onSubmit(saveLocation)
                }
                if task.parentTaskId != nil, !columns.isEmpty {
                    GridRow {
                        Text("Move").foregroundStyle(.secondary)
                        // A subtask can become a card of its own.
                        Menu("Make it a card in…") {
                            ForEach(columns, id: \.id) { column in
                                Button(column.name) { model.moveCard(task.id, to: column.id) }
                            }
                        }
                        .fixedSize()
                    }
                }
            }
            section("Labels") {
                FlowRow {
                    ForEach(task.labels, id: \.self) { label in
                        chip(label) { model.updateTask(task.id, TaskPatch(labels: task.labels.filter { $0 != label })) }
                    }
                    TextField("Add a label", text: $newLabel)
                        .textFieldStyle(.plain)
                        .frame(width: 110)
                        .onSubmit(addLabel)
                }
            }
            section("Reminders") {
                ForEach(task.reminders, id: \.self) { reminder in
                    HStack {
                        Image(systemName: "bell")
                        Text(TaskDates.date(from: reminder)?.formatted(date: .abbreviated, time: .shortened) ?? reminder)
                        Spacer()
                        removeButton { model.updateTask(task.id, TaskPatch(reminders: task.reminders.filter { $0 != reminder })) }
                    }
                }
                HStack {
                    DatePicker("New reminder", selection: $newReminder).labelsHidden()
                    Button("Add") {
                        let stored = TaskDates.string(from: newReminder)
                        if !task.reminders.contains(stored) {
                            model.updateTask(task.id, TaskPatch(reminders: task.reminders + [stored]))
                        }
                    }
                }
            }
            section("Subtasks") {
                ForEach(subtasks, id: \.id) { subtask in
                    HStack {
                        Toggle("Done", isOn: Binding(get: { subtask.completed }, set: { model.setCompleted(subtask.id, $0) }))
                            .toggleStyle(.checkbox)
                            .labelsHidden()
                        Button {
                            openSubtask(subtask.id)
                        } label: {
                            Text(subtask.title.isEmpty ? "Untitled" : subtask.title)
                                .strikethrough(subtask.completed)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        if let due = subtask.dueDate {
                            Text(TaskDates.label(for: due)).font(.caption).foregroundStyle(.secondary)
                        }
                        removeButton { model.deleteTask(subtask.id) }
                    }
                }
                TextField("Add a subtask", text: $newSubtask)
                    .textFieldStyle(.plain)
                    .onSubmit {
                        let text = newSubtask.trimmingCharacters(in: .whitespaces)
                        if !text.isEmpty { model.addSubtask(to: task, titled: text) }
                        newSubtask = ""
                    }
            }
            section("Comments") {
                ForEach(comments, id: \.id) { comment in
                    VStack(alignment: .leading, spacing: 2) {
                        Text(comment.body).textSelection(.enabled)
                        Text(TaskDates.date(from: comment.createdAt)?.formatted(date: .abbreviated, time: .shortened) ?? "")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .contextMenu {
                        Button("Edit…") {
                            editedBody = comment.body
                            editing = comment
                        }
                        Button("Delete", role: .destructive) { model.deleteComment(comment.id) }
                    }
                }
                TextField("Add a comment", text: $newComment, axis: .vertical)
                    .textFieldStyle(.roundedBorder)
                    .onSubmit {
                        let text = newComment.trimmingCharacters(in: .whitespacesAndNewlines)
                        if !text.isEmpty { model.addComment(to: task.id, text) }
                        newComment = ""
                    }
            }
        }
        .onAppear {
            title = task.title
            notes = task.description
            location = task.location ?? ""
        }
        // Text is saved when the sheet moves on from it, and when it closes.
        .onDisappear {
            save(title: title)
            saveNotes()
            saveLocation()
        }
        .onChange(of: notes) { saveNotesSoon() }
        .alert("Edit comment", isPresented: Binding(get: { editing != nil }, set: { if !$0 { editing = nil } }), presenting: editing) { comment in
            TextField("Comment", text: $editedBody)
            Button("Save") { model.updateComment(comment.id, editedBody) }
            Button("Cancel", role: .cancel) {}
        }
    }

    @State private var notesSave: Task<Void, Never>?

    private func save(title: String) {
        if title != task.title { model.renameTask(task.id, to: title) }
    }

    private func saveNotes() {
        notesSave?.cancel()
        if notes != task.description { model.updateTask(task.id, TaskPatch(description: notes)) }
    }

    /// Notes are saved once typing pauses, not on every key.
    private func saveNotesSoon() {
        notesSave?.cancel()
        notesSave = Task {
            try? await Task.sleep(for: .milliseconds(600))
            if !Task.isCancelled { saveNotes() }
        }
    }

    private func saveLocation() {
        let trimmed = location.trimmingCharacters(in: .whitespaces)
        if trimmed != (task.location ?? "") {
            model.updateTask(task.id, TaskPatch(location: .some(trimmed.isEmpty ? nil : trimmed)))
        }
    }

    private func addLabel() {
        let label = newLabel.trimmingCharacters(in: .whitespaces)
        if !label.isEmpty, !task.labels.contains(label) {
            model.updateTask(task.id, TaskPatch(labels: task.labels + [label]))
        }
        newLabel = ""
    }

    private func section(_ name: String, @ViewBuilder content: () -> some View) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(name).font(.headline)
            content()
        }
    }

    /// A row for a date that may not be set: a date picker once it is, and
    /// a button to set it until then.
    private func dateRow(_ name: String, stored: String?, set: @escaping (Date?) -> Void) -> some View {
        GridRow {
            Text(name).foregroundStyle(.secondary)
            HStack {
                if let date = stored.flatMap({ TaskDates.date(from: $0) }) {
                    DatePicker(name, selection: Binding(get: { date }, set: { set($0) }), displayedComponents: .date)
                        .labelsHidden()
                    removeButton { set(nil) }
                } else {
                    Button("Set a date") { set(.now) }
                }
            }
        }
    }

    private func chip(_ text: String, remove: @escaping () -> Void) -> some View {
        HStack(spacing: 4) {
            Text(text)
            removeButton(remove)
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 3)
        .background(.quaternary, in: Capsule())
    }

    private func removeButton(_ action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary)
        }
        .buttonStyle(.plain)
        .help("Remove")
    }
}

/// Lays its children out in a row, starting a new row when one is full.
private struct FlowRow: Layout {
    var spacing: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let rows = rows(in: proposal.width ?? .infinity, subviews)
        let height = rows.map(\.height).reduce(0, +) + spacing * CGFloat(max(0, rows.count - 1))
        return CGSize(width: proposal.width ?? rows.map(\.width).max() ?? 0, height: height)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var y = bounds.minY
        for row in rows(in: bounds.width, subviews) {
            var x = bounds.minX
            for index in row.items {
                let size = subviews[index].sizeThatFits(.unspecified)
                subviews[index].place(at: CGPoint(x: x, y: y + (row.height - size.height) / 2), proposal: .unspecified)
                x += size.width + spacing
            }
            y += row.height + spacing
        }
    }

    private func rows(in width: CGFloat, _ subviews: Subviews) -> [(items: [Int], width: CGFloat, height: CGFloat)] {
        var rows: [(items: [Int], width: CGFloat, height: CGFloat)] = []
        for index in subviews.indices {
            let size = subviews[index].sizeThatFits(.unspecified)
            if let last = rows.last, last.width + spacing + size.width <= width {
                rows[rows.count - 1] = (last.items + [index], last.width + spacing + size.width, max(last.height, size.height))
            } else {
                rows.append(([index], size.width, size.height))
            }
        }
        return rows
    }
}
