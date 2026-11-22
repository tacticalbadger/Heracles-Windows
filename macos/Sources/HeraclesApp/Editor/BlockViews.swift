import AppKit

/// The view of one block. Subclasses draw the kinds of block. Each lays out
/// its own content and the views of the blocks inside it, for a given width,
/// and says how tall that came out.
@MainActor
class BlockView: NSView {
    private(set) var block: Block
    /// Weak, so a view that happens to outlive its editor does nothing
    /// instead of reaching for an object that is gone.
    private(set) weak var editor: BlockEditorController?
    /// The views of the blocks inside this one, in order.
    var childViews: [BlockView] = []
    /// A numbered item's number.
    private(set) var ordinal = 1

    override var isFlipped: Bool { true }

    required init(block: Block, editor: BlockEditorController) {
        self.block = block
        self.editor = editor
        super.init(frame: .zero)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("not used") }

    /// The class that shows a kind of block.
    static func viewClass(for kind: Block.Kind) -> BlockView.Type {
        switch kind {
        case .paragraph, .heading, .bullet, .numbered, .todo: TextBlockView.self
        case .code: CodeBlockView.self
        case .quote: QuoteBlockView.self
        case .callout: CalloutBlockView.self
        case .toggle: ToggleBlockView.self
        case .columns: ColumnsBlockView.self
        case .column: ColumnBlockView.self
        case .divider: DividerBlockView.self
        case .image: ImageBlockView.self
        case .unknown: UnknownBlockView.self
        }
    }

    static func make(for block: Block, editor: BlockEditorController) -> BlockView {
        viewClass(for: block.kind).init(block: block, editor: editor)
    }

    /// Show the block as it now is. Called after every edit, for every
    /// block, so it must cost little when nothing changed.
    func configure(_ block: Block, ordinal: Int) {
        let changed = block != self.block || ordinal != self.ordinal
        self.block = block
        self.ordinal = ordinal
        if changed { needsDisplay = true }
    }

    /// Lay out for `width` and return the height needed.
    func layout(width: CGFloat) -> CGFloat { 0 }

    /// The text view of the block's own text, if it has any.
    var textView: BlockTextView? { nil }

    /// Extra space above the block.
    var topMargin: CGFloat { EditorTheme.topMargin(for: block) }

    /// The height of the block's own first line, which the block handle is
    /// centered on.
    var firstLineHeight: CGFloat { 22 }

    /// Stack the views of the blocks inside this one. Returns where the
    /// stack ends.
    func stack(_ views: [BlockView], x: CGFloat, y: CGFloat, width: CGFloat) -> CGFloat {
        var y = y
        var isFirst = true
        for view in views where !view.isHidden {
            y += isFirst ? 0 : EditorTheme.blockSpacing + view.topMargin
            let height = view.layout(width: max(width, 40))
            view.frame = NSRect(x: x, y: y, width: max(width, 40), height: height)
            y += height
            isFirst = false
        }
        return y
    }

    /// A click on a block that has no text selects the block.
    func selectOnClick(_ event: NSEvent) {
        editor?.selectBlocks([block.id], extending: event.modifierFlags.contains(.shift))
    }

    var isEditable: Bool { editor?.isEditable ?? false }

    /// Write to a text view without the editor taking it for typing.
    func quietly(_ work: () -> Void) {
        if let editor { editor.withoutTextCallbacks(work) } else { work() }
    }
}

// MARK: - Text

/// A paragraph, a heading, or a list item with its marker.
final class TextBlockView: BlockView {
    private let text: BlockTextView
    private var checkbox: NSButton?
    private var shown: (text: RichText, kind: Block.Kind, attrs: [String: JSONValue])?

    required init(block: Block, editor: BlockEditorController) {
        text = BlockTextView.make(editor: editor)
        super.init(block: block, editor: editor)
        addSubview(text)
    }

    override var textView: BlockTextView? { text }

    private var markerWidth: CGFloat { block.isListItem ? EditorTheme.indent : 0 }

    override var firstLineHeight: CGFloat {
        EditorTheme.lineHeight(of: EditorTheme.baseFont(for: block))
    }

