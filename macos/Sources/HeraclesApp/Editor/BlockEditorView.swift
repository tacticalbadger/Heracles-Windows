import AppKit

/// The page of an open note: the views of its blocks, stacked, and what
/// floats over them (the block handle, the selection, the drop line and the
/// slash menu). It is the document view of a scroll view.
@MainActor
final class BlockEditorView: NSView {
    /// Weak: the editor owns this view, through its scroll view.
    private(set) weak var editor: BlockEditorController?
    private(set) var rootViews: [BlockView] = []
    private var views: [String: BlockView] = [:]
    private let overlay = SelectionOverlay()
    private let dropLine = NSView()
    private let handle = BlockHandleView()
    let slashMenu = SlashMenuView()
    private var hoveredBlock: String?
    private var fileDropTarget: DropTarget?

    /// Whole blocks that are selected.
    var selectedBlocks: [String] = [] {
        didSet { if selectedBlocks != oldValue { refreshOverlay() } }
    }
    /// Blocks a drag in progress would select.
    var pendingBlocks: [String] = [] {
        didSet { if pendingBlocks != oldValue { refreshOverlay() } }
    }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }

    init(editor: BlockEditorController) {
        self.editor = editor
        super.init(frame: NSRect(x: 0, y: 0, width: 600, height: 400))
        autoresizingMask = [.width]
        dropLine.wantsLayer = true
        dropLine.layer?.backgroundColor = NSColor.controlAccentColor.cgColor
        dropLine.layer?.cornerRadius = 1.5
        dropLine.isHidden = true
        handle.isHidden = true
        handle.page = self
        slashMenu.isHidden = true
        slashMenu.choose = { [weak editor] index in editor?.chooseSlashCommand(at: index) }
        for floating in [overlay, dropLine, handle, slashMenu] { addSubview(floating) }
        registerForDraggedTypes([.fileURL])
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("not used") }

    // MARK: Blocks to views

    /// Make the views match the document. A block keeps its view from one
    /// edit to the next, wherever it moves, so the text view being typed in
    /// is never thrown away under the user.
    func reconcile(_ document: EditorDocument) {
        guard let editor else { return }
        var old = views
        var new: [String: BlockView] = [:]

        func build(_ blocks: [Block], in container: NSView) -> [BlockView] {
            var built: [BlockView] = []
            var number = 0
            for (index, block) in blocks.enumerated() {
                if block.kind == .numbered {
                    let continues = index > 0 && blocks[index - 1].kind == .numbered && !block.startsList
                    number = continues ? number + 1 : block.listAttrs["start"]?.intValue ?? 1
                }
                let view: BlockView
                if let existing = old[block.id], type(of: existing) == BlockView.viewClass(for: block.kind) {
                    old[block.id] = nil
                    view = existing
                } else {
                    view = BlockView.make(for: block, editor: editor)
                }
                view.isHidden = false
                view.configure(block, ordinal: number)
                if view.superview !== container { container.addSubview(view) }
                view.childViews = build(block.children, in: view)
                built.append(view)
                new[block.id] = view
            }
            return built
        }

        rootViews = build(document.blocks, in: self)
        for stale in old.values { stale.removeFromSuperview() }
        views = new
        // What floats stays above the blocks.
        for floating in [overlay, dropLine, handle, slashMenu] { addSubview(floating) }
        if let hoveredBlock, views[hoveredBlock] == nil { hideHandle() }
        needsLayout = true
    }

    func blockView(for id: String) -> BlockView? { views[id] }
    func textView(for id: String) -> BlockTextView? { views[id]?.textView }

    /// A block's frame on the page.
    func frame(of id: String) -> NSRect? {
        views[id].map(frame(of:))
    }

    private func frame(of view: BlockView) -> NSRect {
        view.convert(view.bounds, to: self)
    }

    // MARK: Layout

    override func setFrameSize(_ newSize: NSSize) {
        let widthChanged = newSize.width != frame.width
        super.setFrameSize(newSize)
        if widthChanged { needsLayout = true }
    }

    override func layout() {
        super.layout()
        layoutBlocks()
    }

    /// The text column: as wide as the page allows, up to a comfortable
    /// reading width, and centered.
    private var column: (x: CGFloat, width: CGFloat) {
        let width = max(min(EditorTheme.contentWidth, bounds.width - EditorTheme.gutter * 2), 120)
        return (max((bounds.width - width) / 2, EditorTheme.gutter).rounded(), width.rounded())
    }

    func layoutBlocks() {
        // Before the page has been given its width there is nothing to
        // lay out for.
        guard bounds.width > 1 else { return }
        let column = column
        var y = EditorTheme.topPadding
        for (index, view) in rootViews.enumerated() {
            y += index == 0 ? 0 : EditorTheme.blockSpacing + view.topMargin
            let height = view.layout(width: column.width)
            view.frame = NSRect(x: column.x, y: y, width: column.width, height: height)
            y += height
        }
        // Never shorter than the window, so a click anywhere below the
        // text lands on the page.
        let visible = enclosingScrollView?.contentSize.height ?? 0
        let height = max(y + EditorTheme.bottomPadding, visible)
        if abs(frame.height - height) > 0.5 {
            super.setFrameSize(NSSize(width: frame.width, height: height))
        }
        overlay.frame = bounds
        refreshOverlay()
        editor?.pageDidLayout()
    }

    // MARK: Finding the block at a point

    /// The block whose row a point is in. A point beside a block, in the
    /// margin, counts as in its row. A quote, callout or toggle is found as
    /// a whole from the margin beside it, and the blocks inside it from
    /// within. `nearest` settles for the closest block where a column is
    /// shorter than its row.
    func blockView(at point: NSPoint, excluding: Set<String> = [], nearest: Bool = false) -> BlockView? {
        let slack = EditorTheme.blockSpacing / 2
        func search(_ candidates: [BlockView]) -> BlockView? {
            for view in candidates where !view.isHidden && !excluding.contains(view.block.id) {
                let frame = frame(of: view)
                guard point.y >= frame.minY - slack, point.y < frame.maxY + slack else { continue }
                if view is ColumnsBlockView {
                    let columns = view.childViews
                    let column = columns.last { point.x >= self.frame(of: $0).minX - EditorTheme.columnGap } ?? columns.first
                    guard let column else { return nil }
                    if let inside = search(column.childViews) { return inside }
                    return nearest ? column.childViews.last { !excluding.contains($0.block.id) } : nil
                }
                let isBox = view.block.kind == .quote || view.block.kind == .callout || view.block.kind == .toggle
                if isBox, point.x < frame.minX { return view }
                return search(view.childViews) ?? view
            }
            return nil
        }
        return search(rootViews)
    }

    // MARK: Selection, drop line and flash

    private func refreshOverlay() {
        overlay.rects = (selectedBlocks + pendingBlocks).compactMap { frame(of: $0)?.insetBy(dx: -6, dy: -2) }
    }

    /// Mark a block for a moment, after a link brought the reader to it.
    func flash(_ id: String) {
        guard let frame = frame(of: id) else { return }
        overlay.flash = frame.insetBy(dx: -6, dy: -2)
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) { [weak self] in
            self?.overlay.flash = nil
        }
    }

    private func showDropLine(for target: DropTarget?) {
        guard let target, let frame = frame(of: target.blockID) else {
            dropLine.isHidden = true
            return
        }
        let gap = EditorTheme.blockSpacing / 2
        switch target {
        case .before: dropLine.frame = NSRect(x: frame.minX, y: frame.minY - gap - 1.5, width: frame.width, height: 3)
        case .after: dropLine.frame = NSRect(x: frame.minX, y: frame.maxY + gap - 1.5, width: frame.width, height: 3)
        case .leftOf: dropLine.frame = NSRect(x: frame.minX - 10, y: frame.minY, width: 3, height: frame.height)
        case .rightOf: dropLine.frame = NSRect(x: frame.maxX + 7, y: frame.minY, width: 3, height: frame.height)
        }
        dropLine.isHidden = false
    }

    /// Where a block dropped at a point would land.
    func dropTarget(at point: NSPoint, moving: [String], allowColumns: Bool = true) -> DropTarget? {
        guard let document = editor?.document else { return nil }
        let excluded = Set(document.allBlocks.map(\.id).filter { id in moving.contains { document.isBlock(id, inside: $0) } })
        var view = blockView(at: point, excluding: excluded, nearest: true)
        if view == nil {
            // Above the first block or below the last.
            let candidates = rootViews.filter { !excluded.contains($0.block.id) }
            view = point.y < (candidates.first.map(frame(of:))?.minY ?? 0) ? candidates.first : candidates.last
        }
        guard let view else { return nil }
        let frame = frame(of: view)
        let id = view.block.id

        // The right-hand end of a block makes a column beside it.
        let path = document.path(to: id) ?? []
        let inColumn = path.count > 1 && document[Array(path.dropLast())].kind == .column
        if allowColumns, path.count == 1 || inColumn, point.x > frame.maxX - min(90, frame.width * 0.25) {
            return .rightOf(id)
        }
        // The top half of a block's own row is before it, and the bottom
        // half after it. Under a list item's own line is the head of the
        // blocks inside the item.
        let ownRow = view.textView.map { $0.frame.height } ?? frame.height
        if point.y < frame.minY + ownRow / 2 { return .before(id) }
        if view.block.isListItem, point.y < frame.minY + ownRow + EditorTheme.blockSpacing,
            let first = view.childViews.first(where: { !$0.isHidden && !excluded.contains($0.block.id) })
        {
            return .before(first.block.id)
        }
        return .after(id)
    }

    // MARK: The block handle

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        for area in trackingAreas { removeTrackingArea(area) }
        addTrackingArea(NSTrackingArea(
            rect: .zero, options: [.mouseMoved, .mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect], owner: self))
    }

    override func mouseMoved(with event: NSEvent) {
        guard editor?.isEditable == true else { return }
        showHandle(at: convert(event.locationInWindow, from: nil))
    }

    override func mouseExited(with event: NSEvent) {
        hideHandle()
    }

    func showHandle(at point: NSPoint) {
        guard let view = blockView(at: point) else {
            hideHandle()
            return
        }
        let frame = frame(of: view)
        hoveredBlock = view.block.id
        handle.blockID = view.block.id
        handle.frame = NSRect(
            x: frame.minX - BlockHandleView.size.width - 6,
            y: frame.minY + (view.firstLineHeight - BlockHandleView.size.height) / 2,
            width: BlockHandleView.size.width, height: BlockHandleView.size.height)
        handle.isHidden = false
    }

    func hideHandle() {
        hoveredBlock = nil
        handle.isHidden = true
    }

    /// The block the handle is showing for.
    var handleBlock: String? { handle.isHidden ? nil : hoveredBlock }

    /// Follow the mouse after it went down on the handle's grip: a drag
    /// moves the block, and a click opens its menu.
    fileprivate func trackGrip(_ event: NSEvent, block id: String) {
        guard let window else { return }
        let start = event.locationInWindow
        let moving = selectedBlocks.contains(id) ? selectedBlocks : [id]
        var isDragging = false
        var target: DropTarget?
        while let next = window.nextEvent(matching: [.leftMouseDragged, .leftMouseUp]) {
            if next.type == .leftMouseUp { break }
            let location = next.locationInWindow
            if !isDragging, hypot(location.x - start.x, location.y - start.y) > 4 {
                isDragging = true
                pendingBlocks = moving
            }
            guard isDragging else { continue }
            autoscroll(with: next)
            target = dropTarget(at: convert(location, from: nil), moving: moving)
            showDropLine(for: target)
        }
        dropLine.isHidden = true
        pendingBlocks = []
        if isDragging {
            if let target { editor?.move(moving, to: target) }
        } else {
            editor?.selectBlocks([id], extending: false)
            editor?.showBlockMenu(for: id, at: NSPoint(x: handle.frame.minX, y: handle.frame.maxY + 4))
        }
    }

    fileprivate func addBlock(after id: String) {
        editor?.addBlock(after: id)
    }

    // MARK: Mouse on the page itself

    override func mouseDown(with event: NSEvent) {
        guard let editor else { return }
        let point = convert(event.locationInWindow, from: nil)
        editor.closeSlashMenu()
        if let last = rootViews.last, point.y > frame(of: last).maxY {
            // Below the note: carry on writing at its end.
            editor.focusEnd()
            return
        }
        guard let view = blockView(at: point, nearest: true) else {
            editor.focusStart()
            return
        }
        // Beside a block: the caret goes to the nearer end of its text.
        if let text = view.textView, view.block.hasText {
            let end = point.x > frame(of: view).midX ? (text.string as NSString).length : 0
            editor.select(.caret(view.block.id, end))
        } else {
            editor.selectBlocks([view.block.id], extending: event.modifierFlags.contains(.shift))
        }
    }

    // MARK: Keys while whole blocks are selected

    override func keyDown(with event: NSEvent) {
        if editor?.handleBlockKey(event) != true { super.keyDown(with: event) }
    }

    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        guard window?.firstResponder === self, editor?.handleKeyEquivalent(event, in: nil) == true else {
            return super.performKeyEquivalent(with: event)
        }
        return true
    }

    @objc func applyEditorFormat(_ sender: Any?) {
        guard let request = sender as? FormatRequest else { return }
        editor?.apply(request.format)
    }

    @objc func copy(_ sender: Any?) { _ = editor?.copySelection(to: .general) }
    @objc func cut(_ sender: Any?) { _ = editor?.cutSelection(to: .general) }
    @objc func paste(_ sender: Any?) { _ = editor?.paste(from: .general) }
    @objc func delete(_ sender: Any?) { editor?.deleteSelectedBlocks() }
    @objc func undo(_ sender: Any?) { editor?.undo() }
    @objc func redo(_ sender: Any?) { editor?.redo() }
    override func selectAll(_ sender: Any?) { editor?.selectAllBlocks() }

    override func becomeFirstResponder() -> Bool {
        editor?.pageDidFocus()
        return true
    }

    // MARK: Width

    // The page is as wide as the scroll view shows, whatever happens to
    // that: a resized window, a split, a scroll bar coming or going.
    override func viewDidMoveToSuperview() {
        super.viewDidMoveToSuperview()
        NotificationCenter.default.removeObserver(self, name: NSView.frameDidChangeNotification, object: nil)
        guard let superview else { return }
        superview.postsFrameChangedNotifications = true
        NotificationCenter.default.addObserver(
            self, selector: #selector(fitWidth), name: NSView.frameDidChangeNotification, object: superview)
        fitWidth()
    }

    @objc private func fitWidth() {
        guard let superview, abs(frame.width - superview.bounds.width) > 0.5 else { return }
        setFrameSize(NSSize(width: superview.bounds.width, height: frame.height))
    }

    // MARK: Files dropped on the page

    /// True when a drag carries picture files, which become image blocks
    /// wherever on the page they are dropped.
    func takesFiles(_ info: NSDraggingInfo) -> Bool {
        !imageFiles(in: info).isEmpty
    }

    private func imageFiles(in info: NSDraggingInfo) -> [URL] {
        let urls = info.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL]
        return (urls ?? []).filter { BlockEditorController.imageType(of: $0) != nil }
    }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        draggingUpdated(sender)
    }

    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation {
        guard editor?.isEditable == true, !imageFiles(in: sender).isEmpty else { return [] }
        fileDropTarget = dropTarget(at: convert(sender.draggingLocation, from: nil), moving: [], allowColumns: false)
        showDropLine(for: fileDropTarget)
        return .copy
    }

    override func draggingExited(_ sender: NSDraggingInfo?) {
        dropLine.isHidden = true
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        dropLine.isHidden = true
        let files = imageFiles(in: sender)
        guard !files.isEmpty else { return false }
        return editor?.insertImages(from: files, at: fileDropTarget) ?? false
    }

    // MARK: The slash menu

    /// Put the slash menu under a place in a block's text, or above it when
    /// there is no room below.
    func placeSlashMenu(under textView: BlockTextView, at offset: Int) {
        let lines = textView.lines
        var anchor = NSRect(x: 0, y: 0, width: 0, height: 20)
        if let line = lines.last(where: { $0.range.location <= offset }) ?? lines.first {
            anchor = NSRect(x: textView.caretX, y: line.frame.minY, width: 0, height: line.frame.height)
        }
        anchor = textView.convert(anchor, to: self)
        let size = slashMenu.fittingSize
        var origin = NSPoint(x: min(anchor.minX, max(bounds.width - size.width - 8, 0)), y: anchor.maxY + 6)
        if origin.y + size.height > visibleRect.maxY, anchor.minY - size.height - 6 >= visibleRect.minY {
            origin.y = anchor.minY - size.height - 6
        }
        slashMenu.frame = NSRect(origin: origin, size: size)
    }
}

