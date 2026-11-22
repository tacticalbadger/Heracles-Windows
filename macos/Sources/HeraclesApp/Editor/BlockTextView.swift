import AppKit

/// The text of one block: a system text view, so typing, selection, spelling,
/// dictation, text replacement and input methods all behave as they do
/// everywhere else on the Mac. What crosses from one block to the next (Return,
/// Backspace at the start, the arrow keys at an edge) is passed to the editor.
@MainActor
final class BlockTextView: NSTextView {
    var blockID = ""
    weak var editor: BlockEditorController?
    /// Shown while the block is empty and has the caret.
    var placeholder: String?
    var isCode = false

    static func make(editor: BlockEditorController?) -> BlockTextView {
        // Asked for by name: a subclass made with a plain frame gets the
        // older text engine.
        let view = BlockTextView(usingTextLayoutManager: true)
        view.frame = NSRect(x: 0, y: 0, width: 200, height: 20)
        view.editor = editor
        view.delegate = editor
        view.drawsBackground = false
        view.isRichText = true
        view.importsGraphics = false
        // The editor keeps one undo history for the whole note.
        view.allowsUndo = false
        view.isVerticallyResizable = false
        view.isHorizontallyResizable = false
        view.textContainerInset = .zero
        view.textContainer?.lineFragmentPadding = 0
        view.textContainer?.widthTracksTextView = true
        view.isAutomaticLinkDetectionEnabled = false
        view.isAutomaticDataDetectionEnabled = false
        view.usesFontPanel = false
        view.usesRuler = false
        view.usesFindBar = false
        view.setContentHuggingPriority(.defaultLow, for: .horizontal)
        return view
    }

    /// Code is typed as written: no curly quotes, no corrections.
    func configure(isCode: Bool, editable: Bool) {
        self.isCode = isCode
        isEditable = editable
        isSelectable = true
        isAutomaticQuoteSubstitutionEnabled = !isCode
        isAutomaticDashSubstitutionEnabled = !isCode
        isAutomaticTextReplacementEnabled = !isCode
        isAutomaticSpellingCorrectionEnabled = !isCode
        isContinuousSpellCheckingEnabled = !isCode
        isGrammarCheckingEnabled = false
    }

    // MARK: Size

    /// The height the text needs at the view's current width.
    func fittingHeight() -> CGFloat {
        guard let layout = textLayoutManager else { return frame.height }
        layout.ensureLayout(for: layout.documentRange)
        let used = layout.usageBoundsForTextContainer.height
        return ceil(max(used, EditorTheme.lineHeight(of: font ?? .systemFont(ofSize: EditorTheme.bodySize))))
    }

    // MARK: Lines

    /// One line of text as it is laid out, in the text view's coordinates.
    struct Line {
        var range: NSRange
        var frame: CGRect
        fileprivate var fragment: NSTextLineFragment
        /// Where the line's own text starts within the whole text.
        fileprivate var paragraphStart: Int
        fileprivate var origin: CGPoint
    }

    var lines: [Line] {
        guard let layout = textLayoutManager, let content = layout.textContentManager else { return [] }
        layout.ensureLayout(for: layout.documentRange)
        var lines: [Line] = []
        layout.enumerateTextLayoutFragments(from: layout.documentRange.location, options: [.ensuresLayout]) { fragment in
            let start = content.offset(from: layout.documentRange.location, to: fragment.rangeInElement.location)
            let origin = fragment.layoutFragmentFrame.origin
            for line in fragment.textLineFragments {
                lines.append(Line(
                    range: NSRange(location: start + line.characterRange.location, length: line.characterRange.length),
                    frame: line.typographicBounds.offsetBy(dx: origin.x, dy: origin.y),
                    fragment: line, paragraphStart: start, origin: origin))
            }
            return true
        }
        return lines
    }

    /// Which line the caret at `offset` is on. A caret at a line's end
    /// belongs to that line, except where the line ends in a line break.
    private func lineIndex(for offset: Int, in lines: [Line]) -> Int {
        for (index, line) in lines.enumerated() {
            let isLast = index == lines.count - 1
            let endsInBreak = line.range.length > 0
                && (string as NSString).character(at: line.range.upperBound - 1) == 10
            let end = endsInBreak || !isLast ? line.range.upperBound - 1 : line.range.upperBound
            if offset <= end || isLast { return index }
        }
        return 0
    }

    var caretIsOnFirstLine: Bool {
        let lines = lines
        return lines.count <= 1 || lineIndex(for: selectedRange().location, in: lines) == 0
    }

    var caretIsOnLastLine: Bool {
        let lines = lines
        return lines.count <= 1 || lineIndex(for: selectedRange().upperBound, in: lines) == lines.count - 1
    }

