import Foundation

/// What a block can be turned into.
public enum BlockTarget: Hashable, Sendable {
    case paragraph
    case heading(Int)
    case bullet, numbered, todo
    case quote, callout, toggle
    case code
    case divider
}

/// Where a dragged block lands, relative to another block.
public enum DropTarget: Equatable, Sendable {
    case before(String)
    case after(String)
    /// Beside the block, as a column.
    case leftOf(String)
    case rightOf(String)

    public var blockID: String {
        switch self {
        case .before(let id), .after(let id), .leftOf(let id), .rightOf(let id): id
        }
    }
}

// The edits a note's blocks can undergo. Each is a plain change to the tree,
// with no view involved, and says where the selection should go afterwards.
extension EditorDocument {
    // MARK: Putting blocks in and taking them out

    /// Take a block out, with everything inside it.
    @discardableResult
    mutating func take(_ id: String) -> Block? {
        guard let path = path(to: id) else { return nil }
        var taken: Block?
        withSiblings(of: path) { siblings, index in taken = siblings.remove(at: index) }
        return taken
    }

    @discardableResult
    public mutating func insert(_ new: [Block], after id: String) -> Bool {
        guard let path = path(to: id) else { return false }
        withSiblings(of: path) { siblings, index in siblings.insert(contentsOf: new, at: index + 1) }
        return true
    }

    @discardableResult
    public mutating func insert(_ new: [Block], before id: String) -> Bool {
        guard let path = path(to: id) else { return false }
        withSiblings(of: path) { siblings, index in siblings.insert(contentsOf: new, at: index) }
        return true
    }

    /// Copies of blocks in which every block has a new id.
    static func fresh(_ blocks: [Block], ids: inout BlockIDs) -> [Block] {
        blocks.map { block in
            var copy = block
            copy.id = ids.make()
            if block.isListItem { copy.paragraphID = ids.make() }
            if case .object(var raw) = block.raw, case .object(var attrs) = raw["attrs"], attrs["blockId"] != nil {
                attrs["blockId"] = .string(copy.id)
                raw["attrs"] = .object(attrs)
                copy.raw = .object(raw)
            }
            copy.children = fresh(block.children, ids: &ids)
            return copy
        }
    }

    /// The column a block is in, if it is in one.
    private func column(holding id: String) -> Block? {
        guard let path = path(to: id) else { return nil }
        for length in stride(from: path.count - 1, to: 0, by: -1) {
            let ancestor = self[Array(path.prefix(length))]
            if ancestor.kind == .column { return ancestor }
        }
        return nil
    }

    // MARK: Keeping the tree valid

    /// Remove what an edit can leave behind: containers with nothing in
    /// them, rows with fewer than two columns, and blocks inside a block
    /// that cannot hold any.
    mutating func tidy() {
        func clean(_ blocks: [Block], inRow: Bool, inColumn: Bool) -> [Block] {
            var kept: [Block] = []
            for var block in blocks {
                block.children = clean(
                    block.children, inRow: block.kind == .columns, inColumn: inColumn || block.kind == .column)
                switch block.kind {
                case .quote, .callout, .toggle:
                    if !block.children.isEmpty { kept.append(block) }
                case .column:
                    if !inRow {
                        kept.append(contentsOf: block.children)
                    } else if !block.children.isEmpty {
                        kept.append(block)
                    }
                case .columns:
                    let columns = block.children.filter { $0.kind == .column }
                    if columns.count >= 2, !inColumn {
                        block.children = columns
                        kept.append(block)
                    } else {
                        // One column is not a row, and a row cannot sit in a column.
                        kept.append(contentsOf: columns.flatMap(\.children))
                    }
                case .bullet, .numbered, .todo:
                    kept.append(block)
                default:
                    let children = block.children
                    block.children = []
                    kept.append(block)
                    kept.append(contentsOf: children)
                }
            }
            return kept
        }
        blocks = clean(blocks, inRow: false, inColumn: false)
    }

