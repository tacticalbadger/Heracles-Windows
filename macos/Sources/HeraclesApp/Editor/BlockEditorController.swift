import AppKit

/// What an editor needs from the app around it.
@MainActor
public protocol BlockEditorHost: AnyObject {
    /// The document changed. Time to save it.
    func editorDidChange(_ editor: BlockEditorController)
    func editorDidFocus(_ editor: BlockEditorController)
    func editor(_ editor: BlockEditorController, openLink href: String)
    /// Keep an image and give back the address to store in the document.
    func editor(_ editor: BlockEditorController, storeImage data: Data, mime: String) -> String?
    func editor(_ editor: BlockEditorController, loadImage source: String, completion: @escaping @MainActor (NSImage?) -> Void)
    /// Convert rich text from elsewhere into a document, as JSON.
    func editor(_ editor: BlockEditorController, documentFromHTML html: String) -> String?
    func editor(_ editor: BlockEditorController, copyLinkToBlock id: String)
}

/// One open note's editor: the document, the selection, and the views that
/// show them.
///
/// The document is the truth. A text view holds the text of one block while
/// the user types in it, and every keystroke is copied into the document.
/// Everything that changes the shape of the note (a new block, a merge, a
/// move) is done to the document first, and the views are then made to match.
@MainActor
public final class BlockEditorController: NSObject, NSTextViewDelegate {
    public private(set) var document = EditorDocument(blocks: [])
    public private(set) var selection: EditorSelection = .none
    public weak var host: BlockEditorHost?
    /// The view to put in a window.
    public let scrollView = NSScrollView()
    private(set) var page: BlockEditorView!
    var ids: BlockIDs

    public var isEditable = true {
        didSet {
            guard isEditable != oldValue else { return }
            closeSlashMenu()
            page.hideHandle()
            refresh()
        }
    }

    private struct Snapshot {
        var document: EditorDocument
        var selection: EditorSelection
    }

    private var undoStack: [Snapshot] = []
    private var redoStack: [Snapshot] = []
    /// The run of typing in progress, which is one step to undo.
    private var typing: (block: String, at: Date)?
    /// The character just typed, when exactly one was.
    private var typedCharacter: String?
    private var callbacksOff = 0
    private weak var focusedText: BlockTextView?
    /// The column the caret keeps while it moves up or down through blocks.
    private var goalX: CGFloat?
    private var isMovingVertically = false
    /// The block a mouse drag started in, while the button is down.
    private var mouseAnchor: String?
    private var mouseTimer: Timer?
    /// The two ends of a selection of whole blocks.
    private var blockAnchor: String?
    private var blockHead: String?
    private var slash: (block: String, start: Int)?
    private var popover: NSPopover?

    static let pasteboardType = NSPasteboard.PasteboardType("com.heracles.notes.blocks")

    public init(ids: BlockIDs = .random) {
        self.ids = ids
        super.init()
        page = BlockEditorView(editor: self)
        scrollView.hasVerticalScroller = true
        scrollView.drawsBackground = false
        scrollView.documentView = page
        page.setFrameSize(NSSize(width: scrollView.contentSize.width, height: page.frame.height))
    }

    // MARK: The document

    /// Show a stored document. False when the text is not one.
    @discardableResult
    public func load(json: String) -> Bool {
        guard let loaded = EditorDocument(json: json, ids: &ids) else { return false }
        let previous = selection
        let hadFocus = page.window.map { $0.firstResponder === focusedText && focusedText != nil } ?? false
        document = loaded
        undoStack = []
        redoStack = []
        typing = nil
        closeSlashMenu()
        selection = .none
        page.selectedBlocks = []
        refresh()
        // The same note, saved elsewhere: keep the caret where it was if
        // its block is still there.
        if hadFocus, case .text(let id, let range) = previous, document.block(id)?.hasText == true {
            select(.text(block: id, range: range))
        }
        return true
    }

    /// The document to store.
    public var json: String { document.json }

    private func refresh() {
        page.reconcile(document)
        page.layoutBlocks()
    }

    func pageDidLayout() {
        if let slash, let textView = page.textView(for: slash.block), !page.slashMenu.isHidden {
            page.placeSlashMenu(under: textView, at: slash.start)
        }
    }

    func blockHeightChanged() {
        page.needsLayout = true
    }

    /// Run `work` without the text views' reports of it being taken for
    /// typing. Used whenever the editor itself writes to a text view.
    func withoutTextCallbacks(_ work: () -> Void) {
        callbacksOff += 1
        work()
        callbacksOff -= 1
    }

    // MARK: Editing

    /// Make an edit to the document. The views follow, the selection goes
    /// where the edit says, and the whole thing is one step to undo. True
    /// when the edit did anything.
    @discardableResult
    func edit(undoable: Bool = true, _ change: (inout EditorDocument, inout BlockIDs) -> EditorSelection?) -> Bool {
        guard isEditable else { return false }
        let before = Snapshot(document: document, selection: selection)
        var next = document
        let chosen = change(&next, &ids)
        let changed = next != document
        guard changed || chosen != nil else { return false }
        if changed {
            if undoable { push(before) }
            document = next
            refresh()
        }
        typing = nil
        if let chosen { select(chosen) }
        if changed { host?.editorDidChange(self) }
        return true
    }

    private func push(_ snapshot: Snapshot) {
        undoStack.append(snapshot)
        if undoStack.count > 200 { undoStack.removeFirst() }
        redoStack = []
    }

    /// Start a step to undo that later changes will add to, for a drag
    /// that changes the document many times.
    func beginUndoGroup() {
        push(Snapshot(document: document, selection: selection))
        typing = nil
    }

    public var canUndo: Bool { !undoStack.isEmpty }
    public var canRedo: Bool { !redoStack.isEmpty }

    public func undo() {
        guard isEditable, let snapshot = undoStack.popLast() else { return }
        redoStack.append(Snapshot(document: document, selection: selection))
        restore(snapshot)
    }

    public func redo() {
        guard isEditable, let snapshot = redoStack.popLast() else { return }
        undoStack.append(Snapshot(document: document, selection: selection))
        restore(snapshot)
    }

    private func restore(_ snapshot: Snapshot) {
        typing = nil
        closeSlashMenu()
        document = snapshot.document
        refresh()
        select(snapshot.selection)
        host?.editorDidChange(self)
    }

    // MARK: Selection

