import AppKit
import HeraclesCore
import SwiftUI

/// Draws the app's window into image files, using a throwaway library of
/// sample notes. A way to look at the interface without a screen recording,
/// and without anything else on the screen ending up in the picture.
/// Run with `Heracles --snapshot <folder>`.
@MainActor
public enum Snapshot {
    public static func write(to folder: String) async -> Bool {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("heracles-snapshot-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: directory) }
        do {
            let store = try Store(dataDir: directory.path)
            let model = try AppModel(store: store)
            let (page, list) = try fill(store)
            try FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
            let file = { (name: String) in URL(fileURLWithPath: folder).appendingPathComponent("\(name).png") }
            for (name, selected) in [("page", page), ("board", list)] {
                try await draw(RootView(model: model, selecting: selected), to: file(name))
            }
            // A todo list's first card, as the sheet that opens on it.
            if let card = try store.listTasks(todoListId: list).first(where: { $0.parentTaskId == nil }) {
                try await draw(TaskDetailView(todoListID: list, rootTaskID: card.id).environment(model), to: file("task"))
            }
            // Two panes side by side, and the quick switcher over them.
            let workspace = Workspace(model: model)
            workspace.open(page)
            workspace.open(list, inSplit: .right)
            try await draw(
                PaneNodeView(node: workspace.tree.root, workspace: workspace).environment(model).frame(width: 1100, height: 700),
                to: file("split"))
            try await draw(SwitcherView(workspace: workspace).padding(40), to: file("switcher"))
            try await drawEditor(model: model, page: page, to: file)
            return true
        } catch {
            print("Snapshot failed: \(error)")
            return false
        }
    }

    /// A small library with one of everything. Returns a page and a todo list.
    private static func fill(_ store: Store) throws -> (page: String, list: String) {
        let notebook = try store.listNotes()[0]
        _ = try store.renameNote(id: notebook.id, title: "Personal")
        let section = try store.createNote(new: NewNote(kind: .section, parentId: notebook.id, title: "Travel"))
        let picture = try store.storeImage(bytes: samplePicture(), mime: "image/png")
        let page = try store.createNote(
            new: NewNote(parentId: section.id, title: "Trip plan", icon: "🧭", content: sampleDocument(picture: picture.url)))
        _ = try store.createNote(new: NewNote(parentId: page.id, title: "Packing list"))
        _ = try store.createNote(new: NewNote(parentId: section.id, title: "Ferry times"))
        _ = try store.setNoteFavorited(id: page.id, favorited: true)
        let archived = try store.createNote(new: NewNote(parentId: notebook.id, title: "Last year"))
        _ = try store.setNoteArchived(id: archived.id, archived: true)
        let trashed = try store.createNote(new: NewNote(parentId: notebook.id, title: "Old draft"))
        _ = try store.trashNote(id: trashed.id)
        _ = try store.createNote(new: NewNote(kind: .notebook, title: "Work"))

        let list = try store.createNote(new: NewNote(kind: .todoList, parentId: section.id, title: "Before we leave"))
        let columns = try store.listColumns(todoListId: list.id)
        let ferry = try store.createTask(new: NewTask(todoListId: list.id, columnId: columns[0].id, title: "Book the ferry"))
        _ = try store.updateTask(
            id: ferry.id,
            patch: TaskPatch(
                description: "Check the winter timetable first.",
                dueDate: .some("2026-12-01T14:00:00.000Z"), priority: 1, labels: ["travel", "tickets"],
                reminders: ["2026-11-30T09:00:00.000Z"], location: .some("Harbour office")))
        _ = try store.createComment(taskId: ferry.id, body: "The 9:40 sailing is the one with space for the car.")
        for (title, done) in [("Outbound", true), ("Return", false)] {
            let sub = try store.createTask(new: NewTask(todoListId: list.id, parentTaskId: ferry.id, title: title))
            _ = try store.updateTask(id: sub.id, patch: TaskPatch(completed: done))
        }
        _ = try store.createTask(new: NewTask(todoListId: list.id, columnId: columns[0].id, title: "Water the plants"))
        _ = try store.createTask(new: NewTask(todoListId: list.id, columnId: columns[1].id, title: "Pack the lantern"))
        let done = try store.createTask(new: NewTask(todoListId: list.id, columnId: columns[2].id, title: "Renew passport"))
        _ = try store.updateTask(id: done.id, patch: TaskPatch(completed: true))
        return (page.id, list.id)
    }

    /// The editor by itself, in the states a still picture can show: the
    /// block handle and the slash menu, and whole blocks selected in the dark.
    private static func drawEditor(model: AppModel, page: String, to file: (String) -> URL) async throws {
        for (name, dark) in [("editor-selection", true), ("editor-menu", false)] {
            let coordinator = EditorCoordinator(model: model)
            let editor = coordinator.editor
            let appearance = NSAppearance(named: dark ? .darkAqua : .aqua)
            try await draw(EditorHolder(scrollView: editor.scrollView).frame(width: 820, height: 900), to: file(name), appearance: appearance) {
                coordinator.show(noteID: page, editable: true, change: nil)
                if dark {
                    editor.select(.blocks(Array(editor.document.blocks.map(\.id)[2...4])))
                } else if let last = editor.document.blocks.last {
                    editor.select(.caret(last.id, last.text.length))
                    // One key at a time, as they are typed.
                    for key in " /he" { editor.insertText(String(key)) }
                    if let frame = editor.page.frame(of: editor.document.blocks[1].id) {
                        editor.page.showHandle(at: NSPoint(x: frame.midX, y: frame.midY))
                    }
                }
            }
        }
    }

    private struct EditorHolder: NSViewRepresentable {
        let scrollView: NSScrollView
        func makeNSView(context: Context) -> NSScrollView { scrollView }
        func updateNSView(_ view: NSScrollView, context: Context) {}
    }

    /// A picture to put in the sample note: a soft gradient with a sun.
    private static func samplePicture() -> Data {
        let image = NSImage(size: NSSize(width: 640, height: 220), flipped: false) { rect in
            NSGradient(starting: NSColor.systemTeal, ending: NSColor.systemIndigo)?.draw(in: rect, angle: 20)
            NSColor.systemYellow.setFill()
            NSBezierPath(ovalIn: NSRect(x: 470, y: 90, width: 90, height: 90)).fill()
            return true
        }
        guard let tiff = image.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff),
            let png = bitmap.representation(using: .png, properties: [:])
        else { return Data() }
        return png
    }