    /// `tidy`, and a note is never left with no blocks at all.
    public mutating func normalize(ids: inout BlockIDs) {
        tidy()
        if blocks.isEmpty {
            blocks = [Block(id: ids.make(), kind: .paragraph)]
        }
    }

    // MARK: Return

    /// Split a block where the caret is, as Return does. Text that is
    /// selected is replaced by the split.
    public mutating func split(_ id: String, at range: Range<Int>, ids: inout BlockIDs) -> EditorSelection {
        guard var block = block(id), block.hasText, block.kind != .code, let path = path(to: id) else { return .none }
        block.text.replace(range, with: RichText())
        let offset = min(max(range.lowerBound, 0), block.text.length)
        let parentPath = Array(path.dropLast())
        let parent = parentPath.isEmpty ? nil : self[parentPath]
        let isLast = path[path.count - 1] == children(at: parentPath).count - 1
        self[path] = block

        if block.text.isEmpty, block.children.isEmpty {
            // Return on an empty item ends the list, one level at a time.
            if block.isListItem {
                if parent?.isListItem == true {
                    _ = outdent(id)
                    return .caret(id, 0)
                }
                return turn(id, into: .paragraph, ids: &ids)
            }
            // Return on an empty last line leaves what the line is inside.
            if block.kind == .paragraph, isLast, let parent,
                parent.isListItem || parent.kind == .quote || parent.kind == .callout || parent.kind == .toggle
            {
                take(id)
                insert([block], after: parent.id)
                normalize(ids: &ids)
                return .caret(id, 0)
            }
        }

        var new = Block(id: ids.make(), kind: block.kind, attrs: block.attrs)
        if block.isListItem { new.paragraphID = ids.make() }
        if block.kind == .todo { new.isChecked = false }

        if offset == 0, !block.text.isEmpty {
            // At the start, the text keeps its block, so a link to the
            // block still lands on the text. The empty block goes above.
            if block.kind == .heading {
                new.kind = .paragraph
                new.attrs = [:]
            }
            insert([new], before: id)
            return .caret(id, 0)
        }

        new.text = block.text.subtext(offset..<block.text.length)
        block.text = block.text.subtext(0..<offset)
        if block.kind == .heading, new.text.isEmpty {
            new.kind = .paragraph
            new.attrs = [:]
        }
        if block.isListItem {
            new.children = block.children
            block.children = []
        }
        self[path] = block
        insert([new], after: id)
        return .caret(new.id, 0)
    }

    // MARK: Delete

    /// Backspace with the caret at the start of a block. `nil` when there
    /// is nothing for it to do.
    public mutating func deleteBackward(atStartOf id: String, ids: inout BlockIDs) -> EditorSelection? {
        guard let block = block(id), block.hasText, let path = path(to: id) else { return nil }
        if block.kind == .code {
            return block.text.isEmpty ? turn(id, into: .paragraph, ids: &ids) : nil
        }
        // First the block stops being a heading or a list item.
        if block.kind != .paragraph {
            return turn(id, into: .paragraph, ids: &ids)
        }
        // Then it leaves the quote, callout or toggle it begins.
        if path.count > 1, path[path.count - 1] == 0 {
            let parent = self[Array(path.dropLast())]
            if parent.kind == .quote || parent.kind == .callout || parent.kind == .toggle {
                take(id)
                insert([block], before: parent.id)
                normalize(ids: &ids)
                return .caret(id, 0)
            }
        }
        // Then it joins whatever is above it.
        guard let previous = stop(before: id) else { return nil }
        if previous.kind == .code || !previous.hasText {
            if block.text.isEmpty {
                take(id)
                normalize(ids: &ids)
            }
            // Text is never poured into code, and a block without text is
            // selected so that a second Backspace removes it.
            return previous.hasText ? .caret(previous.id, previous.text.length) : .blocks([previous.id])
        }
        take(id)
        update(previous.id) { $0.text += block.text }
        normalize(ids: &ids)
        return .caret(previous.id, previous.text.length)
    }