    /// Put the selection somewhere, and the keyboard focus with it.
    public func select(_ new: EditorSelection) {
        switch new {
        case .none:
            selection = .none
            page.selectedBlocks = []
        case .blocks(let selected):
            let present = selected.filter(document.contains)
            guard !present.isEmpty else { return }
            collapseFocusedText()
            selection = .blocks(present)
            page.selectedBlocks = present
            let inside = stops(in: present)
            if blockAnchor == nil || !inside.contains(blockAnchor ?? "") { blockAnchor = inside.first }
            if blockHead == nil || !inside.contains(blockHead ?? "") { blockHead = inside.last }
            page.window?.makeFirstResponder(page)
            if let frame = page.frame(of: present[0]) { page.scrollToVisible(frame.insetBy(dx: 0, dy: -24)) }
        case .text(let id, let range):
            guard let textView = page.textView(for: id) else { return }
            page.selectedBlocks = []
            blockAnchor = nil
            blockHead = nil
            let length = (textView.string as NSString).length
            let lower = min(max(range.lowerBound, 0), length)
            let upper = min(max(range.upperBound, lower), length)
            if let window = page.window, window.firstResponder !== textView {
                window.makeFirstResponder(textView)
            } else if page.window == nil {
                textViewDidFocus(textView)
            }
            withoutTextCallbacks { textView.setSelectedRange(NSRange(location: lower, length: upper - lower)) }
            selection = .text(block: id, range: lower..<upper)
            updateTypingAttributes(textView)
            textView.scrollRangeToVisible(NSRange(location: upper, length: 0))
        }
        if !isMovingVertically { goalX = nil }
    }

    /// The caret's stops inside some blocks, in order.
    private func stops(in blocks: [String]) -> [String] {
        document.stops.map(\.id).filter { stop in blocks.contains { document.isBlock(stop, inside: $0) } }
    }

    private func collapseFocusedText() {
        guard let old = focusedText, old.selectedRange().length > 0 else { return }
        withoutTextCallbacks { old.setSelectedRange(NSRange(location: old.selectedRange().upperBound, length: 0)) }
    }

    func textViewDidFocus(_ textView: BlockTextView) {
        // A text view that loses the focus would go on showing its
        // selection, greyed. Only one block's text is ever selected.
        if let old = focusedText, old !== textView, old.selectedRange().length > 0 {
            withoutTextCallbacks { old.setSelectedRange(NSRange(location: old.selectedRange().upperBound, length: 0)) }
        }
        if focusedText !== textView { closeSlashMenu() }
        focusedText = textView
        page.selectedBlocks = []
        let range = textView.selectedRange()
        selection = .text(block: textView.blockID, range: range.location..<range.upperBound)
        host?.editorDidFocus(self)
    }

    func pageDidFocus() {
        collapseFocusedText()
        closeSlashMenu()
        host?.editorDidFocus(self)
    }

    /// Select whole blocks. With `extending`, from where the selection
    /// already is to the first of them.
    func selectBlocks(_ blocks: [String], extending: Bool) {
        guard let first = blocks.first else { return }
        var anchor: String?
        if extending {
            switch selection {
            case .text(let id, _): anchor = id
            case .blocks: anchor = blockAnchor
            case .none: break
            }
        }
        if let anchor, let head = stops(in: [first]).last {
            let range = document.blocks(from: anchor, to: head)
            blockAnchor = anchor
            blockHead = head
            select(.blocks(range.isEmpty ? blocks : range))
        } else {
            blockAnchor = nil
            blockHead = nil
            select(.blocks(blocks))
        }
    }

    /// A shift-click in a block's text. When the selection is in another
    /// block, whole blocks from there to here are selected. False when it
    /// is in this block, where the text view extends its own selection.
    func extendSelection(toBlock id: String) -> Bool {
        var anchor: String?
        switch selection {
        case .text(let from, _): anchor = from == id ? nil : from
        case .blocks: anchor = blockAnchor
        case .none: break
        }
        guard let anchor else { return false }
        extendBlockSelection(from: anchor, to: id)
        return true
    }

    func selectAllBlocks() {
        blockAnchor = nil
        blockHead = nil
        select(.blocks(document.blocks.map(\.id)))
    }

    func deleteSelectedBlocks() {
        guard case .blocks(let selected) = selection else { return }
        edit { $0.remove(selected, ids: &$1) }
    }

    /// Put the caret at the end of the note, in a new line if the note
    /// does not end in an empty one.
    public func focusEnd() {
        if let last = document.blocks.last, last.kind == .paragraph, last.text.isEmpty {
            select(.caret(last.id, 0))
            return
        }
        edit { document, ids in
            let paragraph = Block(id: ids.make(), kind: .paragraph)
            document.blocks.append(paragraph)
            return .caret(paragraph.id, 0)
        }
        if !isEditable, let last = document.stops.last(where: \.hasText) {
            select(.caret(last.id, last.text.length))
        }
    }

    public func focusStart() {
        guard let first = document.stops.first else { return }
        select(first.hasText ? .caret(first.id, 0) : .blocks([first.id]))
    }

    private func updateTypingAttributes(_ textView: BlockTextView) {
        guard let block = document.block(textView.blockID), !textView.hasMarkedText() else { return }
        let range = textView.selectedRange()
        guard range.length == 0 else { return }
        let style = block.kind == .code ? TextStyle.plain : block.text.style(before: range.location)
        textView.typingAttributes = EditorTheme.attributes(for: style, in: block)
    }

    // MARK: Text view delegate: typing

    public func textView(_ textView: NSTextView, shouldChangeTextIn range: NSRange, replacementString: String?) -> Bool {
        guard callbacksOff == 0 else { return true }
        guard isEditable, let textView = textView as? BlockTextView else { return false }
        typedCharacter = range.length == 0 && replacementString?.count == 1 ? replacementString : nil
        // A run of typing in one block is one step to undo.
        let now = Date()
        if let typing, typing.block == textView.blockID, now.timeIntervalSince(typing.at) < 1.5 {
            self.typing?.at = now
        } else {
            push(Snapshot(document: document, selection: selection))
            typing = (textView.blockID, now)
        }
        return true
    }

    public func textDidChange(_ notification: Notification) {
        guard callbacksOff == 0, let textView = notification.object as? BlockTextView else { return }
        syncText(from: textView)
    }

    /// Copy what a text view now holds into the document.
    private func syncText(from textView: BlockTextView) {
        guard var block = document.block(textView.blockID) else { return }
        let text = EditorTheme.richText(from: textView.attributedString(), isCode: block.kind == .code)
        let typed = typedCharacter
        typedCharacter = nil
        guard text != block.text else { return }
        block.text = text
        document.update(block.id) { $0.text = text }
        (page.blockView(for: block.id) as? TextBlockView)?.noteShown(block)
        (page.blockView(for: block.id) as? CodeBlockView)?.noteShown(block)
        page.hideHandle()

        // While an input method is composing, the text is not final.
        if !textView.hasMarkedText() {
            let caret = textView.selectedRange()
            if let typed, caret.length == 0, block.kind != .code, applyShortcut(typed, in: block, caret: caret.location) {
                return
            }
            restyle(textView, block)
            if typed == "/", block.kind != .code { openSlashMenu(in: textView, caret: caret.location) }
            updateSlashMenu(for: textView)
            updateTypingAttributes(textView)
        }
        page.needsLayout = true
        host?.editorDidChange(self)
    }

