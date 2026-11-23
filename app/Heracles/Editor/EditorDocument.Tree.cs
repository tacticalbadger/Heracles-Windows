namespace Heracles.Editor;

/// <summary>
/// Finding blocks in the tree, and the order they come in down the page.
///
/// A place in the tree is a path: the index to follow at each level, from
/// the top. A block found here is the block in the document, not a copy of
/// it: changing it changes the note.
/// </summary>
internal sealed partial class EditorDocument
{
    // Finding blocks

    internal int[]? PathTo(string id)
    {
        var path = new List<int>();

        bool Search(List<Block> blocks)
        {
            for (var index = 0; index < blocks.Count; index++)
            {
                path.Add(index);
                if (blocks[index].Id == id || Search(blocks[index].Children)) return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }

        return Search(Blocks) ? [.. path] : null;
    }

    /// <summary>The block at a path. The path must lead somewhere.</summary>
    internal Block At(ReadOnlySpan<int> path)
    {
        var block = Blocks[path[0]];
        foreach (var index in path[1..]) block = block.Children[index];
        return block;
    }

    public Block? Block(string id) => PathTo(id) is { } path ? At(path) : null;

    public bool Contains(string id) => PathTo(id) is not null;

    /// <summary>The block that holds <paramref name="id"/> directly, or null at the top level.</summary>
    public Block? Parent(string id) => PathTo(id) is { Length: > 1 } path ? At(path.AsSpan(..^1)) : null;

    /// <summary>Change one block in place. Does nothing when there is no such block.</summary>
    public void Update(string id, Action<Block> change)
    {
        if (Block(id) is { } block) change(block);
    }

    /// <summary>The list of blocks a path's parent holds: the top level for an empty path.</summary>
    internal List<Block> ChildrenAt(ReadOnlySpan<int> parent) => parent.IsEmpty ? Blocks : At(parent).Children;

    /// <summary>The list that holds the block at <paramref name="path"/>, and where in it the block is.</summary>
    internal (List<Block> List, int Index) Siblings(ReadOnlySpan<int> path) => (ChildrenAt(path[..^1]), path[^1]);

    // Order

    /// <summary>Every block, in the order it is read down the page.</summary>
    public IReadOnlyList<Block> AllBlocks => Within(Blocks);

    /// <summary>The blocks given and everything inside them, in order.</summary>
    internal static List<Block> Within(IEnumerable<Block> blocks)
    {
        var all = new List<Block>();

        void Walk(IEnumerable<Block> level)
        {
            foreach (var block in level)
            {
                all.Add(block);
                Walk(block.Children);
            }
        }

        Walk(blocks);
        return all;
    }

    /// <summary>
    /// The blocks the caret can stop at, in order down the page: those with
    /// text, and those that stand alone such as an image. Blocks that only
    /// hold others are not stops, and neither is anything folded away in a
    /// closed toggle.
    /// </summary>
    public IReadOnlyList<Block> Stops => StopsWithin(Blocks);

    internal static List<Block> StopsWithin(IEnumerable<Block> blocks)
    {
        var stops = new List<Block>();

        void Walk(IEnumerable<Block> level)
        {
            foreach (var block in level)
            {
                if (!block.IsContainer) stops.Add(block);
                Walk(Shown(block));
            }
        }

        Walk(blocks);
        return stops;
    }

    /// <summary>What a block shows of its children: a closed toggle shows only the first.</summary>
    private static IReadOnlyList<Block> Shown(Block block) =>
        block.Kind == BlockKind.Toggle && !block.IsOpen ? block.Children.Take(1).ToList() : block.Children;

    public Block? StopBefore(string id)
    {
        var stops = StopsWithin(Blocks);
        var index = stops.FindIndex(block => block.Id == id);
        return index > 0 ? stops[index - 1] : null;
    }

    public Block? StopAfter(string id)
    {
        var stops = StopsWithin(Blocks);
        var index = stops.FindIndex(block => block.Id == id);
        return index >= 0 && index + 1 < stops.Count ? stops[index + 1] : null;
    }

    /// <summary>True when <paramref name="id"/> is <paramref name="inside"/> or somewhere inside it.</summary>
    public bool IsBlock(string id, string inside) =>
        PathTo(inside) is { } outer && PathTo(id) is { } inner
        && inner.Length >= outer.Length && inner.AsSpan(0, outer.Length).SequenceEqual(outer);
}