    /// Forward delete with the caret at the end of a block.
    public mutating func deleteForward(atEndOf id: String) -> EditorSelection? {
        guard let block = block(id), block.hasText, block.kind != .code, let next = stop(after: id),
            next.kind != .code
        else { return nil }
        guard next.hasText, let nextPath = path(to: next.id) else { return .blocks([next.id]) }
        // What was inside the joined block stays where that block was.
        withSiblings(of: nextPath) { siblings, index in siblings.replaceSubrange(index...index, with: next.children) }
        update(id) { $0.text += next.text }
        tidy()
        return .caret(id, block.text.length)
    }

    // MARK: Indenting

    /// Move a list item under the item before it. False when it cannot be.
    public mutating func indent(_ id: String) -> Bool {
        guard let path = path(to: id), self[path].isListItem else { return false }
        let index = path[path.count - 1]
        let parentPath = Array(path.dropLast())
        var siblings = children(at: parentPath)
        guard index > 0, siblings[index - 1].isListItem else { return false }
        var moved = siblings.remove(at: index)
        moved.startsList = false
        siblings[index - 1].children.append(moved)
        setChildren(siblings, at: parentPath)
        return true
    }

    /// Move a block out of the list item it is in, to follow that item.
    /// The items below it go along as its children, so nothing changes
    /// order on the page.
    public mutating func outdent(_ id: String) -> Bool {
        guard let path = path(to: id), path.count >= 2 else { return false }
        let parentPath = Array(path.dropLast())
        var parent = self[parentPath]
        guard parent.isListItem else { return false }
        let index = path[path.count - 1]
        var block = parent.children[index]
        let following = Array(parent.children[(index + 1)...])
        if block.isListItem {
            block.children += following
            parent.children.removeSubrange(index...)
        } else {
            guard following.isEmpty else { return false }
            parent.children.remove(at: index)
        }
        block.startsList = false
        self[parentPath] = parent
        withSiblings(of: parentPath) { siblings, parentIndex in siblings.insert(block, at: parentIndex + 1) }
        return true
    }

    // MARK: Turning a block into another kind

    @discardableResult
    public mutating func turn(_ id: String, into target: BlockTarget, ids: inout BlockIDs) -> EditorSelection {
        guard var block = block(id), let path = path(to: id) else { return .none }
        let containers: [BlockTarget: Block.Kind] = [.quote: .quote, .callout: .callout, .toggle: .toggle]

        if block.isContainer {
            guard block.kind != .columns, block.kind != .column else { return .none }
            let inside = EditorDocument(blocks: block.children).stops.first
            if let kind = containers[target] {
                block.kind = kind
                block.attrs = [:]
                self[path] = block
            } else {
                // Anything else lets the blocks inside out.
                withSiblings(of: path) { siblings, index in siblings.replaceSubrange(index...index, with: block.children) }
            }
            return inside.map { $0.hasText ? .caret($0.id, 0) : .blocks([$0.id]) } ?? .none
        }
        guard block.hasText else { return .none }

        if let kind = containers[target] {
            self[path] = Block(id: ids.make(), kind: kind, children: [block])
            return .caret(id, 0)
        }

        switch target {
        case .divider:
            let paragraph = Block(id: ids.make(), kind: .paragraph)
            if block.kind != .code, block.text.isEmpty, block.children.isEmpty {
                self[path] = Block(id: id, kind: .divider)
                insert([paragraph], after: id)
                return .caret(paragraph.id, 0)
            }
            // A block with text is kept, and the divider goes below it.
            let rule = Block(id: paragraph.id, kind: .divider)
            let after = Block(id: ids.make(), kind: .paragraph)
            insert([rule, after], after: id)
            return .caret(after.id, 0)
        case .code:
            let children = block.children
            let code = Block(id: id, kind: .code, text: RichText(block.text.string))
            withSiblings(of: path) { siblings, index in siblings.replaceSubrange(index...index, with: [code] + children) }
        case .paragraph, .heading, .bullet, .numbered, .todo:
            let kinds: [BlockTarget: Block.Kind] = [.paragraph: .paragraph, .bullet: .bullet, .numbered: .numbered, .todo: .todo]
            block.kind = kinds[target] ?? .heading
            block.attrs = [:]
            block.startsList = false
            block.listAttrs = [:]
            if case .heading(let level) = target { block.level = level }
            var after: [Block] = []
            if block.isListItem {
                if block.paragraphID == nil { block.paragraphID = ids.make() }
            } else {
                // Only a list item can hold blocks. The rest follow it.
                block.paragraphID = nil
                after = block.children
                block.children = []
            }
            withSiblings(of: path) { siblings, index in siblings.replaceSubrange(index...index, with: [block] + after) }
        case .quote, .callout, .toggle:
            break
        }
        return .caret(id, 0)
    }