    override func configure(_ block: Block, ordinal: Int) {
        super.configure(block, ordinal: ordinal)
        text.blockID = block.id
        text.configure(isCode: false, editable: isEditable)
        text.placeholder = block.kind == .heading ? "Heading \(block.level)" : block.isListItem ? "List" : "Type / for blocks"

        // The text view is only written to when the model's text is not
        // what it already shows. While the user types, it always is.
        if shown?.text != block.text || shown?.kind != block.kind || shown?.attrs != block.attrs {
            quietly {
                let selected = text.selectedRange()
                text.textStorage?.setAttributedString(EditorTheme.attributedString(for: block))
                text.typingAttributes = EditorTheme.attributes(for: block.text.style(before: selected.location), in: block)
                if selected.upperBound <= block.text.length { text.setSelectedRange(selected) }
            }
            shown = (block.text, block.kind, block.attrs)
        }

        if block.kind == .todo {
            let box = checkbox ?? makeCheckbox()
            box.state = block.isChecked ? .on : .off
            box.isEnabled = isEditable
        } else {
            checkbox?.removeFromSuperview()
            checkbox = nil
        }
    }

    /// Record what the text view now shows, after the user typed in it, so
    /// the next `configure` does not write the same text back.
    func noteShown(_ block: Block) {
        shown = (block.text, block.kind, block.attrs)
    }

    private func makeCheckbox() -> NSButton {
        let box = NSButton(checkboxWithTitle: "", target: self, action: #selector(toggleChecked))
        box.controlSize = .small
        box.setAccessibilityLabel("Done")
        addSubview(box)
        checkbox = box
        return box
    }

    @objc private func toggleChecked() {
        editor?.setChecked(block.id, checkbox?.state == .on)
    }

    override func layout(width: CGFloat) -> CGFloat {
        let textWidth = max(width - markerWidth, 40)
        text.setFrameSize(NSSize(width: textWidth, height: text.frame.height))
        let height = text.fittingHeight()
        text.frame = NSRect(x: markerWidth, y: 0, width: textWidth, height: height)
        if let checkbox {
            let size = checkbox.fittingSize
            checkbox.frame = NSRect(x: 2, y: max((firstLineHeight - size.height) / 2, 0), width: size.width, height: size.height)
        }
        guard !childViews.isEmpty else { return height }
        return stack(childViews, x: markerWidth, y: height + EditorTheme.blockSpacing, width: textWidth)
    }

    override func draw(_ dirtyRect: NSRect) {
        guard block.kind == .bullet || block.kind == .numbered else { return }
        let font = EditorTheme.baseFont(for: block)
        let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: NSColor.secondaryLabelColor]
        let marker = NSAttributedString(string: block.kind == .bullet ? "•" : "\(ordinal).", attributes: attributes)
        let size = marker.size()
        // A bullet sits in the middle of the marker's room. A number ends
        // just before the text, so the numbers of a list line up.
        let x = block.kind == .bullet ? (markerWidth - size.width) / 2 - 2 : markerWidth - size.width - 6
        marker.draw(at: NSPoint(x: max(x, 0), y: 0))
    }
}

// MARK: - Containers

final class QuoteBlockView: BlockView {
    override func layout(width: CGFloat) -> CGFloat {
        stack(childViews, x: 16, y: 2, width: width - 16) + 2
    }

    override func draw(_ dirtyRect: NSRect) {
        NSColor.labelColor.withAlphaComponent(0.75).setFill()
        NSBezierPath(roundedRect: NSRect(x: 0, y: 0, width: 3, height: bounds.height), xRadius: 1.5, yRadius: 1.5).fill()
    }
}

final class CalloutBlockView: BlockView {
    private let emoji = NSButton(title: "", target: nil, action: nil)

    required init(block: Block, editor: BlockEditorController) {
        super.init(block: block, editor: editor)
        emoji.isBordered = false
        emoji.font = .systemFont(ofSize: 18)
        emoji.target = self
        emoji.action = #selector(chooseEmoji)
        emoji.toolTip = "Change the icon"
        addSubview(emoji)
    }

