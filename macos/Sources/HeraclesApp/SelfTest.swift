import AppKit
import Foundation
import HeraclesCore

/// An end-to-end check of the parts no unit test reaches: the editor in a
/// window, in the built app, loading a document from the core and saving
/// edits back to it. Run with `Heracles --self-test`.
@MainActor
public enum SelfTest {
    public static func run() async -> Bool {
        var failures = 0
        func check(_ label: String, _ passed: Bool) {
            print("\(passed ? "PASS" : "FAIL"): \(label)")
            if !passed { failures += 1 }
        }

        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("heracles-self-test-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: directory) }

        do {
            let store = try Store(dataDir: directory.path)
            let model = try AppModel(store: store)
            let image = try store.storeImage(bytes: samplePicture(), mime: "image/png")
            let original = """
                {"type":"doc","content":[
                  {"type":"heading","attrs":{"blockId":"h1","level":1},"content":[{"type":"text","text":"Trip plan"}]},
                  {"type":"paragraph","attrs":{"blockId":"p1"},"content":[
                    {"type":"text","text":"Pack the "},{"type":"text","text":"lantern","marks":[{"type":"bold"}]}]},
                  {"type":"taskList","content":[{"type":"taskItem","attrs":{"blockId":"t1","checked":true},
                    "content":[{"type":"paragraph","attrs":{"blockId":"p2"},"content":[{"type":"text","text":"Book ferry"}]}]}]},
                  {"type":"codeMirrorBlock","attrs":{"blockId":"c1","language":"rust","code":"fn main() {}"}},
                  {"type":"calloutBlock","attrs":{"blockId":"k1","emoji":"🔥"},
                    "content":[{"type":"paragraph","attrs":{"blockId":"p3"},"content":[{"type":"text","text":"Hot"}]}]},
                  {"type":"columnBlock","content":[
                    {"type":"column","attrs":{"width":1},"content":[
                      {"type":"paragraph","attrs":{"blockId":"l1"},"content":[{"type":"text","text":"Left"}]}]},
                    {"type":"column","attrs":{"width":1},"content":[
                      {"type":"paragraph","attrs":{"blockId":"r0"},"content":[{"type":"text","text":"Right"}]}]}]},
                  {"type":"image","attrs":{"blockId":"i1","src":"\(image.url)","alt":"Map","title":null}}
                ]}
                """
            let page = try store.createNote(new: NewNote(title: "Self-test", content: original))

            let coordinator = EditorCoordinator(model: model)
            let editor = coordinator.editor
            let window = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 900, height: 700), styleMask: [.titled],
                backing: .buffered, defer: false)
            window.isReleasedWhenClosed = false
            window.contentView = editor.scrollView
            // Placed where no screen shows it.
            window.setFrameOrigin(NSPoint(x: -20_000, y: -20_000))
            window.orderBack(nil)
            coordinator.show(noteID: page.id, editable: true, change: nil)
            window.layoutIfNeeded()

            // What the editor holds must be the document the core stored: same
            // blocks, same ids, nothing dropped on the way in or out.
            let stored = try store.getDocument(noteId: page.id)?.content ?? ""
            check("the editor shows the stored document unchanged", sameJSON(editor.json, stored))
            check("every block has a view on the page", ["h1", "p1", "t1", "c1", "k1", "p3", "l1", "r0", "i1"].allSatisfy {
                (editor.page.frame(of: $0)?.height ?? 0) > 0
            })
            check("the text is drawn by the system's current text engine", editor.page.textView(for: "p1")?.textLayoutManager != nil)

            let pictureShown = await waitUntil {
                (editor.page.blockView(for: "i1") as? ImageBlockView)?.hasPicture == true
            }
            check("an image block shows its picture from the image store", pictureShown)

            // Opening a note is not an edit: nothing may have been saved yet.
            let opened = try store.getDocument(noteId: page.id)?.revision ?? -1
            check("opening a note saves nothing", opened == 1 && model.documentChange(for: page.id) == nil)

            editor.select(.caret("p1", 16))
            check("the caret is in the block's own text view", window.firstResponder === editor.page.textView(for: "p1"))
            editor.insertText(" typed-by-self-test")
            await coordinator.flush()
            check("an edit in the editor is saved to the core", try store.getDocument(noteId: page.id)?.revision == opened + 1)

            let content = try store.getDocument(noteId: page.id)?.content ?? ""
            check("the saved document holds the typed text", content.contains("lantern typed-by-self-test"))
            check("the saved document kept its block ids", content.contains("\"blockId\":\"c1\"") && content.contains("\"blockId\":\"h1\""))
            check("the edit is searchable", try store.searchNotes(query: "typed", limit: 5).map(\.note.id) == [page.id])
            check(
                "the editor's own save is attributed to it",
                model.documentChange(for: page.id) == DocumentChange(revision: opened + 1, origin: coordinator.editorID))

            // Typing pauses, and the note saves without being asked to.
            editor.insertText("!")
            let savedByItself = await waitUntil { (try? store.getDocument(noteId: page.id))??.revision == opened + 2 }
            check("a pause in typing saves the note", savedByItself)

            // The keys that cross from one block to the next.
            editor.select(.caret("h1", 4))
            editor.page.textView(for: "h1")?.doCommand(by: #selector(NSResponder.insertNewline(_:)))
            let split = editor.document.blocks.prefix(2).map(\.text.string)
            check("Return splits a block in two", split == ["Trip", " plan"] && editor.document.blocks[1].kind == .heading)
            (window.firstResponder as? NSTextView)?.doCommand(by: #selector(NSResponder.deleteBackward(_:)))
            (window.firstResponder as? NSTextView)?.doCommand(by: #selector(NSResponder.deleteBackward(_:)))
            check("Backspace at the start joins it back", editor.document.blocks[0].text.string == "Trip plan")

            // A save from elsewhere must replace what the editor shows.
            await coordinator.flush()
            let replacement = #"{"type":"doc","content":[{"type":"paragraph","attrs":{"blockId":"r1"},"content":[{"type":"text","text":"Replaced elsewhere"}]}]}"#
            _ = try store.saveDocument(noteId: page.id, content: replacement, origin: "another-window")
            coordinator.show(noteID: page.id, editable: true, change: model.documentChange(for: page.id))
            check("a save made elsewhere replaces what the editor shows", sameJSON(editor.json, replacement))

            // Pasted rich text is converted by the core, not taken as it comes.
            let board = NSPasteboard(name: NSPasteboard.Name("heracles-self-test-\(UUID().uuidString)"))
            defer { board.releaseGlobally() }
            board.clearContents()
            board.setString("<h6 onclick=\"x()\">Deep</h6><p>kept <a href=\"javascript:alert(1)\">bad</a></p><script>alert(1)</script>", forType: .html)
            editor.select(.caret("r1", 18))
            let pasted = editor.paste(from: board)
            let afterPaste = editor.json
            check(
                "pasted HTML is converted by the core's rules",
                pasted && afterPaste.contains("\"level\":4") && afterPaste.contains("kept")
                    && !afterPaste.contains("alert") && !afterPaste.contains("onclick"))

            editor.duplicate("r1")
            let texts = editor.document.blocks.map(\.text.string)
            let ids = Set(editor.document.allBlocks.map(\.id))
            // The original must keep its id, or links to it would break.
            check(
                "duplicating a block leaves the original's id alone",
                texts.prefix(2) == ["Replaced elsewhere", "Replaced elsewhere"] && editor.document.blocks[0].id == "r1"
                    && ids.count == editor.document.allBlocks.count)

            check("a block can be brought into view by its id", editor.reveal("r1"))
            editor.undo()
            editor.undo()
            check("undo takes the edits back", sameJSON(editor.json, replacement))
            check("nothing went wrong along the way", model.lastError == nil)
        } catch {
            check("self-test ran to completion (\(error))", false)
        }

        print(failures == 0 ? "\nSELF-TEST PASSED" : "\nSELF-TEST FAILED (\(failures))")
        return failures == 0
    }

    /// A real picture, small: a 4 by 4 blue square as PNG.
    private static func samplePicture() -> Data {
        let image = NSImage(size: NSSize(width: 4, height: 4), flipped: false) { rect in
            NSColor.systemBlue.setFill()
            rect.fill()
            return true
        }
        guard let tiff = image.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff),
            let png = bitmap.representation(using: .png, properties: [:])
        else { return Data() }
        return png
    }

    private static func waitUntil(timeout: Duration = .seconds(10), _ condition: () async -> Bool) async -> Bool {
        let deadline = ContinuousClock.now + timeout
        while ContinuousClock.now < deadline {
            if await condition() { return true }
            try? await Task.sleep(for: .milliseconds(50))
        }
        return await condition()
    }

    private static func sameJSON(_ a: String, _ b: String) -> Bool {
        guard let first = JSONValue(text: a), let second = JSONValue(text: b) else { return false }
        return first == second
    }
}
