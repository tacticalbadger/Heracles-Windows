namespace Heracles.Editor;

/// <summary>
/// Finding blocks in the tree. The rest of the Mac's
/// <c>EditorDocument+Tree.swift</c> (paths, parents, the order blocks are
/// walked in) is translated with the edits that use it.
/// </summary>
internal sealed partial class EditorDocument
{
    /// <summary>Every block, parents before their children, in document order.</summary>
    public IEnumerable<Block> AllBlocks => Walk(Blocks);

    private static IEnumerable<Block> Walk(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;
            foreach (var inner in Walk(block.Children)) yield return inner;
        }
    }

    public Block? Block(string id) => AllBlocks.FirstOrDefault(block => block.Id == id);

    public bool Contains(string id) => Block(id) is not null;
}