    override func configure(_ block: Block, ordinal: Int) {
        super.configure(block, ordinal: ordinal)
        emoji.title = block.emoji
        emoji.isEnabled = isEditable
    }

    @objc private func chooseEmoji() {
        editor?.chooseEmoji(for: block.id, from: emoji)
    }

    override func layout(width: CGFloat) -> CGFloat {
        emoji.frame = NSRect(x: 10, y: 10, width: 26, height: 24)
        return stack(childViews, x: 44, y: 12, width: width - 44 - 14) + 12
    }

    override func draw(_ dirtyRect: NSRect) {
        EditorTheme.panelColor.setFill()
        NSBezierPath(roundedRect: bounds, xRadius: 6, yRadius: 6).fill()
    }
}

final class ToggleBlockView: BlockView {
    private let arrow = NSButton(title: "", target: nil, action: nil)

    required init(block: Block, editor: BlockEditorController) {
        super.init(block: block, editor: editor)
        arrow.isBordered = false
        arrow.imagePosition = .imageOnly
        arrow.target = self
        arrow.action = #selector(flip)
        addSubview(arrow)
    }

    override func configure(_ block: Block, ordinal: Int) {
        super.configure(block, ordinal: ordinal)
        arrow.image = NSImage(
            systemSymbolName: block.isOpen ? "chevron.down" : "chevron.right",
            accessibilityDescription: block.isOpen ? "Collapse" : "Expand")?
            .withSymbolConfiguration(.init(pointSize: 10, weight: .semibold))
        arrow.contentTintColor = .secondaryLabelColor
    }

    @objc private func flip() {
        editor?.setOpen(block.id, !block.isOpen)
    }

    override func layout(width: CGFloat) -> CGFloat {
        // A closed toggle shows its first block only.
        for (index, view) in childViews.enumerated() {
            view.isHidden = !block.isOpen && index > 0
        }
        let first = childViews.first?.firstLineHeight ?? 22
        arrow.frame = NSRect(x: 0, y: max((first - 18) / 2, 0), width: 18, height: 18)
        return stack(childViews, x: 22, y: 0, width: width - 22)
    }
}

/// A row of columns, with a divider to drag between each pair.
final class ColumnsBlockView: BlockView {
    private var grips: [ColumnGrip] = []

    override func layout(width: CGFloat) -> CGFloat {
        let columns = childViews
        guard !columns.isEmpty else { return 0 }
        let gap = EditorTheme.columnGap
        let available = max(width - gap * CGFloat(columns.count - 1), 40)
        let total = columns.reduce(0) { $0 + $1.block.width }
        var x: CGFloat = 0
        var tallest: CGFloat = 0
        for column in columns {
            let share = (available * column.block.width / total).rounded()
            let height = column.layout(width: share)
            column.frame = NSRect(x: x, y: 0, width: share, height: height)
            tallest = max(tallest, height)
            x += share + gap
        }

        while grips.count < columns.count - 1 {
            let grip = ColumnGrip(row: self, index: grips.count)
            grips.append(grip)
            addSubview(grip)
        }
        while grips.count > max(columns.count - 1, 0) { grips.removeLast().removeFromSuperview() }
        for (index, grip) in grips.enumerated() {
            grip.isHidden = !isEditable
            grip.frame = NSRect(x: columns[index].frame.maxX + gap / 2 - 5, y: 0, width: 10, height: tallest)
        }
        return tallest
    }

    /// Move the divider after column `index` by `distance`, taking width
    /// from one neighbour and giving it to the other.
    fileprivate func resize(after index: Int, by distance: CGFloat, from start: [Double]) {
        guard index + 1 < start.count else { return }
        let gap = EditorTheme.columnGap
        let available = Double(max(bounds.width - gap * CGFloat(start.count - 1), 40))
        let total = start.reduce(0, +)
        let pair = start[index] + start[index + 1]
        let least = total * 0.12
        let moved = min(max(start[index] + Double(distance) / available * total, least), pair - least)
        var widths = start
        widths[index] = moved
        widths[index + 1] = pair - moved
        editor?.setColumnWidths(block.id, widths)
    }

    fileprivate var widths: [Double] { childViews.map(\.block.width) }
}