    /// Make a text view's fonts and colors those the document's marks call
    /// for, when typing or a correction left them otherwise.
    private func restyle(_ textView: BlockTextView, _ block: Block) {
        let wanted = EditorTheme.attributedString(for: block)
        guard let storage = textView.textStorage, !storage.isEqual(to: wanted) else { return }
        withoutTextCallbacks {
            let selected = textView.selectedRange()
            storage.setAttributedString(wanted)
            textView.setSelectedRange(selected)
        }
    }

    /// Markdown typed at the start of a block, or around a few words.
    private func applyShortcut(_ typed: String, in block: Block, caret: Int) -> Bool {
        let before = Snapshot(document: document, selection: .caret(block.id, caret))
        var next = document
        var chosen: EditorSelection?
        var closed: TextMark?
        if typed == " " || typed == "-" {
            chosen = next.applyBlockShortcut(in: block.id, caret: caret, ids: &ids)
        }
        if chosen == nil, "*_`~".contains(typed), let result = block.text.applyingInlineShortcut(caret: caret) {
            next.update(block.id) { $0.text = result.text }
            chosen = .caret(block.id, result.caret)
            closed = result.mark
        }
        if chosen == nil, typed == " ", let linked = block.text.autolinked(caret: caret) {
            next.update(block.id) { $0.text = linked }
            chosen = .caret(block.id, caret)
        }
        guard let chosen else { return false }
        // A step of its own, so undoing gives back what was typed.
        push(before)
        typing = nil
        document = next
        refresh()
        select(chosen)
        // The closing mark ended it: what is typed next does not carry on
        // in bold.
        if let closed, let textView = page.textView(for: block.id), let current = document.block(block.id) {
            var style = current.text.style(before: textView.selectedRange().location)
            style[closed] = false
            textView.typingAttributes = EditorTheme.attributes(for: style, in: current)
        }
        host?.editorDidChange(self)
        return true
    }

    public func textViewDidChangeSelection(_ notification: Notification) {
        guard callbacksOff == 0, let textView = notification.object as? BlockTextView else { return }
        let hasFocus = textView.window.map { $0.firstResponder === textView } ?? (focusedText === textView)
        guard hasFocus else { return }
        let range = textView.selectedRange()
        selection = .text(block: textView.blockID, range: range.location..<range.upperBound)
        if !isMovingVertically { goalX = nil }
        updateTypingAttributes(textView)
        updateSlashMenu(for: textView)
    }

    public func textView(_ textView: NSTextView, clickedOnLink link: Any, at charIndex: Int) -> Bool {
        let href = (link as? URL)?.absoluteString ?? (link as? String) ?? ""
        if !href.isEmpty { host?.editor(self, openLink: href) }
        return true
    }

    // MARK: Text view delegate: keys

    public func textView(_ textView: NSTextView, doCommandBy selector: Selector) -> Bool {
        guard let textView = textView as? BlockTextView, !textView.hasMarkedText() else { return false }
        if slash != nil, handleSlashKey(selector) { return true }
        let id = textView.blockID
        let range = textView.selectedRange()
        let length = (textView.string as NSString).length
        let selected = range.location..<range.upperBound

        switch selector {
        case #selector(NSResponder.insertNewline(_:)):
            guard isEditable else { return true }
            if textView.isCode {
                insertCodeNewline(in: textView)
            } else if NSApp.currentEvent?.type == .keyDown, NSApp.currentEvent?.modifierFlags.contains(.shift) == true {
                textView.insertText("\n", replacementRange: range)
            } else {
                edit { $0.split(id, at: selected, ids: &$1) }
            }
            return true
        case #selector(NSResponder.insertLineBreak(_:)), #selector(NSResponder.insertNewlineIgnoringFieldEditor(_:)):
            guard isEditable else { return true }
            if textView.isCode {
                // The way out of a code block: a new line below it.
                edit { document, ids in
                    let paragraph = Block(id: ids.make(), kind: .paragraph)
                    document.insert([paragraph], after: id)
                    return .caret(paragraph.id, 0)
                }
            } else {
                textView.insertText("\n", replacementRange: range)
            }
            return true
        case #selector(NSResponder.deleteBackward(_:)):
            guard range.length == 0, range.location == 0 else { return false }
            edit { $0.deleteBackward(atStartOf: id, ids: &$1) }
            return true
        case #selector(NSResponder.deleteForward(_:)):
            guard range.length == 0, range.location == length else { return false }
            edit { document, _ in document.deleteForward(atEndOf: id) }
            return true
        case #selector(NSResponder.insertTab(_:)):
            if textView.isCode {
                textView.insertText("  ", replacementRange: range)
            } else {
                edit { document, _ in document.indent(id) ? .text(block: id, range: selected) : nil }
            }
            return true
        case #selector(NSResponder.insertBacktab(_:)):
            if !textView.isCode {
                edit { document, _ in document.outdent(id) ? .text(block: id, range: selected) : nil }
            }
            return true
        case #selector(NSResponder.moveUp(_:)):
            return textView.caretIsOnFirstLine && moveCaret(from: textView, up: true)
        case #selector(NSResponder.moveDown(_:)):
            return textView.caretIsOnLastLine && moveCaret(from: textView, up: false)
        case #selector(NSResponder.moveLeft(_:)):
            guard range.length == 0, range.location == 0, let previous = document.stop(before: id) else { return false }
            select(previous.hasText ? .caret(previous.id, previous.text.length) : .blocks([previous.id]))
            return true
        case #selector(NSResponder.moveRight(_:)):
            guard range.length == 0, range.location == length, let next = document.stop(after: id) else { return false }
            select(next.hasText ? .caret(next.id, 0) : .blocks([next.id]))
            return true
        case #selector(NSResponder.moveUpAndModifySelection(_:)):
            guard textView.caretIsOnFirstLine, let previous = document.stop(before: id) else { return false }
            extendBlockSelection(from: id, to: previous.id)
            return true
        case #selector(NSResponder.moveDownAndModifySelection(_:)):
            guard textView.caretIsOnLastLine, let next = document.stop(after: id) else { return false }
            extendBlockSelection(from: id, to: next.id)
            return true
        case #selector(NSResponder.moveToBeginningOfDocument(_:)):
            focusStart()
            return true
        case #selector(NSResponder.moveToEndOfDocument(_:)):
            guard let last = document.stops.last else { return false }
            select(last.hasText ? .caret(last.id, last.text.length) : .blocks([last.id]))
            return true
        case #selector(NSResponder.selectAll(_:)):
            // The first Select All takes the block's text, the second the note.
            guard range.length == length else { return false }
            selectAllBlocks()
            return true
        case #selector(NSResponder.cancelOperation(_:)):
            selectBlocks([id], extending: false)
            return true
        default:
            return false
        }
    }