    // MARK: Removing and copying

    /// Remove blocks, with everything inside them.
    public mutating func remove(_ removing: [String], ids: inout BlockIDs) -> EditorSelection {
        let present = removing.filter(contains)
        guard !present.isEmpty else { return .none }
        let before = stops
        func isGoing(_ block: Block) -> Bool { present.contains { isBlock(block.id, inside: $0) } }
        let above = before.firstIndex(where: isGoing).flatMap { $0 > 0 ? before[$0 - 1] : nil }
        let below = before.lastIndex(where: isGoing).flatMap { $0 + 1 < before.count ? before[$0 + 1] : nil }
        for id in present { take(id) }
        normalize(ids: &ids)

        if let above, contains(above.id) {
            return above.hasText ? .caret(above.id, above.text.length) : .blocks([above.id])
        }
        if let below, contains(below.id) {
            return below.hasText ? .caret(below.id, 0) : .blocks([below.id])
        }
        guard let first = stops.first else { return .none }
        return first.hasText ? .caret(first.id, 0) : .blocks([first.id])
    }

    /// Put a copy of a block after it. Returns the copy's id.
    public mutating func duplicate(_ id: String, ids: inout BlockIDs) -> String? {
        guard let block = block(id) else { return nil }
        var copy = Self.fresh([block], ids: &ids)[0]
        copy.startsList = false
        insert([copy], after: id)
        return copy.id
    }

    // MARK: Moving

    /// Move blocks to a new place. False, with nothing changed, when the
    /// move makes no sense: into itself, or a row of columns into a column.
    public mutating func move(_ moving: [String], to target: DropTarget, ids: inout BlockIDs) -> Bool {
        let targetID = target.blockID
        guard !moving.isEmpty, moving.allSatisfy(contains), let targetPath = path(to: targetID),
            !moving.contains(where: { isBlock(targetID, inside: $0) })
        else { return false }
        // A block inside another that is moving goes along with it.
        let roots = moving.filter { id in !moving.contains { $0 != id && isBlock(id, inside: $0) } }
        let blocks = roots.compactMap(block)
        guard !blocks.contains(where: { $0.kind == .column }) else { return false }
        let holdsRow = blocks.contains { block in
            EditorDocument(blocks: [block]).allBlocks.contains { $0.kind == .columns }
        }
        let column = column(holding: targetID)
        var target = target
        switch target {
        case .leftOf, .rightOf:
            if column == nil, targetPath.count > 1 {
                // Only a block at the top, or one in a column, can have a
                // column beside it. Elsewhere the block lands below.
                target = .after(targetID)
            } else if holdsRow {
                return false
            }
        case .before, .after:
            if column != nil, holdsRow { return false }
        }

        var taken = roots.compactMap { take($0) }
        for index in taken.indices { taken[index].startsList = false }
        switch target {
        case .before(let id):
            insert(taken, before: id)
        case .after(let id):
            insert(taken, after: id)
        case .leftOf(let id), .rightOf(let id):
            let isLeft = target == .leftOf(id)
            if let column {
                let added = Block(id: ids.make(), kind: .column, children: taken)
                if isLeft { insert([added], before: column.id) } else { insert([added], after: column.id) }
            } else if let path = path(to: id) {
                let beside = self[path]
                if beside.kind == .columns {
                    let added = Block(id: ids.make(), kind: .column, children: taken)
                    self[path].children.insert(added, at: isLeft ? 0 : beside.children.count)
                } else {
                    var row = Block(id: ids.make(), kind: .columns)
                    let first = Block(id: ids.make(), kind: .column, children: isLeft ? taken : [beside])
                    let second = Block(id: ids.make(), kind: .column, children: isLeft ? [beside] : taken)
                    row.children = [first, second]
                    self[path] = row
                }
            }
        }
        normalize(ids: &ids)
        return true
    }