private final class ColumnGrip: NSView {
    private unowned let row: ColumnsBlockView
    private let index: Int
    private var isHovered = false

    init(row: ColumnsBlockView, index: Int) {
        self.row = row
        self.index = index
        super.init(frame: .zero)
        addTrackingArea(NSTrackingArea(
            rect: .zero, options: [.mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect], owner: self))
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("not used") }

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: .resizeLeftRight)
    }

    override func mouseEntered(with event: NSEvent) {
        isHovered = true
        needsDisplay = true
    }

    override func mouseExited(with event: NSEvent) {
        isHovered = false
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        guard isHovered else { return }
        NSColor.controlAccentColor.withAlphaComponent(0.6).setFill()
        NSBezierPath(roundedRect: NSRect(x: bounds.midX - 1, y: 0, width: 2, height: bounds.height), xRadius: 1, yRadius: 1).fill()
    }

    override func mouseDown(with event: NSEvent) {
        guard let window else { return }
        let start = row.widths
        let origin = event.locationInWindow.x
        row.editor?.beginUndoGroup()
        while let next = window.nextEvent(matching: [.leftMouseDragged, .leftMouseUp]) {
            if next.type == .leftMouseUp { break }
            row.resize(after: index, by: next.locationInWindow.x - origin, from: start)
        }
    }
}

final class ColumnBlockView: BlockView {
    override func layout(width: CGFloat) -> CGFloat {
        stack(childViews, x: 0, y: 0, width: width)
    }
}

// MARK: - Blocks without text

final class DividerBlockView: BlockView {
    override func layout(width: CGFloat) -> CGFloat { 17 }

    override var firstLineHeight: CGFloat { 17 }

    override func draw(_ dirtyRect: NSRect) {
        EditorTheme.ruleColor.setFill()
        NSRect(x: 0, y: bounds.midY.rounded() - 0.5, width: bounds.width, height: 1).fill()
    }

    override func mouseDown(with event: NSEvent) {
        selectOnClick(event)
    }
}

final class ImageBlockView: BlockView {
    private var image: NSImage?
    private var source: String?

    /// True once the picture has been found and is being drawn.
    var hasPicture: Bool { image != nil }

    override func configure(_ block: Block, ordinal: Int) {
        super.configure(block, ordinal: ordinal)
        if source != block.imageSource {
            source = block.imageSource
            image = nil
            if let source {
                editor?.image(for: source) { [weak self] loaded in
                    guard let self, self.source == source else { return }
                    self.image = loaded
                    self.needsDisplay = true
                    self.editor?.blockHeightChanged()
                }
            }
        }
        let alt = block.attrs["alt"]?.stringValue
        toolTip = block.attrs["title"]?.stringValue ?? alt
        setAccessibilityElement(true)
        setAccessibilityRole(.image)
        setAccessibilityLabel(alt ?? "Image")
    }

    /// The size the picture is drawn at: its own size, or as wide as there
    /// is room for.
    private func drawnSize(in width: CGFloat) -> NSSize {
        guard let image, image.size.width > 0, image.size.height > 0 else { return NSSize(width: width, height: 64) }
        let scale = min(1, width / image.size.width)
        return NSSize(width: (image.size.width * scale).rounded(), height: (image.size.height * scale).rounded())
    }

    override func layout(width: CGFloat) -> CGFloat {
        drawnSize(in: width).height + 8
    }

    override var firstLineHeight: CGFloat { min(bounds.height, 40) }

    override func draw(_ dirtyRect: NSRect) {
        let size = drawnSize(in: bounds.width)
        let frame = NSRect(x: 0, y: 4, width: size.width, height: size.height)
        let shape = NSBezierPath(roundedRect: frame, xRadius: 6, yRadius: 6)
        guard let image else {
            // No picture to show: say what was meant to be here.
            EditorTheme.panelColor.setFill()
            shape.fill()
            let label = block.attrs["alt"]?.stringValue ?? "Image"
            let attributes: [NSAttributedString.Key: Any] = [
                .font: NSFont.systemFont(ofSize: 12), .foregroundColor: NSColor.secondaryLabelColor,
            ]
            NSAttributedString(string: "🖼  \(label)", attributes: attributes).draw(at: NSPoint(x: 12, y: frame.midY - 8))
            return
        }
        NSGraphicsContext.saveGraphicsState()
        shape.addClip()
        image.draw(in: frame, from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
        NSGraphicsContext.restoreGraphicsState()
    }

    override func mouseDown(with event: NSEvent) {
        selectOnClick(event)
    }
}

final class CodeBlockView: BlockView {
    private let text: BlockTextView
    private let language = NSPopUpButton(frame: .zero, pullsDown: false)
    private var shown: (text: RichText, editable: Bool)?