    private func extendBlockSelection(from anchor: String, to head: String) {
        let range = document.blocks(from: anchor, to: head)
        guard !range.isEmpty else { return }
        blockAnchor = anchor
        blockHead = head
        select(.blocks(range))
    }

    /// Move the caret to the block above or below, keeping its column.
    private func moveCaret(from textView: BlockTextView, up: Bool) -> Bool {
        let id = textView.blockID
        guard let stop = up ? document.stop(before: id) : document.stop(after: id) else { return false }
        guard stop.hasText, let target = page.textView(for: stop.id) else {
            select(.blocks([stop.id]))
            return true
        }
        let x = goalX ?? textView.convert(NSPoint(x: textView.caretX, y: 0), to: page).x
        let offset = target.offset(nearestTo: target.convert(NSPoint(x: x, y: 0), from: page).x, onFirstLine: !up)
        isMovingVertically = true
        select(.caret(stop.id, offset))
        isMovingVertically = false
        goalX = x
        return true
    }

    /// A new line in code starts as far in as the line above it.
    private func insertCodeNewline(in textView: BlockTextView) {
        let text = textView.string as NSString
        let range = textView.selectedRange()
        let line = text.lineRange(for: NSRange(location: range.location, length: 0))
        let before = text.substring(with: NSRange(location: line.location, length: range.location - line.location))
        let indent = String(before.prefix { $0 == " " || $0 == "\t" })
        textView.insertText("\n" + indent, replacementRange: range)
    }

    // MARK: Keys while whole blocks are selected

    func handleBlockKey(_ event: NSEvent) -> Bool {
        guard case .blocks(let selected) = selection, !selected.isEmpty else { return false }
        let extending = event.modifierFlags.contains(.shift)
        switch event.keyCode {
        case 51, 117:  // Delete, Forward Delete
            deleteSelectedBlocks()
        case 126, 123:  // Up, Left
            moveBlockSelection(up: true, extending: extending)
        case 125, 124:  // Down, Right
            moveBlockSelection(up: false, extending: extending)
        case 36, 76:  // Return, Enter: back to typing, at the end of the selection
            if let last = stops(in: selected).last, let block = document.block(last), block.hasText {
                select(.caret(last, block.text.length))
            }
        case 53:  // Escape
            select(.none)
        case 48:  // Tab
            edit { document, _ in
                var moved = false
                for id in extending ? selected.reversed() : selected {
                    moved = (extending ? document.outdent(id) : document.indent(id)) || moved
                }
                return moved ? .blocks(selected) : nil
            }
        default:
            return false
        }
        return true
    }

    private func moveBlockSelection(up: Bool, extending: Bool) {
        guard case .blocks(let selected) = selection else { return }
        let order = document.stops
        let inside = stops(in: selected)
        guard let first = inside.first, let last = inside.last else { return }
        if extending {
            let anchor = blockAnchor ?? (up ? last : first)
            let head = blockHead ?? (up ? first : last)
            guard let index = order.firstIndex(where: { $0.id == head }) else { return }
            let next = up ? index - 1 : index + 1
            guard order.indices.contains(next) else { return }
            extendBlockSelection(from: anchor, to: order[next].id)
            return
        }
        guard let edge = order.firstIndex(where: { $0.id == (up ? first : last) }) else { return }
        let next = up ? edge - 1 : edge + 1
        guard order.indices.contains(next) else {
            // Nothing beyond: back to typing in what is selected.
            if let block = document.block(up ? first : last), block.hasText {
                select(.caret(block.id, up ? 0 : block.text.length))
            }
            return
        }
        let stop = order[next]
        select(stop.hasText ? .caret(stop.id, up ? stop.text.length : 0) : .blocks([stop.id]))
    }

    // MARK: Key equivalents

    /// Command keys the editor answers itself. `textView` is the one being
    /// typed in, or `nil` while whole blocks are selected.
    func handleKeyEquivalent(_ event: NSEvent, in textView: BlockTextView?) -> Bool {
        let flags = event.modifierFlags.intersection([.command, .shift, .option, .control])
        guard flags.contains(.command), let key = event.charactersIgnoringModifiers?.lowercased() else { return false }
        switch (key, flags) {
        case ("z", [.command]):
            undo()
            return true
        case ("z", [.command, .shift]):
            redo()
            return true
        default:
            break
        }
        guard let textView, isEditable, !textView.isCode else { return false }
        switch (key, flags) {
        case ("b", [.command]): toggle(.bold)
        case ("i", [.command]): toggle(.italic)
        case ("e", [.command]): toggle(.code)
        case ("x", [.command, .shift]): toggle(.strike)
        case ("k", [.command]):
            // With nothing to link, Command-K is the app's Quick Open.
            guard textView.selectedRange().length > 0 || currentLink != nil else { return false }
            editLink()
        case ("0", [.command, .option]): turnCurrentBlock(into: .paragraph)
        case ("1", [.command, .option]): turnCurrentBlock(into: .heading(1))
        case ("2", [.command, .option]): turnCurrentBlock(into: .heading(2))
        case ("3", [.command, .option]): turnCurrentBlock(into: .heading(3))
        case ("4", [.command, .option]): turnCurrentBlock(into: .heading(4))
        default:
            switch (event.keyCode, flags) {
            case (26, [.command, .shift]): turnCurrentBlock(into: .numbered)  // 7
            case (28, [.command, .shift]): turnCurrentBlock(into: .bullet)  // 8
            case (25, [.command, .shift]): turnCurrentBlock(into: .todo)  // 9
            default: return false
            }
        }
        return true
    }

    // MARK: Marks and links

    /// A command from the Format menu.
    func apply(_ format: EditorFormat) {
        switch format {
        case .mark(let mark): toggle(mark)
        case .link: editLink()
        case .block(let target): turnCurrentBlock(into: target)
        }
    }

    /// Turn a mark on or off for the selected text, or for what is typed
    /// next when nothing is selected.
    public func toggle(_ mark: TextMark) {
        guard isEditable, case .text(let id, let range) = selection, let block = document.block(id),
            block.kind != .code, let textView = page.textView(for: id)
        else { return }
        if range.isEmpty {
            var style = (textView.typingAttributes[.editorStyle] as? StyleBox)?.style ?? .plain
            if mark == .code {
                style = style.code ? TextStyle(other: style.other) : TextStyle(code: true, other: style.other)
            } else if !style.code {
                style[mark].toggle()
            }
            textView.typingAttributes = EditorTheme.attributes(for: style, in: block)
            return
        }
        let on = !block.text.has(mark, in: range)
        edit { document, _ in
            document.update(id) { $0.text.set(mark, on, in: range) }
            return .text(block: id, range: range)
        }
    }