    // MARK: Typing shortcuts

    /// Turn a paragraph into another kind of block when what has just been
    /// typed at its start is one of the Markdown beginnings: `# `, `- `,
    /// `1. `, `[] `, `> `, ``` ``` ```, or `---`. `caret` is where the caret
    /// is, which must be right after the beginning.
    public mutating func applyBlockShortcut(in id: String, caret: Int, ids: inout BlockIDs) -> EditorSelection? {
        guard let block = block(id), block.kind == .paragraph else { return nil }
        let string = block.text.string as NSString
        guard caret > 0, caret <= string.length else { return nil }
        let typed = string.substring(to: caret)

        // Two dashes become a long one as they are typed, when the system
        // is set to make smart dashes.
        if typed == "---" || typed == "—-", string.length == caret {
            update(id) { $0.text = RichText() }
            return turn(id, into: .divider, ids: &ids)
        }
        guard typed.hasSuffix(" ") else { return nil }
        let start = String(typed.dropLast())

        var target: BlockTarget
        var checked = false
        var number: Int?
        var language: String?
        switch start {
        case "#", "##", "###", "####", "#####", "######": target = .heading(start.count)
        case "-", "*", "+": target = .bullet
        case "[]", "[ ]": target = .todo
        case "[x]", "[X]":
            target = .todo
            checked = true
        case ">": target = .quote
        default:
            if start.count >= 2, start.count <= 10, let last = start.last, last == "." || last == ")",
                let value = Int(start.dropLast()), value >= 0
            {
                target = .numbered
                number = value
            } else if start.hasPrefix("```"),
                start.dropFirst(3).allSatisfy({ $0.isLetter || $0.isNumber || "+#_-".contains($0) })
            {
                target = .code
                language = String(start.dropFirst(3))
            } else {
                return nil
            }
        }

        update(id) { $0.text.replace(0..<caret, with: RichText()) }
        turn(id, into: target, ids: &ids)
        update(id) { block in
            if checked { block.isChecked = true }
            if let number, number != 1 { block.listAttrs["start"] = .number(Double(number)) }
            if let language, !language.isEmpty { block.language = language.lowercased() }
        }
        return .caret(id, 0)
    }

    // MARK: Pasting

