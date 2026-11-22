import Foundation

/// Where a block is: the index to follow at each level, from the top.
typealias BlockPath = [Int]

extension EditorDocument {
    // MARK: Finding blocks

    func path(to id: String) -> BlockPath? {
        func search(_ blocks: [Block], _ prefix: BlockPath) -> BlockPath? {
            for (index, block) in blocks.enumerated() {
                if block.id == id { return prefix + [index] }
                if let found = search(block.children, prefix + [index]) { return found }
            }
            return nil
        }
        return search(blocks, [])
    }

    subscript(path: BlockPath) -> Block {
        get {
            var block = blocks[path[0]]
            for index in path.dropFirst() { block = block.children[index] }
            return block
        }
        set {
            func assign(_ blocks: inout [Block], _ path: ArraySlice<Int>) {
                guard let first = path.first else { return }
                if path.count == 1 {
                    blocks[first] = newValue
                } else {
                    assign(&blocks[first].children, path.dropFirst())
                }
            }
            assign(&blocks, path[...])
        }
    }

    public func block(_ id: String) -> Block? {
        path(to: id).map { self[$0] }
    }

    public func contains(_ id: String) -> Bool {
        path(to: id) != nil
    }

    /// The block that holds `id` directly, or `nil` at the top level.
    public func parent(of id: String) -> Block? {
        guard let path = path(to: id), path.count > 1 else { return nil }
        return self[Array(path.dropLast())]
    }

    /// Change one block in place. Does nothing when there is no such block.
    public mutating func update(_ id: String, _ change: (inout Block) -> Void) {
        guard let path = path(to: id) else { return }
        var block = self[path]
        change(&block)
        self[path] = block
    }

    /// The list of blocks a path's parent holds: the top level for `[]`.
    func children(at parent: BlockPath) -> [Block] {
        parent.isEmpty ? blocks : self[parent].children
    }

    mutating func setChildren(_ children: [Block], at parent: BlockPath) {
        if parent.isEmpty {
            blocks = children
        } else {
            self[parent].children = children
        }
    }

    /// Change the list of blocks that holds the block at `path`.
    mutating func withSiblings(of path: BlockPath, _ change: (inout [Block], Int) -> Void) {
        let parent = Array(path.dropLast())
        var siblings = children(at: parent)
        change(&siblings, path[path.count - 1])
        setChildren(siblings, at: parent)
    }

    // MARK: Order

    /// Every block, in the order it is read down the page.
    public var allBlocks: [Block] {
        var all: [Block] = []
        func walk(_ blocks: [Block]) {
            for block in blocks {
                all.append(block)
                walk(block.children)
            }
        }
        walk(blocks)
        return all
    }

    /// The blocks the caret can stop at, in order down the page: those with
    /// text, and those that stand alone such as an image. Blocks that only
    /// hold others are not stops, and neither is anything folded away in a
    /// closed toggle.
    public var stops: [Block] {
        var stops: [Block] = []
        func walk(_ blocks: [Block]) {
            for block in blocks {
                if !block.isContainer { stops.append(block) }
                let shown = block.kind == .toggle && !block.isOpen ? Array(block.children.prefix(1)) : block.children
                walk(shown)
            }
        }
        walk(blocks)
        return stops
    }

    public func stop(before id: String) -> Block? {
        let stops = stops
        guard let index = stops.firstIndex(where: { $0.id == id }), index > 0 else { return nil }
        return stops[index - 1]
    }

    public func stop(after id: String) -> Block? {
        let stops = stops
        guard let index = stops.firstIndex(where: { $0.id == id }), index + 1 < stops.count else { return nil }
        return stops[index + 1]
    }

    /// True when `id` is `ancestor` or somewhere inside it.
    public func isBlock(_ id: String, inside ancestor: String) -> Bool {
        guard let outer = path(to: ancestor), let inner = path(to: id) else { return false }
        return inner.count >= outer.count && Array(inner.prefix(outer.count)) == outer
    }
}