    /// How far from the left edge the caret is, for keeping its column when
    /// it moves to another block.
    var caretX: CGFloat {
        let lines = lines
        guard !lines.isEmpty else { return 0 }
        let offset = selectedRange().location
        let line = lines[lineIndex(for: offset, in: lines)]
        return line.origin.x + line.fragment.locationForCharacter(at: offset - line.paragraphStart).x
    }

    /// The place in the text nearest to `x` on the first or the last line.
    func offset(nearestTo x: CGFloat, onFirstLine first: Bool) -> Int {
        let lines = lines
        guard let line = first ? lines.first : lines.last else { return 0 }
        let point = CGPoint(x: x - line.origin.x, y: line.fragment.typographicBounds.midY)
        var index = line.fragment.characterIndex(for: point)
        // The index is of the character under the point. The caret goes
        // after it when the point is past its middle.
        if index < line.fragment.characterRange.upperBound, line.fragment.fractionOfDistanceThroughGlyph(for: point) > 0.5 {
            index += 1
        }
        var offset = line.paragraphStart + index
        let text = string as NSString
        // Never after the line break that ends the line.
        if offset > line.range.location, offset == line.range.upperBound, offset <= text.length,
            text.character(at: offset - 1) == 10
        {
            offset -= 1
        }
        return min(max(offset, line.range.location), text.length)
    }

    // MARK: Focus

    override func becomeFirstResponder() -> Bool {
        let became = super.becomeFirstResponder()
        if became {
            needsDisplay = true
            editor?.textViewDidFocus(self)
        }
        return became
    }

    override func resignFirstResponder() -> Bool {
        needsDisplay = true
        return super.resignFirstResponder()
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        guard let placeholder, string.isEmpty, window?.firstResponder === self, !hasMarkedText() else { return }
        var attributes = typingAttributes
        attributes[.foregroundColor] = NSColor.tertiaryLabelColor
        attributes[.editorStyle] = nil
        NSAttributedString(string: placeholder, attributes: attributes).draw(at: .zero)
    }

    // MARK: Keys and commands

    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        // Every view in the window is offered a key equivalent. Only the
        // one being typed in answers.
        guard window?.firstResponder === self, let editor,
            editor.handleKeyEquivalent(event, in: self)
        else { return super.performKeyEquivalent(with: event) }
        return true
    }

    @objc func applyEditorFormat(_ sender: Any?) {
        guard let request = sender as? FormatRequest else { return }
        editor?.apply(request.format)
    }

    // The Edit menu's Undo and Redo arrive here and go to the editor's own
    // history, which covers the whole note.
    @objc func undo(_ sender: Any?) { editor?.undo() }
    @objc func redo(_ sender: Any?) { editor?.redo() }

    override func validateUserInterfaceItem(_ item: NSValidatedUserInterfaceItem) -> Bool {
        switch item.action {
        case #selector(undo(_:)): return editor?.canUndo ?? false
        case #selector(redo(_:)): return editor?.canRedo ?? false
        default: return super.validateUserInterfaceItem(item)
        }
    }

    override func paste(_ sender: Any?) {
        guard let editor, editor.paste(from: .general) else { return }
    }

    override func pasteAsPlainText(_ sender: Any?) {
        guard let editor, editor.paste(from: .general, plainOnly: true) else { return }
    }

    override func copy(_ sender: Any?) {
        // The text view offers its selection to other apps as it always
        // does. The editor adds the same text in its own form.
        super.copy(sender)
        editor?.addCopiedText(to: .general)
    }

    override func cut(_ sender: Any?) {
        guard selectedRange().length > 0 else { return }
        copy(sender)
        insertText("", replacementRange: selectedRange())
    }

    // MARK: Mouse

    override func mouseDown(with event: NSEvent) {
        // A shift-click from another block selects the blocks between.
        if event.modifierFlags.contains(.shift), editor?.extendSelection(toBlock: blockID) == true { return }
        editor?.textViewWillTrackMouse(self)
        // This returns when the button is let go.
        super.mouseDown(with: event)
        editor?.textViewDidTrackMouse(self)
    }

    // Only text is dropped into the text itself. Picture files go to the
    // page around it, which makes image blocks of them. A file dragged from
    // the Finder also carries its name as text, so the text view has to be
    // told not to take it.
    override var acceptableDragTypes: [NSPasteboard.PasteboardType] { [.string] }

    private func pageTaking(_ info: NSDraggingInfo) -> BlockEditorView? {
        guard let page = editor?.page, page.takesFiles(info) else { return nil }
        return page
    }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        pageTaking(sender)?.draggingEntered(sender) ?? super.draggingEntered(sender)
    }

    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation {
        pageTaking(sender)?.draggingUpdated(sender) ?? super.draggingUpdated(sender)
    }

    override func draggingExited(_ sender: NSDraggingInfo?) {
        editor?.page.draggingExited(sender)
        super.draggingExited(sender)
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        pageTaking(sender)?.performDragOperation(sender) ?? super.performDragOperation(sender)
    }
}
