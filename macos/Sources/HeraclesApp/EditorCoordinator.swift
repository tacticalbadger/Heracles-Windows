import AppKit
import Foundation
import HeraclesCore
import SwiftUI

/// The app's end of one note editor. It gives the editor the note to show,
/// saves what the user changes, and answers what the editor asks of the app:
/// keeping images, converting pasted text, opening links.
///
/// The document lives in the core. The editor holds a working copy while a
/// note is open, and the copy is replaced whenever someone else saves that
/// note: another window, or another pane of this one.
@MainActor
final class EditorCoordinator: NSObject, BlockEditorHost {
    let editor = BlockEditorController()
    /// Passed with every save, and compared against the change feed, so this
    /// editor never reloads because of its own typing.
    let editorID = UUID().uuidString
    /// Called when the editor takes the keyboard focus.
    var onFocus: (() -> Void)?

    private let model: AppModel
    private var shown: (noteID: String, editable: Bool, revision: Int64)?
    private var revealed: String?
    private var isDirty = false
    private var pendingSave: Task<Void, Never>?
    private var lastWrite: Task<Void, Never>?
    private static let images = NSCache<NSString, NSImage>()
    /// How long typing must pause before the note is saved.
    private static let saveDelay: Duration = .milliseconds(300)

    init(model: AppModel) {
        self.model = model
        super.init()
        editor.host = self
        model.register(self)
    }

    /// Show a note, or catch up with a save someone else made to the one
    /// already showing.
    ///
    /// SwiftUI calls this while it is updating its views, when the model
    /// must not be changed. So nothing is saved from here directly.
    func show(noteID: String, editable: Bool, change: DocumentChange?) {
        if let shown, shown.noteID == noteID {
            if shown.editable != editable {
                editor.isEditable = editable
                self.shown?.editable = editable
            }
            let isNewer = (change?.revision ?? 0) > shown.revision
            guard isNewer, change?.origin != editorID else { return }
            // What is being typed here is not dropped for a copy saved
            // elsewhere. It is saved in a moment, and the other editor
            // then follows this one.
            guard !isDirty else { return }
        } else {
            saveAfterUpdate()
            revealed = nil
        }
        guard let document = model.document(for: noteID) else { return }
        let isAnotherNote = shown?.noteID != noteID
        editor.isEditable = editable
        editor.load(json: document.content)
        shown = (noteID, editable, document.revision)
        if isAnotherNote { editor.scrollToTop() }
    }

    /// Bring a block into view. Once per block: asking again for the same
    /// one does nothing, so a view update cannot keep scrolling the page.
    func reveal(_ blockID: String?, then done: @escaping () -> Void) {
        guard let blockID, blockID != revealed, shown != nil else { return }
        revealed = blockID
        // After this pass of layout, when the block has its place.
        DispatchQueue.main.async { [editor] in
            editor.reveal(blockID)
            done()
        }
    }

    /// Save now what the editor is holding, if anything.
    func save() {
        pendingSave?.cancel()
        pendingSave = nil
        guard isDirty, let shown else { return }
        isDirty = false
        model.saveDocument(shown.noteID, content: editor.json, origin: editorID)
        if let revision = model.documentChange(for: shown.noteID)?.revision {
            self.shown?.revision = revision
        }
    }

    /// Save what the editor is holding once the view update in progress is
    /// over. For leaving a note, and for the editor going away.
    func saveAfterUpdate() {
        pendingSave?.cancel()
        pendingSave = nil
        guard isDirty, let shown else { return }
        isDirty = false
        let content = editor.json
        lastWrite = Task { [model, editorID] in
            model.saveDocument(shown.noteID, content: content, origin: editorID)
        }
    }

    /// Save, and wait for any save still on its way. The app's quit
    /// sequence waits on this.
    func flush() async {
        save()
        await lastWrite?.value
    }

    var shownNoteID: String? { shown?.noteID }

    // MARK: What the editor asks of the app