    /// One of every kind of block.
    private static func sampleDocument(picture: String) -> String {
        func text(_ string: String, _ marks: String...) -> String {
            let list = marks.map { $0.hasPrefix("http") ? #"{"type":"link","attrs":{"href":"\#($0)"}}"# : #"{"type":"\#($0)"}"# }
            return #"{"type":"text","text":"\#(string)""# + (list.isEmpty ? "" : #","marks":[\#(list.joined(separator: ","))]"#) + "}"
        }
        func block(_ type: String, _ attrs: String = "", _ content: String...) -> String {
            #"{"type":"\#(type)""# + (attrs.isEmpty ? "" : #","attrs":{\#(attrs)}"#)
                + (content.isEmpty ? "" : #","content":[\#(content.joined(separator: ","))]"#) + "}"
        }
        func para(_ parts: String...) -> String { #"{"type":"paragraph","content":[\#(parts.joined(separator: ","))]}"# }
        func item(_ type: String, _ attrs: String, _ parts: String...) -> String { block(type, attrs, parts.joined(separator: ",")) }
        let blocks = [
            block("heading", #""level":1"#, text("Three days on the coast")),
            para(
                text("We leave on "), text("Friday morning", "bold"), text(" and take the "), text("first ferry", "italic"),
                text(". The timetable is on the "), text("harbour site", "https://example.com/ferries"),
                text(", and the car booking code is "), text("FX-2291", "code"), text(". The old plan is "),
                text("off", "strike"), text(".")),
            block("heading", #""level":2"#, text("What to bring")),
            block(
                "bulletList", "",
                item("listItem", "", para(text("Tent and pegs")), block("bulletList", "", item("listItem", "", para(text("Spare guy lines"))))),
                item("listItem", "", para(text("Stove, gas and the lantern")))),
            block("orderedList", "", item("listItem", "", para(text("Load the car"))), item("listItem", "", para(text("Drive to the harbour")))),
            block(
                "taskList", "", item("taskItem", #""checked":true"#, para(text("Book the ferry"))),
                item("taskItem", #""checked":false"#, para(text("Water the plants")))),
            block("blockquote", "", para(text("The sea, once it casts its spell, holds one in its net of wonder forever."))),
            block("calloutBlock", #""emoji":"⚠️""#, para(text("The last ferry back is at 17:40. There is no later one in winter."))),
            block("toggleBlock", #""open":true"#, para(text("If it rains")), para(text("Museum in the morning, then the long lunch."))),
            block("heading", #""level":3"#, text("Costs")),
            block(
                "columnBlock", "",
                block("column", #""width":1"#, para(text("Ferry", "bold")), para(text("Car and two adults, return."))),
                block("column", #""width":1"#, para(text("Camping", "bold")), para(text("Two nights, pitch with a view.")))),
            block("codeMirrorBlock", #""language":"python","code":"total = ferry + 2 * pitch\nprint(round(total, 2))""#),
            block("horizontalRule"),
            block("image", #""src":"\#(picture)","alt":"The bay at dusk","title":null"#),
            para(text("More notes to come.")),
        ]
        return #"{"type":"doc","content":[\#(blocks.joined(separator: ","))]}"#
    }

    private static func draw(
        _ view: some View, to file: URL, appearance: NSAppearance? = nil, prepare: @MainActor () -> Void = {}
    ) async throws {
        defer { print("Wrote \(file.path)") }
        let frame = NSRect(x: 0, y: 0, width: 1100, height: 700)
        let window = NSWindow(
            contentRect: frame, styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        // Swift owns the window. AppKit must not also release it on close.
        window.isReleasedWhenClosed = false
        window.appearance = appearance
        // An offscreen drawing has no window behind it, so paint one in.
        let host = NSHostingView(rootView: view.background(Color(nsColor: .windowBackgroundColor)))
        window.contentView = host
        // Laid out as a real window, placed where no screen shows it.
        window.setFrameOrigin(NSPoint(x: -20_000, y: -20_000))
        window.orderBack(nil)
        try await Task.sleep(for: .seconds(1))
        prepare()
        try await Task.sleep(for: .seconds(1))

        guard let bitmap = host.bitmapImageRepForCachingDisplay(in: host.bounds) else {
            throw CocoaError(.fileWriteUnknown)
        }
        host.cacheDisplay(in: host.bounds, to: bitmap)
        guard let png = bitmap.representation(using: .png, properties: [:]) else {
            throw CocoaError(.fileWriteUnknown)
        }
        try png.write(to: file)
        window.close()
    }
}