    /// The link the caret is in, or that the selection is within.
    private var currentLink: (block: String, range: Range<Int>, link: TextLink)? {
        guard case .text(let id, let range) = selection, let block = document.block(id) else { return nil }
        guard let found = block.text.link(at: max(range.upperBound, range.lowerBound + 1)) ?? block.text.link(at: range.lowerBound)
        else { return nil }
        return (id, found.range, found.link)
    }

    /// Link the selected text, or change or remove the link the caret is in.
    /// `nil` or an empty address removes the link.
    public func setLink(_ address: String?) {
        guard isEditable, case .text(let id, let selected) = selection else { return }
        let range = selected.isEmpty ? currentLink?.range ?? selected : selected
        guard !range.isEmpty else { return }
        let link = address.flatMap(Self.cleanAddress).map { TextLink(href: $0, extra: currentLink?.link.extra ?? [:]) }
        edit { document, _ in
            document.update(id) { $0.text.setLink(link, in: range) }
            return .text(block: id, range: selected)
        }
    }

    /// An address fit to be a link: one of the kinds the app opens, with
    /// `https://` assumed when none is given. `nil` for anything else.
    static func cleanAddress(_ address: String) -> String? {
        let trimmed = address.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, !trimmed.contains(where: \.isWhitespace) else { return nil }
        guard let colon = trimmed.firstIndex(of: ":") else { return "https://" + trimmed }
        let scheme = trimmed[..<colon]
        let rest = trimmed[trimmed.index(after: colon)...]
        // What stands before a colon is a kind of link when it is a bare
        // word: `mailto:`, `https:`. `example.com:8080` is a host and a port.
        let isScheme = scheme.first?.isLetter == true
            && scheme.allSatisfy { $0.isLetter || $0.isNumber || $0 == "+" || $0 == "-" }
            && rest.first?.isNumber != true
        guard isScheme else { return "https://" + trimmed }
        return ["http", "https", "mailto", "heracles"].contains(scheme.lowercased()) ? trimmed : nil
    }

    /// Ask for the address of a link, in a small panel by the text.
    func editLink() {
        guard case .text(let id, let range) = selection, let textView = page.textView(for: id) else { return }
        let shown = currentLink?.range ?? range
        guard !shown.isEmpty else { return }
        let field = LinkField(address: currentLink?.link.href ?? "") { [weak self] address in
            self?.popover?.close()
            self?.setLink(address)
        }
        let popover = NSPopover()
        popover.behavior = .transient
        popover.contentViewController = field
        self.popover = popover
        var anchor = NSRect(x: 0, y: 0, width: 10, height: 18)
        if let window = textView.window {
            let screen = textView.firstRect(forCharacterRange: NSRange(location: shown.lowerBound, length: shown.count), actualRange: nil)
            anchor = textView.convert(window.convertFromScreen(screen), from: nil)
        }
        popover.show(relativeTo: anchor, of: textView, preferredEdge: .maxY)
    }

    // MARK: Changing blocks

    /// Turn the block being typed in into another kind, keeping the caret.
    public func turnCurrentBlock(into target: BlockTarget) {
        switch selection {
        case .text(let id, let range):
            edit { document, ids in
                let chosen = document.turn(id, into: target, ids: &ids)
                if case .text(let landed, _) = chosen, landed == id { return .text(block: id, range: range) }
                return chosen
            }
        case .blocks(let selected):
            edit { document, ids in
                for id in selected { document.turn(id, into: target, ids: &ids) }
                return nil
            }
            select(.blocks(selected.filter(document.contains)))
        case .none:
            break
        }
    }

    func turn(_ id: String, into target: BlockTarget) {
        edit { $0.turn(id, into: target, ids: &$1) }
    }

    func setChecked(_ id: String, _ checked: Bool) {
        edit { document, _ in
            document.update(id) { $0.isChecked = checked }
            return nil
        }
    }

    func setOpen(_ id: String, _ open: Bool) {
        edit { document, _ in
            document.update(id) { $0.isOpen = open }
            return nil
        }
        // The caret cannot stay in a block that was just folded away.
        if !open, case .text(let selected, _) = selection, !document.stops.contains(where: { $0.id == selected }),
            let first = document.block(id)?.children.first
        {
            select(first.hasText ? .caret(first.id, first.text.length) : .blocks([id]))
        }
    }

    func setLanguage(_ id: String, _ language: String) {
        edit { document, _ in
            document.update(id) { $0.language = language }
            return nil
        }
    }

    func setEmoji(_ id: String, _ emoji: String) {
        edit { document, _ in
            document.update(id) { $0.emoji = emoji }
            return nil
        }
    }

    /// Set the widths of a row's columns, during a drag. The drag as a
    /// whole is one step to undo, begun with `beginUndoGroup`.
    func setColumnWidths(_ id: String, _ widths: [Double]) {
        edit(undoable: false) { document, _ in
            document.update(id) { row in
                for index in row.children.indices where index < widths.count {
                    row.children[index].width = (widths[index] * 1000).rounded() / 1000
                }
            }
            return nil
        }
    }

    func chooseEmoji(for id: String, from view: NSView) {
        let picker = EmojiPicker(current: document.block(id)?.emoji) { [weak self] emoji in
            self?.popover?.close()
            self?.setEmoji(id, emoji ?? "💡")
        }
        let popover = NSPopover()
        popover.behavior = .transient
        popover.contentViewController = picker
        self.popover = popover
        popover.show(relativeTo: view.bounds, of: view, preferredEdge: .maxY)
    }

    /// Add an empty line below a block and put the caret in it.
    func addBlock(after id: String) {
        edit { document, ids in
            let paragraph = Block(id: ids.make(), kind: .paragraph)
            document.insert([paragraph], after: id)
            return .caret(paragraph.id, 0)
        }
    }

    @discardableResult
    func move(_ blocks: [String], to target: DropTarget) -> Bool {
        edit { document, ids in
            document.move(blocks, to: target, ids: &ids) ? .blocks(blocks) : nil
        }
    }

    func duplicate(_ id: String) {
        edit { document, ids in
            document.duplicate(id, ids: &ids).map { .blocks([$0]) }
        }
    }

    func remove(_ blocks: [String]) {
        edit { $0.remove(blocks, ids: &$1) }
    }

    // MARK: The block menu