/// Draws the selection of whole blocks over them. Clicks pass through.
private final class SelectionOverlay: NSView {
    var rects: [NSRect] = [] {
        didSet { if rects != oldValue { needsDisplay = true } }
    }
    var flash: NSRect? {
        didSet { needsDisplay = true }
    }

    override var isFlipped: Bool { true }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func draw(_ dirtyRect: NSRect) {
        EditorTheme.selectionColor.setFill()
        for rect in rects { NSBezierPath(roundedRect: rect, xRadius: 4, yRadius: 4).fill() }
        if let flash {
            NSColor.systemYellow.withAlphaComponent(0.3).setFill()
            NSBezierPath(roundedRect: flash, xRadius: 4, yRadius: 4).fill()
        }
    }
}

/// What appears beside the block under the pointer: a button to add a
/// block below, and a grip to drag the block by or click for its menu.
private final class BlockHandleView: NSView {
    static let size = NSSize(width: 40, height: 22)
    weak var page: BlockEditorView?
    var blockID = ""
    private let add = NSButton(title: "", target: nil, action: nil)
    private let grip = GripView()

    override var isFlipped: Bool { true }

    init() {
        super.init(frame: NSRect(origin: .zero, size: Self.size))
        add.isBordered = false
        add.image = NSImage(systemSymbolName: "plus", accessibilityDescription: "Add a block below")?
            .withSymbolConfiguration(.init(pointSize: 11, weight: .medium))
        add.contentTintColor = .tertiaryLabelColor
        add.toolTip = "Add a block below"
        add.target = self
        add.action = #selector(addBlock)
        add.frame = NSRect(x: 0, y: 0, width: 20, height: 22)
        addSubview(add)
        grip.toolTip = "Drag to move. Click for the menu."
        grip.handle = self
        grip.frame = NSRect(x: 20, y: 0, width: 20, height: 22)
        addSubview(grip)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("not used") }