    /// Put blocks in at the selection. One paragraph goes into the line
    /// being typed. Anything more goes below the block, or takes the place
    /// of an empty one or of selected blocks. Pasted blocks get new ids.
    public mutating func paste(_ pasted: [Block], at selection: EditorSelection, ids: inout BlockIDs) -> EditorSelection {
        guard !pasted.isEmpty else { return selection }
        func end(of blocks: [Block]) -> EditorSelection {
            guard let last = EditorDocument(blocks: blocks).stops.last else { return .none }
            return last.hasText ? .caret(last.id, last.text.length) : .blocks([last.id])
        }

        switch selection {
        case .none:
            let fresh = Self.fresh(pasted, ids: &ids)
            blocks.append(contentsOf: fresh)
            normalize(ids: &ids)
            return end(of: fresh)
        case .blocks(let selected):
            guard let first = selected.first(where: contains) else { return selection }
            let fresh = Self.fresh(pasted, ids: &ids)
            insert(fresh, before: first)
            for id in selected { take(id) }
            normalize(ids: &ids)
            return end(of: fresh)
        case .text(let id, let range):
            guard let block = block(id) else { return selection }
            if block.kind == .code {
                // Code takes only the words.
                let words = EditorDocument(blocks: pasted).allBlocks.filter(\.hasText).map(\.text.string)
                    .joined(separator: "\n")
                update(id) { $0.text.replace(range, with: RichText(words)) }
                return .caret(id, range.lowerBound + (words as NSString).length)
            }
            if pasted.count == 1, pasted[0].kind == .paragraph, block.hasText {
                update(id) { $0.text.replace(range, with: pasted[0].text) }
                return .caret(id, range.lowerBound + pasted[0].text.length)
            }
            let fresh = Self.fresh(pasted, ids: &ids)
            update(id) { $0.text.replace(range, with: RichText()) }
            if block.kind == .paragraph, block.text.length == range.count, block.children.isEmpty {
                insert(fresh, before: id)
                take(id)
            } else {
                insert(fresh, after: id)
            }
            normalize(ids: &ids)
            return end(of: fresh)
        }
    }

    // MARK: Selecting whole blocks

    /// The blocks to select when a selection runs from one block to
    /// another, in either direction. A block stands for everything inside
    /// it, and a container all of whose blocks are selected is selected in
    /// their place.
    public func blocks(from anchor: String, to head: String) -> [String] {
        let order = stops.map(\.id)
        guard let first = order.firstIndex(of: anchor), let second = order.firstIndex(of: head) else { return [] }
        let chosen = Set(order[min(first, second)...max(first, second)])

        func reduce(_ blocks: [Block]) -> [String] {
            var selected: [String] = []
            for block in blocks {
                if chosen.contains(block.id) {
                    selected.append(block.id)
                    continue
                }
                let shown = block.kind == .toggle && !block.isOpen ? Array(block.children.prefix(1)) : block.children
                let inside = reduce(shown)
                if block.isContainer, !shown.isEmpty, inside == shown.map(\.id) {
                    selected.append(block.id)
                } else {
                    selected.append(contentsOf: inside)
                }
            }
            return selected
        }
        return reduce(blocks)
    }

    /// Blocks as plain text, in Markdown's way of writing them, for the
    /// clipboard.
    public func plainText(of ids: [String]) -> String {
        Self.lines(ids.compactMap(block), indent: "").joined(separator: "\n")
    }

    static func lines(_ blocks: [Block], indent: String) -> [String] {
        var lines: [String] = []
        var number = 0
        func add(_ prefix: String, _ text: String) {
            // A line break inside a block starts a new line at the same depth.
            for (index, line) in text.components(separatedBy: "\n").enumerated() {
                lines.append(indent + (index == 0 ? prefix : String(repeating: " ", count: prefix.count)) + line)
            }
        }
        for (index, block) in blocks.enumerated() {
            let text = block.text.string
            switch block.kind {
            case .paragraph: add("", text)
            case .heading: add(String(repeating: "#", count: block.level) + " ", text)
            case .bullet: add("- ", text)
            case .todo: add(block.isChecked ? "- [x] " : "- [ ] ", text)
            case .numbered:
                let continues = index > 0 && blocks[index - 1].kind == .numbered && !block.startsList
                number = continues ? number + 1 : block.listAttrs["start"]?.intValue ?? 1
                add("\(number). ", text)
            case .code:
                let language = block.language == "plaintext" ? "" : block.language
                lines.append(indent + "```" + language)
                lines.append(contentsOf: text.components(separatedBy: "\n").map { indent + $0 })
                lines.append(indent + "```")
            case .divider: lines.append(indent + "---")
            case .image:
                lines.append(indent + "![\(block.attrs["alt"]?.stringValue ?? "")](\(block.imageSource ?? ""))")
            case .quote, .callout:
                lines.append(contentsOf: Self.lines(block.children, indent: indent + "> "))
            case .toggle, .columns, .column:
                lines.append(contentsOf: Self.lines(block.children, indent: indent))
            case .unknown: break
            }
            if block.isListItem {
                lines.append(contentsOf: Self.lines(block.children, indent: indent + "  "))
            }
        }
        return lines
    }
}