    func showBlockMenu(for id: String, at point: NSPoint) {
        guard let block = document.block(id) else { return }
        let menu = NSMenu()
        if block.hasText || block.kind == .quote || block.kind == .callout || block.kind == .toggle {
            let turnInto = NSMenu()
            let choices: [(String, BlockTarget)] = [
                ("Text", .paragraph), ("Heading 1", .heading(1)), ("Heading 2", .heading(2)), ("Heading 3", .heading(3)),
                ("Bullet List", .bullet), ("Numbered List", .numbered), ("Task List", .todo), ("Quote", .quote),
                ("Callout", .callout), ("Toggle", .toggle), ("Code", .code),
            ]
            for (title, target) in choices {
                turnInto.addItem(ActionMenuItem(title) { [weak self] in self?.turn(id, into: target) })
            }
            let item = NSMenuItem(title: "Turn Into", action: nil, keyEquivalent: "")
            item.submenu = turnInto
            menu.addItem(item)
            menu.addItem(.separator())
        }
        menu.addItem(ActionMenuItem("Duplicate") { [weak self] in self?.duplicate(id) })
        menu.addItem(ActionMenuItem("Copy Link to Block") { [weak self] in
            guard let self else { return }
            self.host?.editor(self, copyLinkToBlock: id)
        })
        menu.addItem(.separator())
        menu.addItem(ActionMenuItem("Delete") { [weak self] in self?.remove([id]) })
        menu.popUp(positioning: nil, at: point, in: page)
    }

    public func textView(_ view: NSTextView, menu: NSMenu, for event: NSEvent, at charIndex: Int) -> NSMenu? {
        guard isEditable, let textView = view as? BlockTextView, !textView.isCode else { return menu }
        var items: [NSMenuItem] = []
        if textView.selectedRange().length > 0 || currentLink != nil {
            items.append(ActionMenuItem(currentLink == nil ? "Add Link…" : "Edit Link…") { [weak self] in self?.editLink() })
            if currentLink != nil {
                items.append(ActionMenuItem("Remove Link") { [weak self] in self?.setLink(nil) })
            }
        }
        if textView.selectedRange().length > 0 {
            let marks: [(String, TextMark)] = [("Bold", .bold), ("Italic", .italic), ("Strikethrough", .strike), ("Code", .code)]
            for (title, mark) in marks {
                items.append(ActionMenuItem(title) { [weak self] in self?.toggle(mark) })
            }
        }
        let id = textView.blockID
        items.append(ActionMenuItem("Copy Link to Block") { [weak self] in
            guard let self else { return }
            self.host?.editor(self, copyLinkToBlock: id)
        })
        items.append(.separator())
        for item in items.reversed() { menu.insertItem(item, at: 0) }
        return menu
    }

    // MARK: The slash menu

    /// The commands the slash menu is offering, or none while it is closed.
    var slashCommands: [SlashCommand] { slash == nil ? [] : page.slashMenu.items }

    private func openSlashMenu(in textView: BlockTextView, caret: Int) {
        let text = textView.string as NSString
        let start = caret - 1
        guard start >= 0, start < text.length, text.character(at: start) == 47 else { return }
        // Only where a word could start, so a slash inside a date or a
        // path is just a slash.
        if start > 0, let before = Unicode.Scalar(text.character(at: start - 1)),
            !CharacterSet.whitespacesAndNewlines.contains(before)
        {
            return
        }
        slash = (textView.blockID, start)
        page.slashMenu.selected = 0
    }

    private func updateSlashMenu(for textView: BlockTextView) {
        guard let slash else { return }
        let text = textView.string as NSString
        let caret = textView.selectedRange()
        guard textView.blockID == slash.block, caret.length == 0, caret.location > slash.start,
            slash.start < text.length, text.character(at: slash.start) == 47
        else {
            closeSlashMenu()
            return
        }
        let query = text.substring(with: NSRange(location: slash.start + 1, length: caret.location - slash.start - 1))
        guard !query.contains(" "), !query.contains("\n"), query.count <= 24 else {
            closeSlashMenu()
            return
        }
        let items = SlashCommand.matching(query)
        page.slashMenu.items = items
        page.slashMenu.selected = min(page.slashMenu.selected, max(items.count - 1, 0))
        page.slashMenu.isHidden = false
        page.placeSlashMenu(under: textView, at: slash.start)
    }

    func closeSlashMenu() {
        slash = nil
        page?.slashMenu.isHidden = true
    }

    private func handleSlashKey(_ selector: Selector) -> Bool {
        let menu = page.slashMenu
        switch selector {
        case #selector(NSResponder.moveDown(_:)):
            if !menu.items.isEmpty { menu.selected = (menu.selected + 1) % menu.items.count }
        case #selector(NSResponder.moveUp(_:)):
            if !menu.items.isEmpty { menu.selected = (menu.selected + menu.items.count - 1) % menu.items.count }
        case #selector(NSResponder.insertNewline(_:)), #selector(NSResponder.insertTab(_:)):
            guard !menu.items.isEmpty else {
                closeSlashMenu()
                return false
            }
            chooseSlashCommand(at: menu.selected)
        case #selector(NSResponder.cancelOperation(_:)):
            closeSlashMenu()
        default:
            return false
        }
        return true
    }

    /// Take a command from the slash menu: what was typed to find it goes
    /// away, and the block becomes what was chosen.
    func chooseSlashCommand(at index: Int) {
        guard let slash, page.slashMenu.items.indices.contains(index), let textView = page.textView(for: slash.block)
        else { return }
        let command = page.slashMenu.items[index]
        let caret = textView.selectedRange().location
        closeSlashMenu()
        edit { document, ids in
            document.update(slash.block) { $0.text.replace(slash.start..<max(caret, slash.start), with: RichText()) }
            let chosen = document.turn(slash.block, into: command.target, ids: &ids)
            if case .text(let landed, _) = chosen, landed == slash.block { return .caret(slash.block, slash.start) }
            return chosen
        }
    }

    // MARK: Dragging a selection across blocks