    @objc private func addBlock() {
        page?.addBlock(after: blockID)
    }

    fileprivate func gripDown(_ event: NSEvent) {
        page?.trackGrip(event, block: blockID)
    }
}

private final class GripView: NSView {
    weak var handle: BlockHandleView?

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: .openHand)
    }

    override func draw(_ dirtyRect: NSRect) {
        // Six dots, two across and three down.
        NSColor.tertiaryLabelColor.setFill()
        for column in 0..<2 {
            for row in 0..<3 {
                let dot = NSRect(x: bounds.midX - 4 + CGFloat(column) * 5, y: bounds.midY - 6.5 + CGFloat(row) * 5, width: 3, height: 3)
                NSBezierPath(ovalIn: dot).fill()
            }
        }
    }

    override func mouseDown(with event: NSEvent) {
        handle?.gripDown(event)
    }
}

/// A command in the slash menu.
struct SlashCommand: Equatable {
    var title: String
    var detail: String
    var symbol: String
    var target: BlockTarget

    static let all: [SlashCommand] = [
        SlashCommand(title: "Text", detail: "Plain paragraph", symbol: "text.alignleft", target: .paragraph),
        SlashCommand(title: "Heading 1", detail: "Big section heading", symbol: "1.square", target: .heading(1)),
        SlashCommand(title: "Heading 2", detail: "Medium section heading", symbol: "2.square", target: .heading(2)),
        SlashCommand(title: "Heading 3", detail: "Smaller section heading", symbol: "3.square", target: .heading(3)),
        SlashCommand(title: "Heading 4", detail: "Small heading", symbol: "4.square", target: .heading(4)),
        SlashCommand(title: "Bullet list", detail: "A list with bullets", symbol: "list.bullet", target: .bullet),
        SlashCommand(title: "Numbered list", detail: "A list with numbers", symbol: "list.number", target: .numbered),
        SlashCommand(title: "Task list", detail: "Things to tick off", symbol: "checklist", target: .todo),
        SlashCommand(title: "Quote", detail: "Set a passage apart", symbol: "text.quote", target: .quote),
        SlashCommand(title: "Callout", detail: "A panel with an icon", symbol: "lightbulb", target: .callout),
        SlashCommand(title: "Toggle", detail: "Blocks you can fold away", symbol: "chevron.right.square", target: .toggle),
        SlashCommand(title: "Divider", detail: "A line across the page", symbol: "minus", target: .divider),
        SlashCommand(title: "Code", detail: "A block of code", symbol: "chevron.left.forwardslash.chevron.right", target: .code),
    ]

