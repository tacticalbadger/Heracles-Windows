import Foundation

/// Makes ids for new blocks. Random in the app, in sequence in tests so that
/// a test can name the blocks an edit creates.
public struct BlockIDs: Sendable {
    private var prefix: String?
    private var count = 0

    /// Ids like the web editor made: lowercase UUIDs.
    public static var random: BlockIDs { BlockIDs(prefix: nil) }

    /// `n1`, `n2`, and so on.
    public static func sequential(prefix: String = "n") -> BlockIDs {
        BlockIDs(prefix: prefix)
    }

    private init(prefix: String?) {
        self.prefix = prefix
    }

    public mutating func make() -> String {
        guard let prefix else { return UUID().uuidString.lowercased() }
        count += 1
        return "\(prefix)\(count)"
    }
}

/// One block of a note.
///
/// The stored document nests lists the way HTML does: a list holds items,
/// an item holds a paragraph and then anything else. Here a list item is a
/// block of its own with its text on it and the rest as its children, and
/// the list around it is implied by its neighbours. That is the shape the
/// editor works in: every block is a row you can move.
public struct Block: Equatable, Identifiable, Sendable {
    public enum Kind: Equatable, Sendable {
        case paragraph, heading
        /// Items of a bullet list, a numbered list and a task list.
        case bullet, numbered, todo
        case code
        /// Blocks that hold other blocks and have no text of their own.
        case quote, callout, toggle
        /// A row of columns, and one column of it.
        case columns, column
        case divider, image
        /// A block this editor does not know. It is kept as stored.
        case unknown
    }

    /// The block's id. Links to a block use it. Rows, columns and unknown
    /// blocks have no stored id, and theirs lasts only while the note is open.
    public var id: String
    public var kind: Kind
    /// The block's text. Empty for blocks that have none.
    public var text: RichText
    public var children: [Block]
    /// Attributes as stored, apart from the id: a heading's level, a task's
    /// state, and any this editor does not know.
    public var attrs: [String: JSONValue]
    /// For a list item, the id of the stored paragraph that holds its text.
    public var paragraphID: String?
    /// True for an item that begins a list of its own although the block
    /// before it is an item of the same kind.
    public var startsList: Bool
    /// The stored attributes of the list an item begins, such as the number
    /// a numbered list starts from. Only the first item's are used.
    public var listAttrs: [String: JSONValue]
    /// An unknown block, as stored.
    public var raw: JSONValue?

    public init(
        id: String, kind: Kind, text: RichText = RichText(), children: [Block] = [],
        attrs: [String: JSONValue] = [:], paragraphID: String? = nil
    ) {
        self.id = id
        self.kind = kind
        self.text = text
        self.children = children
        self.attrs = attrs
        self.paragraphID = paragraphID
        startsList = false
        listAttrs = [:]
        raw = nil
    }

    // MARK: What kind of block

    public var isListItem: Bool { kind == .bullet || kind == .numbered || kind == .todo }
    /// True for blocks whose text the user types into.
    public var hasText: Bool { kind == .paragraph || kind == .heading || isListItem || kind == .code }
    /// True for blocks that only hold other blocks.
    public var isContainer: Bool {
        kind == .quote || kind == .callout || kind == .toggle || kind == .columns || kind == .column
    }
    /// True for blocks that may have children at all.
    public var holdsBlocks: Bool { isListItem || isContainer }

    // MARK: Attributes by name

    /// A heading's level, 1 to 6.
    public var level: Int {
        get { min(max(attrs["level"]?.intValue ?? 1, 1), 6) }
        set { attrs["level"] = .number(Double(min(max(newValue, 1), 6))) }
    }

    public var isChecked: Bool {
        get { attrs["checked"]?.boolValue ?? false }
        set { attrs["checked"] = .bool(newValue) }
    }

    public var language: String {
        get { attrs["language"]?.stringValue ?? "plaintext" }
        set { attrs["language"] = .string(newValue) }
    }

    public var emoji: String {
        get { attrs["emoji"]?.stringValue ?? "💡" }
        set { attrs["emoji"] = .string(newValue) }
    }