    static let languages = [
        "plaintext", "bash", "c", "cpp", "csharp", "css", "go", "html", "java", "javascript", "json", "kotlin",
        "markdown", "python", "ruby", "rust", "sql", "swift", "typescript", "yaml",
    ]

    required init(block: Block, editor: BlockEditorController) {
        text = BlockTextView.make(editor: editor)
        super.init(block: block, editor: editor)
        addSubview(text)
        language.isBordered = false
        language.controlSize = .small
        language.font = .systemFont(ofSize: 11)
        language.target = self
        language.action = #selector(chooseLanguage)
        language.contentTintColor = .secondaryLabelColor
        addSubview(language)
    }

    override var textView: BlockTextView? { text }

    override func configure(_ block: Block, ordinal: Int) {
        super.configure(block, ordinal: ordinal)
        text.blockID = block.id
        text.configure(isCode: true, editable: isEditable)
        if shown?.text != block.text {
            quietly {
                let selected = text.selectedRange()
                text.textStorage?.setAttributedString(EditorTheme.attributedString(for: block))
                text.typingAttributes = EditorTheme.attributes(for: .plain, in: block)
                if selected.upperBound <= block.text.length { text.setSelectedRange(selected) }
            }
        }
        shown = (block.text, isEditable)

        // A language this list does not have is still shown and kept.
        var names = Self.languages
        if !names.contains(block.language) { names.append(block.language) }
        if language.itemTitles != names {
            language.removeAllItems()
            language.addItems(withTitles: names)
        }
        language.selectItem(withTitle: block.language)
        language.isEnabled = isEditable
    }

    func noteShown(_ block: Block) {
        shown = (block.text, isEditable)
    }

    @objc private func chooseLanguage() {
        guard let chosen = language.titleOfSelectedItem else { return }
        editor?.setLanguage(block.id, chosen)
    }

    override var firstLineHeight: CGFloat { 40 }

    override func layout(width: CGFloat) -> CGFloat {
        let size = language.fittingSize
        language.frame = NSRect(x: width - size.width - 6, y: 4, width: size.width, height: size.height)
        let textWidth = max(width - 28, 40)
        text.setFrameSize(NSSize(width: textWidth, height: text.frame.height))
        let height = text.fittingHeight()
        text.frame = NSRect(x: 14, y: 26, width: textWidth, height: height)
        return height + 26 + 12
    }

    override func draw(_ dirtyRect: NSRect) {
        let shape = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5, dy: 0.5), xRadius: 6, yRadius: 6)
        EditorTheme.panelColor.setFill()
        shape.fill()
        EditorTheme.ruleColor.setStroke()
        shape.stroke()
    }
}

/// A block from a newer version of the app. It is kept, and cannot be edited.
final class UnknownBlockView: BlockView {
    override func layout(width: CGFloat) -> CGFloat { 34 }

    override func draw(_ dirtyRect: NSRect) {
        let shape = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5, dy: 0.5), xRadius: 6, yRadius: 6)
        shape.setLineDash([4, 3], count: 2, phase: 0)
        NSColor.tertiaryLabelColor.setStroke()
        shape.stroke()
        let attributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 12), .foregroundColor: NSColor.secondaryLabelColor,
        ]
        NSAttributedString(string: "This block needs a newer version of Heracles. It is kept as it is.", attributes: attributes)
            .draw(at: NSPoint(x: 12, y: 9))
    }

    override func mouseDown(with event: NSEvent) {
        selectOnClick(event)
    }
}