    /// The commands whose names match what was typed after the slash.
    static func matching(_ query: String) -> [SlashCommand] {
        let query = query.lowercased()
        guard !query.isEmpty else { return all }
        let starts = all.filter { $0.title.lowercased().hasPrefix(query) }
        let contains = all.filter { !$0.title.lowercased().hasPrefix(query) && $0.title.lowercased().contains(query) }
        return starts + contains
    }
}

/// The list of blocks that opens when `/` is typed.
final class SlashMenuView: NSView {
    var choose: (Int) -> Void = { _ in }
    private static let rowHeight: CGFloat = 30
    private static let maxRows = 9

    var items: [SlashCommand] = [] {
        didSet {
            needsDisplay = true
            setFrameSize(fittingSize)
        }
    }
    var selected = 0 {
        didSet { needsDisplay = true }
    }

    override var isFlipped: Bool { true }

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        shadow = NSShadow()
        layer?.shadowOpacity = 0.25
        layer?.shadowRadius = 10
        layer?.shadowOffset = CGSize(width: 0, height: -3)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("not used") }

    /// The first row drawn, so the selected row is always among those shown.
    private var firstRow: Int {
        min(max(selected - Self.maxRows + 1, 0), max(items.count - Self.maxRows, 0))
    }

    override var fittingSize: NSSize {
        NSSize(width: 250, height: CGFloat(min(max(items.count, 1), Self.maxRows)) * Self.rowHeight + 8)
    }

    override func draw(_ dirtyRect: NSRect) {
        let shape = NSBezierPath(roundedRect: bounds, xRadius: 8, yRadius: 8)
        NSColor.windowBackgroundColor.setFill()
        shape.fill()
        NSColor.separatorColor.setStroke()
        shape.stroke()

        let title: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 13), .foregroundColor: NSColor.labelColor]
        let detail: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 11), .foregroundColor: NSColor.secondaryLabelColor,
        ]
        guard !items.isEmpty else {
            NSAttributedString(string: "No such block", attributes: detail).draw(at: NSPoint(x: 12, y: 12))
            return
        }
        for (row, index) in (firstRow..<min(firstRow + Self.maxRows, items.count)).enumerated() {
            let item = items[index]
            let frame = NSRect(x: 4, y: 4 + CGFloat(row) * Self.rowHeight, width: bounds.width - 8, height: Self.rowHeight)
            if index == selected {
                NSColor.controlAccentColor.withAlphaComponent(0.18).setFill()
                NSBezierPath(roundedRect: frame, xRadius: 5, yRadius: 5).fill()
            }
            if let symbol = NSImage(systemSymbolName: item.symbol, accessibilityDescription: nil)?
                .withSymbolConfiguration(.init(pointSize: 12, weight: .regular))
            {
                let tinted = symbol.copy() as? NSImage ?? symbol
                tinted.isTemplate = true
                let box = NSRect(x: frame.minX + 8, y: frame.midY - 8, width: 16, height: 16)
                NSColor.secondaryLabelColor.set()
                tinted.draw(in: box, from: .zero, operation: .sourceOver, fraction: 0.7, respectFlipped: true, hints: nil)
            }
            NSAttributedString(string: item.title, attributes: title).draw(at: NSPoint(x: frame.minX + 34, y: frame.minY + 7))
            let width = NSAttributedString(string: item.title, attributes: title).size().width
            NSAttributedString(string: item.detail, attributes: detail)
                .draw(at: NSPoint(x: frame.minX + 34 + width + 8, y: frame.minY + 9))
        }
    }

    override func mouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        let row = Int((point.y - 4) / Self.rowHeight) + firstRow
        if items.indices.contains(row) { choose(row) }
    }
}
