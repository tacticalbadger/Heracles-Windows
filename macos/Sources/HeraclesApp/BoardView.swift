import HeraclesCore
import SwiftUI

/// A todo list as a board: columns of cards. Drag a card to another column,
/// or onto a card to land in front of it.
struct BoardView: View {
    @Environment(AppModel.self) private var model
    let todoList: Note
    @State private var columns: [TaskColumn] = []
    @State private var tasks: [TodoTask] = []
    @State private var newColumn = ""
    @State private var openTask: OpenTask?

    var body: some View {
        ScrollView(.horizontal) {
            HStack(alignment: .top, spacing: 12) {
                ForEach(columns, id: \.id) { column in
                    BoardColumnView(
                        column: column,
                        cards: tasks.filter { $0.columnId == column.id },
                        subtaskCounts: subtaskCounts,
                        canDelete: !todoList.trashed,
                        open: { openTask = OpenTask(id: $0) })
                }
                if !todoList.trashed {
                    TextField("New column", text: $newColumn)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 160)
                        .onSubmit {
                            model.addColumn(to: todoList.id, named: newColumn)
                            newColumn = ""
                        }
                }
            }
            .padding(.horizontal, 32)
            .padding(.vertical, 16)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .disabled(todoList.trashed)
        .sheet(item: $openTask) { task in
            TaskDetailView(todoListID: todoList.id, rootTaskID: task.id)
                .environment(model)
        }
        // Reloads on first showing, when another list is shown, and whenever
        // the change feed says this board changed, in any window.
        .task(id: BoardState(id: todoList.id, version: model.boardVersion(for: todoList.id))) {
            columns = model.columns(of: todoList.id)
            tasks = model.tasks(of: todoList.id)
        }
    }

    /// For each card, how many of its subtasks are done, and how many there are.
    private var subtaskCounts: [String: (done: Int, total: Int)] {
        var counts: [String: (done: Int, total: Int)] = [:]
        for task in tasks {
            guard let parent = task.parentTaskId else { continue }
            counts[parent, default: (0, 0)].total += 1
            if task.completed { counts[parent, default: (0, 0)].done += 1 }
        }
        return counts
    }
}

private struct OpenTask: Identifiable {
    let id: String
}

private struct BoardState: Hashable {
    let id: String
    let version: Int
}

private struct BoardColumnView: View {
    @Environment(AppModel.self) private var model
    let column: TaskColumn
    let cards: [TodoTask]
    let subtaskCounts: [String: (done: Int, total: Int)]
    let canDelete: Bool
    let open: (String) -> Void
    @State private var newCard = ""
    @State private var isTargeted = false

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text(column.name).font(.headline)
                Text("\(cards.count)").foregroundStyle(.secondary)
                Spacer()
            }
            .contextMenu {
                if canDelete {
                    Button("Delete Column", role: .destructive) { model.deleteColumn(column.id) }
                }
            }
            ForEach(cards, id: \.id) { card in
                BoardCardView(card: card, subtasks: subtaskCounts[card.id], open: { open(card.id) })
                    .onTapGesture { open(card.id) }
                    .draggable(card.id)
                    .dropDestination(for: String.self) { ids, _ in
                        guard let id = ids.first, id != card.id else { return false }
                        model.moveCard(id, to: column.id, before: card.id)
                        return true
                    }
            }
            TextField("Add a card", text: $newCard)
                .textFieldStyle(.plain)
                .onSubmit {
                    let title = newCard.trimmingCharacters(in: .whitespaces)
                    if !title.isEmpty { model.addCard(to: column.todoListId, column: column.id, titled: title) }
                    newCard = ""
                }
            Spacer(minLength: 0)
        }
        .padding(10)
        .frame(width: 260, alignment: .topLeading)
        .frame(minHeight: 120)
        .background(.quaternary.opacity(isTargeted ? 0.9 : 0.5), in: RoundedRectangle(cornerRadius: 10))
        .dropDestination(for: String.self) { ids, _ in
            guard let id = ids.first else { return false }
            model.moveCard(id, to: column.id)
            return true
        } isTargeted: {
            isTargeted = $0
        }
    }
}

private struct BoardCardView: View {
    @Environment(AppModel.self) private var model
    let card: TodoTask
    let subtasks: (done: Int, total: Int)?
    let open: () -> Void

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Toggle("Done", isOn: Binding(get: { card.completed }, set: { model.setCompleted(card.id, $0) }))
                .toggleStyle(.checkbox)
                .labelsHidden()
            VStack(alignment: .leading, spacing: 4) {
                Text(card.title.isEmpty ? "Untitled" : card.title)
                    .strikethrough(card.completed)
                    .foregroundStyle(card.completed ? .secondary : .primary)
                HStack(spacing: 8) {
                    // The short facts keep their width. Labels give way first.
                    Group {
                        if card.priority < 4 {
                            Label("P\(card.priority)", systemImage: "flag.fill")
                        }
                        if let due = card.dueDate {
                            Label(TaskDates.label(for: due), systemImage: "calendar")
                        }
                        if let subtasks {
                            Label("\(subtasks.done)/\(subtasks.total)", systemImage: "checklist")
                        }
                    }
                    .fixedSize()
                    ForEach(card.labels, id: \.self) { label in
                        Text(label)
                    }
                }
                .lineLimit(1)
                .font(.caption)
                .foregroundStyle(.secondary)
                .labelStyle(.titleAndIcon)
            }
            Spacer(minLength: 0)
        }
        .padding(8)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.background, in: RoundedRectangle(cornerRadius: 8))
        .shadow(color: .black.opacity(0.08), radius: 1, y: 1)
        .contextMenu {
            Button("Open") { open() }
            Button("Delete Card", role: .destructive) { model.deleteTask(card.id) }
        }
    }
}