    /// Whether a toggle shows more than its first block.
    public var isOpen: Bool {
        get { attrs["open"]?.boolValue ?? true }
        set { attrs["open"] = .bool(newValue) }
    }

    /// A column's share of its row, as a weight. Equal weights are equal widths.
    public var width: Double {
        get {
            let stored = attrs["width"]?.doubleValue ?? 1
            return stored > 0 ? stored : 1
        }
        set { attrs["width"] = .number(newValue) }
    }

    public var imageSource: String? { attrs["src"]?.stringValue }
}

/// A note's content as the editor holds it: a tree of blocks.
public struct EditorDocument: Equatable, Sendable {
    public var blocks: [Block]

    public init(blocks: [Block]) {
        self.blocks = blocks
    }
}

// MARK: - Reading and writing the stored document

private let listTypes: [String: Block.Kind] = ["bulletList": .bullet, "orderedList": .numbered, "taskList": .todo]

extension EditorDocument {
    /// Read a stored document. `nil` when the text is not one.
    ///
    /// Every block ends up with an id of its own: a block stored without one
    /// is given one, and so is the second block to carry the same id.
    public init?(json: String, ids: inout BlockIDs) {
        guard let root = JSONValue(text: json), root["type"]?.stringValue == "doc" else { return nil }
        var reader = Reader(ids: ids)
        blocks = reader.blocks(root["content"]?.arrayValue ?? [])
        if blocks.isEmpty {
            blocks = [Block(id: reader.ids.make(), kind: .paragraph)]
        }
        ids = reader.ids
    }

    /// The document to store.
    public var json: String {
        JSONValue.object(["type": .string("doc"), "content": .array(Self.nodes(blocks))]).text
    }

    /// Blocks as the nodes of a stored document.
    static func nodes(_ blocks: [Block]) -> [JSONValue] {
        var nodes: [JSONValue] = []
        var index = 0
        while index < blocks.count {
            let block = blocks[index]
            guard block.isListItem else {
                nodes.append(node(block))
                index += 1
                continue
            }
            // Items of one kind in a row are one list, until an item says
            // it begins another.
            var items = [item(block)]
            index += 1
            while index < blocks.count, blocks[index].kind == block.kind, !blocks[index].startsList {
                items.append(item(blocks[index]))
                index += 1
            }
            let type = listTypes.first { $0.value == block.kind }!.key
            var list: [String: JSONValue] = ["type": .string(type), "content": .array(items)]
            if !block.listAttrs.isEmpty { list["attrs"] = .object(block.listAttrs) }
            nodes.append(.object(list))
        }
        return nodes
    }

    private static func item(_ block: Block) -> JSONValue {
        var paragraph: [String: JSONValue] = ["type": .string("paragraph")]
        if let id = block.paragraphID { paragraph["attrs"] = .object(["blockId": .string(id)]) }
        let inline = block.text.inlineJSON
        if !inline.isEmpty { paragraph["content"] = .array(inline) }
        var attrs = block.attrs
        attrs["blockId"] = .string(block.id)
        if block.kind == .todo, attrs["checked"] == nil { attrs["checked"] = .bool(false) }
        return .object([
            "type": .string(block.kind == .todo ? "taskItem" : "listItem"),
            "attrs": .object(attrs),
            "content": .array([.object(paragraph)] + nodes(block.children)),
        ])
    }