extension RichText {
    /// Turn a pair of Markdown marks into the mark they stand for, when the
    /// closing one has just been typed: `**bold**`, `*italic*`, `` `code` ``,
    /// `~~strike~~`, and the underscore forms. `caret` is where the caret is,
    /// right after the closing mark. `nil` when there is no such pair.
    public func applyingInlineShortcut(caret: Int) -> (text: RichText, caret: Int, mark: TextMark)? {
        let before = (string as NSString).substring(to: min(max(caret, 0), length)) as NSString
        let rules: [(String, TextMark)] = [
            ("**", .bold), ("__", .bold), ("~~", .strike), ("*", .italic), ("_", .italic), ("`", .code),
        ]
        for (mark, kind) in rules {
            let width = (mark as NSString).length
            guard before.length > width * 2, before.hasSuffix(mark) else { continue }
            let close = before.length - width
            let open = before.range(of: mark, options: .backwards, range: NSRange(location: 0, length: close))
            guard open.location != NSNotFound, open.upperBound < close else { continue }
            let inner = open.upperBound..<close
            let content = before.substring(with: NSRange(location: inner.lowerBound, length: inner.count))
            guard let first = content.first, let last = content.last, !first.isWhitespace, !last.isWhitespace,
                !content.contains(mark.first!), !content.contains("\n")
            else { continue }
            // The pair must begin a word: `2 * 3 * 4` and `snake_case_name`
            // are not marks. Code only must not sit against another backtick.
            if open.location > 0 {
                let previous = before.substring(with: NSRange(location: open.location - 1, length: 1))
                if mark == "`" ? previous == "`" : previous.first?.isWhitespace != true { continue }
            }
            let whole = open.location..<before.length
            guard !subtext(whole).spans.contains(where: { $0.style.code || $0.atom != nil }) else { continue }
            var marked = subtext(inner)
            marked.set(kind, true, in: 0..<marked.length)
            var result = self
            result.replace(whole, with: marked)
            return (result, open.location + marked.length, kind)
        }
        return nil
    }
}

extension RichText {
    /// True for text that is one web address and nothing else.
    public static func isWebAddress(_ text: String) -> Bool {
        let lower = text.lowercased()
        guard lower.hasPrefix("http://") || lower.hasPrefix("https://"), !text.contains(where: \.isWhitespace),
            let url = URL(string: text), let host = url.host
        else { return false }
        return !host.isEmpty
    }

    /// Link a web address that has just been typed, when the space after
    /// it is. `caret` is where the caret is, right after that space. `nil`
    /// when the word before it is not an address, or is a link already.
    public func autolinked(caret: Int) -> RichText? {
        let before = (string as NSString).substring(to: min(max(caret, 0), length)) as NSString
        guard before.length >= 2, before.hasSuffix(" ") else { return nil }
        let end = before.length - 1
        let gap = before.rangeOfCharacter(
            from: .whitespacesAndNewlines, options: .backwards, range: NSRange(location: 0, length: end))
        let start = gap.location == NSNotFound ? 0 : gap.upperBound
        guard start < end else { return nil }
        var word = before.substring(with: NSRange(location: start, length: end - start))
        // A full stop or a comma after an address is the sentence's, not its own.
        while let last = word.last, ".,;:!?)".contains(last) { word.removeLast() }
        let range = start..<(start + (word as NSString).length)
        guard Self.isWebAddress(word), !subtext(range).spans.contains(where: { $0.style.link != nil || $0.style.code })
        else { return nil }
        var linked = self
        linked.setLink(TextLink(href: word), in: range)
        return linked
    }
}