    func textViewWillTrackMouse(_ textView: BlockTextView) {
        closeSlashMenu()
        mouseAnchor = textView.blockID
        // The text view follows the mouse in a loop of its own until the
        // button is let go. A timer in that loop's mode watches for the
        // pointer leaving the block.
        let timer = Timer(timeInterval: 0.04, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.updatePendingBlocks() }
        }
        RunLoop.main.add(timer, forMode: .eventTracking)
        mouseTimer = timer
    }

    /// The block under the pointer, when the drag has left the block it
    /// began in.
    private func blockUnderDrag() -> String? {
        guard let anchor = mouseAnchor, let window = page.window else { return nil }
        let point = page.convert(window.mouseLocationOutsideOfEventStream, from: nil)
        var hovered = page.blockView(at: point, nearest: true)?.block.id
        if hovered == nil, let first = document.stops.first, let last = document.stops.last {
            // Past either end of the note, the selection runs to that end.
            let top = page.frame(of: document.blocks[0].id)?.minY ?? 0
            hovered = point.y < top ? first.id : last.id
        }
        guard let hovered, hovered != anchor else { return nil }
        return hovered
    }

    private func updatePendingBlocks() {
        guard let anchor = mouseAnchor else { return }
        page.pendingBlocks = blockUnderDrag().map { document.blocks(from: anchor, to: $0) } ?? []
    }

    func textViewDidTrackMouse(_ textView: BlockTextView) {
        mouseTimer?.invalidate()
        mouseTimer = nil
        let anchor = textView.blockID
        let head = blockUnderDrag()
        mouseAnchor = nil
        page.pendingBlocks = []
        if let head { extendBlockSelection(from: anchor, to: head) }
    }

    // MARK: Copy and paste

    private func write(_ blocks: [Block], to pasteboard: NSPasteboard, adding: Bool) {
        let nodes = JSONValue.object(["type": .string("doc"), "content": .array(EditorDocument.nodes(blocks))]).text
        if adding {
            pasteboard.addTypes([Self.pasteboardType], owner: nil)
        } else {
            pasteboard.clearContents()
            pasteboard.setString(EditorDocument.lines(blocks, indent: "").joined(separator: "\n"), forType: .string)
        }
        pasteboard.setString(nodes, forType: Self.pasteboardType)
    }

    /// Copy what is selected. False when a text view should copy its own
    /// selection, which it knows how to offer to other apps. The editor
    /// then adds the marks in its own form with `addCopiedText`.
    func copySelection(to pasteboard: NSPasteboard) -> Bool {
        guard case .blocks(let selected) = selection else { return false }
        write(selected.compactMap(document.block), to: pasteboard, adding: false)
        return true
    }

    /// After a text view copied its selection: add the same text with the
    /// editor's own marks, so pasting it back loses nothing.
    func addCopiedText(to pasteboard: NSPasteboard) {
        guard case .text(let id, let range) = selection, !range.isEmpty, let block = document.block(id),
            block.kind != .code
        else { return }
        write([Block(id: "copy", kind: .paragraph, text: block.text.subtext(range))], to: pasteboard, adding: true)
    }

    func cutSelection(to pasteboard: NSPasteboard) -> Bool {
        guard isEditable, copySelection(to: pasteboard) else { return false }
        deleteSelectedBlocks()
        return true
    }

    /// Paste at the selection. False when the clipboard holds nothing the
    /// editor can use.
    @discardableResult
    func paste(from pasteboard: NSPasteboard, plainOnly: Bool = false) -> Bool {
        guard isEditable else { return false }
        // Code takes text exactly as it is.
        if case .text(let id, let range) = selection, document.block(id)?.kind == .code {
            guard let string = pasteboard.string(forType: .string), let textView = page.textView(for: id) else { return false }
            textView.insertText(string, replacementRange: NSRange(location: range.lowerBound, length: range.count))
            return true
        }

        // A web address pasted over selected text links that text.
        if !plainOnly, case .text(_, let range) = selection, !range.isEmpty,
            let address = pasteboard.string(forType: .string)?.trimmingCharacters(in: .whitespacesAndNewlines),
            RichText.isWebAddress(address), pasteboard.string(forType: Self.pasteboardType) == nil
        {
            setLink(address)
            return true
        }

        var blocks: [Block]?
        if !plainOnly, let own = pasteboard.string(forType: Self.pasteboardType) {
            blocks = self.blocks(fromDocument: own)
        }
        let hasText = pasteboard.availableType(from: [.string, .html, .rtf]) != nil
        if blocks == nil, !hasText || !Self.imageFiles(on: pasteboard).isEmpty {
            // A picture, or picture files copied in the Finder.
            let images = Self.images(on: pasteboard)
            if !images.isEmpty { blocks = imageBlocks(images) }
        }
        if blocks == nil, !plainOnly, let html = Self.html(on: pasteboard),
            let converted = host?.editor(self, documentFromHTML: html)
        {
            blocks = self.blocks(fromDocument: converted)
        }
        if blocks == nil, let string = pasteboard.string(forType: .string) {
            let lines = string.replacingOccurrences(of: "\r\n", with: "\n").components(separatedBy: "\n")
            let kept = lines.count == 1 ? lines : lines.filter { !$0.trimmingCharacters(in: .whitespaces).isEmpty }
            blocks = kept.map { line in
                // An address on a line of its own arrives as a link.
                let address = line.trimmingCharacters(in: .whitespaces)
                let style = !plainOnly && RichText.isWebAddress(address) ? TextStyle(link: TextLink(href: address)) : .plain
                return Block(id: "pasted", kind: .paragraph, text: RichText(line, style: style))
            }
        }
        guard let blocks, !blocks.isEmpty else { return false }
        let target = selection
        return edit { $0.paste(blocks, at: target, ids: &$1) }
    }

    private func blocks(fromDocument json: String) -> [Block]? {
        var scratch = BlockIDs.random
        guard let document = EditorDocument(json: json, ids: &scratch) else { return nil }
        // A document with nothing in it reads as one empty paragraph.
        if document.blocks.count == 1, document.blocks[0].kind == .paragraph, document.blocks[0].text.isEmpty { return nil }
        return document.blocks
    }

    /// Rich text on the clipboard, as HTML. Apps that only offer RTF have
    /// it converted.
    private static func html(on pasteboard: NSPasteboard) -> String? {
        if let html = pasteboard.string(forType: .html), !html.isEmpty { return html }
        guard let rtf = pasteboard.data(forType: .rtf), let text = NSAttributedString(rtf: rtf, documentAttributes: nil),
            let data = try? text.data(
                from: NSRange(location: 0, length: text.length),
                documentAttributes: [.documentType: NSAttributedString.DocumentType.html])
        else { return nil }
        return String(data: data, encoding: .utf8)
    }

    // MARK: Images

    /// The kind of image a file is, by its name. `nil` when it is not one
    /// the editor takes.
    static func imageType(of url: URL) -> String? {
        let types = [
            "png": "image/png", "jpg": "image/jpeg", "jpeg": "image/jpeg", "gif": "image/gif", "webp": "image/webp",
            "heic": "image/heic", "tiff": "image/tiff", "tif": "image/tiff", "bmp": "image/bmp",
        ]
        return types[url.pathExtension.lowercased()]
    }

    private static func imageFiles(on pasteboard: NSPasteboard) -> [URL] {
        let urls = pasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL]
        return (urls ?? []).filter { imageType(of: $0) != nil }
    }

    /// Pictures on a clipboard: files copied in the Finder, or image data.
    private static func images(on pasteboard: NSPasteboard) -> [(data: Data, mime: String, name: String?)] {
        let files = imageFiles(on: pasteboard)
        if !files.isEmpty { return images(in: files) }
        if let png = pasteboard.data(forType: .png) { return [(png, "image/png", nil)] }
        if let tiff = pasteboard.data(forType: .tiff), let bitmap = NSBitmapImageRep(data: tiff),
            let png = bitmap.representation(using: .png, properties: [:])
        {
            return [(png, "image/png", nil)]
        }
        return []
    }

    private static func images(in files: [URL]) -> [(data: Data, mime: String, name: String?)] {
        files.compactMap { url in
            guard let mime = imageType(of: url), let data = try? Data(contentsOf: url), data.count <= 64 * 1024 * 1024
            else { return nil }
            return (data, mime, url.deletingPathExtension().lastPathComponent)
        }
    }

    private func imageBlocks(_ images: [(data: Data, mime: String, name: String?)]) -> [Block]? {
        let blocks: [Block] = images.compactMap { image in
            guard let source = host?.editor(self, storeImage: image.data, mime: image.mime) else { return nil }
            var block = Block(id: "image", kind: .image)
            block.attrs = ["src": .string(source), "alt": image.name.map(JSONValue.string) ?? .null, "title": .null]
            return block
        }
        return blocks.isEmpty ? nil : blocks
    }

    /// Put image files into the note, where they were dropped.
    func insertImages(from files: [URL], at target: DropTarget?) -> Bool {
        guard let blocks = imageBlocks(Self.images(in: files)) else { return false }
        return edit { document, ids in
            let fresh = EditorDocument.fresh(blocks, ids: &ids)
            switch target {
            case .before(let id): document.insert(fresh, before: id)
            case .after(let id), .leftOf(let id), .rightOf(let id): document.insert(fresh, after: id)
            case nil: document.blocks.append(contentsOf: fresh)
            }
            return .blocks(fresh.map(\.id))
        }
    }

    func image(for source: String, completion: @escaping @MainActor (NSImage?) -> Void) {
        guard let host else {
            completion(nil)
            return
        }
        host.editor(self, loadImage: source, completion: completion)
    }

    // MARK: For the app around the editor

    /// Type text at the caret, as the keyboard would. With no caret, at
    /// the end of the note.
    public func insertText(_ text: String) {
        if case .text = selection {} else { focusEnd() }
        guard case .text(let id, let range) = selection, let textView = page.textView(for: id) else { return }
        textView.insertText(text, replacementRange: NSRange(location: range.lowerBound, length: range.count))
    }

    public func scrollToTop() {
        page.scroll(.zero)
    }

    /// Bring a block into view and mark it for a moment. The id may be a
    /// list item's or that of the paragraph inside it.
    @discardableResult
    public func reveal(_ blockID: String) -> Bool {
        guard let block = document.allBlocks.first(where: { $0.id == blockID || $0.paragraphID == blockID }) else {
            return false
        }
        // Open any closed toggle it is folded away in.
        var path = document.path(to: block.id) ?? []
        while path.count > 1 {
            path.removeLast()
            let ancestor = document[path]
            if ancestor.kind == .toggle, !ancestor.isOpen { setOpen(ancestor.id, true) }
        }
        guard let frame = page.frame(of: block.id) else { return false }
        page.scrollToVisible(frame.insetBy(dx: 0, dy: -80))
        page.flash(block.id)
        return true
    }
}