    private static func node(_ block: Block) -> JSONValue {
        var attrs = block.attrs
        var node: [String: JSONValue] = [:]
        switch block.kind {
        case .paragraph, .heading:
            node["type"] = .string(block.kind == .heading ? "heading" : "paragraph")
            attrs["blockId"] = .string(block.id)
            let inline = block.text.inlineJSON
            if !inline.isEmpty { node["content"] = .array(inline) }
        case .code:
            node["type"] = .string("codeMirrorBlock")
            attrs["blockId"] = .string(block.id)
            attrs["language"] = .string(block.language)
            attrs["code"] = .string(block.text.string)
        case .quote, .callout, .toggle:
            let types: [Block.Kind: String] = [.quote: "blockquote", .callout: "calloutBlock", .toggle: "toggleBlock"]
            node["type"] = .string(types[block.kind]!)
            attrs["blockId"] = .string(block.id)
            // These must hold at least one block to be a valid document.
            let inside = nodes(block.children)
            node["content"] = .array(inside.isEmpty ? [.object(["type": .string("paragraph")])] : inside)
        case .columns:
            node["type"] = .string("columnBlock")
            node["content"] = .array(nodes(block.children))
        case .column:
            node["type"] = .string("column")
            node["content"] = .array(nodes(block.children))
        case .divider:
            node["type"] = .string("horizontalRule")
            attrs["blockId"] = .string(block.id)
        case .image:
            node["type"] = .string("image")
            attrs["blockId"] = .string(block.id)
        case .unknown:
            return block.raw ?? .object(["type": .string("paragraph")])
        case .bullet, .numbered, .todo:
            // Written by `nodes`, which knows the list around them.
            return item(block)
        }
        if !attrs.isEmpty { node["attrs"] = .object(attrs) }
        return .object(node)
    }
}

private struct Reader {
    var ids: BlockIDs
    private var seen: Set<String> = []

    init(ids: BlockIDs) {
        self.ids = ids
    }

    /// The id stored on a node, or a new one when it has none or when an
    /// earlier block already has it.
    private mutating func id(_ attrs: [String: JSONValue]) -> String {
        if let stored = attrs["blockId"]?.stringValue, !stored.isEmpty, seen.insert(stored).inserted {
            return stored
        }
        var made = ids.make()
        while !seen.insert(made).inserted { made = ids.make() }
        return made
    }

    mutating func blocks(_ nodes: [JSONValue]) -> [Block] {
        var blocks: [Block] = []
        for node in nodes {
            let type = node["type"]?.stringValue ?? ""
            if let kind = listTypes[type] {
                let attrs = node["attrs"]?.objectValue ?? [:]
                for (index, itemNode) in (node["content"]?.arrayValue ?? []).enumerated() {
                    var block = item(itemNode, kind: kind)
                    if index == 0 {
                        block.listAttrs = attrs
                        block.startsList = blocks.last?.kind == kind
                    }
                    blocks.append(block)
                }
            } else {
                blocks.append(block(node, type: type))
            }
        }
        return blocks
    }

    private mutating func item(_ node: JSONValue, kind: Block.Kind) -> Block {
        var attrs = node["attrs"]?.objectValue ?? [:]
        var block = Block(id: id(attrs), kind: kind)
        attrs["blockId"] = nil
        block.attrs = attrs
        var content = node["content"]?.arrayValue ?? []
        // An item's text is its first paragraph. Whatever follows is nested in it.
        if let first = content.first, first["type"]?.stringValue == "paragraph" {
            block.text = RichText(inline: first["content"]?.arrayValue ?? [])
            block.paragraphID = id(first["attrs"]?.objectValue ?? [:])
            content.removeFirst()
        } else {
            block.paragraphID = id([:])
        }
        block.children = blocks(content)
        return block
    }

    private mutating func block(_ node: JSONValue, type: String) -> Block {
        var attrs = node["attrs"]?.objectValue ?? [:]
        let content = node["content"]?.arrayValue ?? []
        let kinds: [String: Block.Kind] = [
            "paragraph": .paragraph, "heading": .heading, "blockquote": .quote, "calloutBlock": .callout,
            "toggleBlock": .toggle, "columnBlock": .columns, "column": .column, "horizontalRule": .divider,
            "codeMirrorBlock": .code, "image": .image,
        ]
        guard let kind = kinds[type] else {
            var block = Block(id: id(attrs), kind: .unknown)
            block.raw = node
            return block
        }
        var block = Block(id: id(attrs), kind: kind)
        attrs["blockId"] = nil
        switch kind {
        case .paragraph, .heading:
            block.text = RichText(inline: content)
        case .code:
            block.text = RichText(attrs["code"]?.stringValue ?? "")
            attrs["code"] = nil
        case .quote, .callout, .toggle, .columns, .column:
            block.children = blocks(content)
        default:
            break
        }
        block.attrs = attrs
        return block
    }
}
