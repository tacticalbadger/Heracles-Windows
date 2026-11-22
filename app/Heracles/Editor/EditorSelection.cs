namespace Heracles.Editor;

/// <summary>
/// What is selected in an editor: nothing, text inside one block, or whole
/// blocks. A caret is a text selection with an empty range.
/// </summary>
internal abstract record EditorSelection
{
    public static EditorSelection None { get; } = new Nothing();

    public static EditorSelection Caret(string block, int offset) => new InText(block, offset, offset);

    public static EditorSelection Text(string block, int start, int end) => new InText(block, start, end);

    public static EditorSelection Blocks(IEnumerable<string> ids) => new OfBlocks([.. ids]);

    public sealed record Nothing : EditorSelection;

    /// <summary>Text inside one block, from <see cref="Start"/> up to <see cref="End"/>.</summary>
    public sealed record InText(string Block, int Start, int End) : EditorSelection
    {
        public bool IsCaret => Start == End;
    }

    /// <summary>Whole blocks, in document order. None is inside another.</summary>
    public sealed record OfBlocks(IReadOnlyList<string> Ids) : EditorSelection
    {
        public bool Equals(OfBlocks? other) => other is not null && Ids.SequenceEqual(other.Ids);

        public override int GetHashCode() => Ids.Aggregate(29, (hash, id) => HashCode.Combine(hash, id));
    }
}