/// A menu item that runs a closure.
private final class ActionMenuItem: NSMenuItem {
    private let run: () -> Void

    init(_ title: String, _ run: @escaping () -> Void) {
        self.run = run
        super.init(title: title, action: #selector(fire), keyEquivalent: "")
        target = self
    }

    @available(*, unavailable)
    required init(coder: NSCoder) { fatalError("not used") }

    @objc private func fire() { run() }
}

/// The small panel that asks for a link's address.
private final class LinkField: NSViewController, NSTextFieldDelegate {
    private let address: String
    private let done: (String?) -> Void
    private let field = NSTextField()

    init(address: String, done: @escaping (String?) -> Void) {
        self.address = address
        self.done = done
        super.init(nibName: nil, bundle: nil)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("not used") }

    override func loadView() {
        field.stringValue = address
        field.placeholderString = "Paste or type a link"
        field.target = self
        field.action = #selector(commit)
        field.frame = NSRect(x: 10, y: 10, width: 260, height: 22)
        let remove = NSButton(title: "Remove", target: self, action: #selector(removeLink))
        remove.controlSize = .small
        remove.isEnabled = !address.isEmpty
        remove.frame = NSRect(x: 276, y: 9, width: 70, height: 24)
        let box = NSView(frame: NSRect(x: 0, y: 0, width: 356, height: 42))
        box.addSubview(field)
        box.addSubview(remove)
        view = box
    }

    override func viewDidAppear() {
        super.viewDidAppear()
        view.window?.makeFirstResponder(field)
    }

    @objc private func commit() { done(field.stringValue) }
    @objc private func removeLink() { done(nil) }
}

/// A few icons to give a callout, and a field for any other.
private final class EmojiPicker: NSViewController {
    private let current: String?
    private let done: (String?) -> Void
    private static let choices = ["💡", "📌", "⚠️", "❗", "✅", "❓", "🔥", "⭐", "📝", "🎯", "🚀", "🐛"]

    init(current: String?, done: @escaping (String?) -> Void) {
        self.current = current
        self.done = done
        super.init(nibName: nil, bundle: nil)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("not used") }

    override func loadView() {
        let box = NSView(frame: NSRect(x: 0, y: 0, width: 6 * 34 + 12, height: 2 * 34 + 12 + 34))
        for (index, emoji) in Self.choices.enumerated() {
            let button = NSButton(title: emoji, target: self, action: #selector(pick(_:)))
            button.isBordered = false
            button.font = .systemFont(ofSize: 20)
            button.frame = NSRect(x: 6 + CGFloat(index % 6) * 34, y: 40 + CGFloat(1 - index / 6) * 34, width: 34, height: 34)
            box.addSubview(button)
        }
        let field = NSTextField(frame: NSRect(x: 10, y: 10, width: box.frame.width - 20, height: 22))
        field.placeholderString = "Or type one"
        field.target = self
        field.action = #selector(typed(_:))
        box.addSubview(field)
        view = box
    }

    @objc private func pick(_ sender: NSButton) { done(sender.title) }

    @objc private func typed(_ sender: NSTextField) {
        if let first = sender.stringValue.trimmingCharacters(in: .whitespaces).first { done(String(first)) }
    }
}

/// What the Format menu can ask of an editor.
enum EditorFormat {
    case mark(TextMark)
    case link
    case block(BlockTarget)
}

/// A Format menu command on its way to the editor that has the keyboard.
final class FormatRequest: NSObject {
    let format: EditorFormat

    init(_ format: EditorFormat) {
        self.format = format
    }
}