    func editorDidChange(_ editor: BlockEditorController) {
        isDirty = true
        pendingSave?.cancel()
        pendingSave = Task { [weak self] in
            try? await Task.sleep(for: Self.saveDelay)
            guard !Task.isCancelled else { return }
            self?.save()
        }
    }

    func editorDidFocus(_ editor: BlockEditorController) {
        onFocus?()
    }

    func editor(_ editor: BlockEditorController, openLink href: String) {
        guard let url = URL(string: href) else { return }
        switch NavigationPolicy.decide(url) {
        case .openExternally: NSWorkspace.shared.open(url)
        case .openInApp: model.pendingLink = AppLink(url)
        case .deny: break
        }
    }

    func editor(_ editor: BlockEditorController, storeImage data: Data, mime: String) -> String? {
        model.attempt { try model.store.storeImage(bytes: data, mime: mime) }?.url
    }

    func editor(_ editor: BlockEditorController, documentFromHTML html: String) -> String? {
        // The core converts by the rules it uses for imports, so nothing but
        // the editor's own blocks and safe links can come in by the clipboard.
        htmlToDocument(html: html)
    }

    func editor(_ editor: BlockEditorController, copyLinkToBlock id: String) {
        guard let noteID = shown?.noteID else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(AppLink(noteID: noteID, blockID: id).url.absoluteString, forType: .string)
    }

    func editor(_ editor: BlockEditorController, loadImage source: String, completion: @escaping @MainActor (NSImage?) -> Void) {
        if let cached = Self.images.object(forKey: source as NSString) {
            completion(cached)
            return
        }
        func finish(_ image: NSImage?) {
            if let image { Self.images.setObject(image, forKey: source as NSString) }
            completion(image)
        }
        guard let url = URL(string: source), let scheme = url.scheme?.lowercased() else {
            completion(nil)
            return
        }
        switch scheme {
        case imageUrlScheme():
            // The image store keeps each picture in a file of its own.
            let path = try? model.store.imagePath(hashOrUrl: source)
            finish(path.flatMap { $0 }.flatMap { NSImage(contentsOfFile: $0) })
        case "data":
            guard let comma = source.firstIndex(of: ","), source[..<comma].lowercased().hasSuffix(";base64"),
                let data = Data(base64Encoded: String(source[source.index(after: comma)...]), options: .ignoreUnknownCharacters)
            else {
                completion(nil)
                return
            }
            finish(NSImage(data: data))
        case "http", "https":
            // A picture a page linked to on the web, as the note was imported.
            Task {
                let loaded = try? await URLSession(configuration: .ephemeral).data(from: url)
                finish(loaded.flatMap { $0.0.count <= 32 * 1024 * 1024 ? NSImage(data: $0.0) : nil })
            }
        default:
            completion(nil)
        }
    }
}

/// The note editor in a SwiftUI view, kept showing `noteID`.
struct NoteEditor: NSViewRepresentable {
    let noteID: String
    let editable: Bool
    /// The latest save of this note. Passed in so that SwiftUI calls
    /// `updateNSView` when another window saves it.
    let change: DocumentChange?
    let model: AppModel
    /// A block to bring into view, when the note was opened by a link to it.
    var reveal: String?
    var onRevealed: () -> Void = {}
    /// Called when the user clicks into this editor.
    var onFocus: () -> Void = {}

    func makeCoordinator() -> EditorCoordinator {
        EditorCoordinator(model: model)
    }

    func makeNSView(context: Context) -> NSScrollView {
        context.coordinator.editor.scrollView
    }

    func updateNSView(_ view: NSScrollView, context: Context) {
        context.coordinator.onFocus = onFocus
        context.coordinator.show(noteID: noteID, editable: editable, change: change)
        context.coordinator.reveal(reveal, then: onRevealed)
    }

    static func dismantleNSView(_ view: NSScrollView, coordinator: EditorCoordinator) {
        // The window is closing, or another kind of note took the editor's
        // place. Save what has not been saved yet.
        coordinator.saveAfterUpdate()
    }
}
