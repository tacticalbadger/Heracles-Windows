using System.Globalization;
using System.Text;

namespace Heracles.Editor;

/// <summary>What a block can be turned into.</summary>
internal readonly record struct BlockTarget
{
    /// <summary>The kind of block the target makes.</summary>
    public BlockKind Kind { get; }

    /// <summary>A heading's level. Nothing else has one.</summary>
    public int Level { get; }

    private BlockTarget(BlockKind kind, int level = 0)
    {
        Kind = kind;
        Level = level;
    }

    public static BlockTarget Paragraph => new(BlockKind.Paragraph);

    public static BlockTarget Heading(int level) => new(BlockKind.Heading, level);

    public static BlockTarget Bullet => new(BlockKind.Bullet);

    public static BlockTarget Numbered => new(BlockKind.Numbered);

    public static BlockTarget Todo => new(BlockKind.Todo);

    public static BlockTarget Quote => new(BlockKind.Quote);

    public static BlockTarget Callout => new(BlockKind.Callout);

    public static BlockTarget Toggle => new(BlockKind.Toggle);

    public static BlockTarget Code => new(BlockKind.Code);

    public static BlockTarget Divider => new(BlockKind.Divider);
}

/// <summary>Which side of a block a dragged block lands on.</summary>
internal enum DropSide
{
    Before,
    After,

    // Beside the block, as a column.
    LeftOf,
    RightOf,
}

/// <summary>Where a dragged block lands, relative to another block.</summary>
internal readonly record struct DropTarget(DropSide Side, string BlockId)
{
    public static DropTarget Before(string id) => new(DropSide.Before, id);

    public static DropTarget After(string id) => new(DropSide.After, id);

    public static DropTarget LeftOf(string id) => new(DropSide.LeftOf, id);

    public static DropTarget RightOf(string id) => new(DropSide.RightOf, id);

    /// <summary>True when the block lands beside the other, as a column.</summary>
    public bool IsBeside => Side is DropSide.LeftOf or DropSide.RightOf;
}

/// <summary>
/// The edits a note's blocks can undergo. Each is a plain change to the
/// tree, with no view involved, and says where the selection should go
/// afterwards.
///
/// Translated from the Mac's <c>EditorDocument+Editing.swift</c>. There a
/// block is a value and every assignment copies it. Here a block is the
/// block in the document, so an edit changes it where it is, and where the
/// Mac reads a copy taken before a change (a length, a list of children)
/// the value is read before the change is made.
/// </summary>
internal sealed partial class EditorDocument
{
    private static bool IsWrapper(BlockKind kind) => kind is BlockKind.Quote or BlockKind.Callout or BlockKind.Toggle;

    private static EditorSelection AtStart(Block block) =>
        block.HasText ? EditorSelection.Caret(block.Id, 0) : EditorSelection.Blocks([block.Id]);

    private static EditorSelection AtEnd(Block block) =>
        block.HasText ? EditorSelection.Caret(block.Id, block.Text.Length) : EditorSelection.Blocks([block.Id]);

    // Putting blocks in and taking them out

    /// <summary>Take a block out, with everything inside it.</summary>
    internal Block? Take(string id)
    {
        if (PathTo(id) is not { } path) return null;
        var (siblings, index) = Siblings(path);
        var taken = siblings[index];
        siblings.RemoveAt(index);
        return taken;
    }

    public bool InsertAfter(IEnumerable<Block> blocks, string id)
    {
        if (PathTo(id) is not { } path) return false;
        var (siblings, index) = Siblings(path);
        siblings.InsertRange(index + 1, blocks);
        return true;
    }

    public bool InsertBefore(IEnumerable<Block> blocks, string id)
    {
        if (PathTo(id) is not { } path) return false;
        var (siblings, index) = Siblings(path);
        siblings.InsertRange(index, blocks);
        return true;
    }

    /// <summary>Copies of blocks in which every block has a new id. Nothing is shared with the originals.</summary>
    internal static List<Block> Fresh(IEnumerable<Block> blocks, BlockIds ids)
    {
        var copies = new List<Block>();
        foreach (var block in blocks)
        {
            var copy = block.CopyHolding([]);
            copy.Id = ids.Make();
            if (block.IsListItem) copy.ParagraphId = ids.Make();
            if (block.Raw is JsonObject raw && raw.Members.GetValueOrDefault("attrs") is JsonObject attrs
                && attrs.Members.ContainsKey("blockId"))
            {
                var renamed = new JsonObject(attrs.Members.SetItem("blockId", JsonValue.Of(copy.Id)));
                copy.Raw = new JsonObject(raw.Members.SetItem("attrs", renamed));
            }
            copy.Children = Fresh(block.Children, ids);
            copies.Add(copy);
        }
        return copies;
    }

    /// <summary>The column a block is in, if it is in one.</summary>
    private Block? ColumnHolding(string id)
    {
        if (PathTo(id) is not { } path) return null;
        for (var length = path.Length - 1; length > 0; length--)
        {
            var ancestor = At(path.AsSpan(0, length));
            if (ancestor.Kind == BlockKind.Column) return ancestor;
        }
        return null;
    }

    // Keeping the tree valid

    /// <summary>
    /// Remove what an edit can leave behind: containers with nothing in
    /// them, rows with fewer than two columns, and blocks inside a block
    /// that cannot hold any. The lists of blocks are made anew, so a list
    /// read before this is not the document's afterwards.
    /// </summary>
    internal void Tidy()
    {
        static List<Block> Clean(List<Block> blocks, bool inRow, bool inColumn)
        {
            var kept = new List<Block>();
            foreach (var block in blocks)
            {
                block.Children = Clean(
                    block.Children, block.Kind == BlockKind.Columns, inColumn || block.Kind == BlockKind.Column);
                switch (block.Kind)
                {
                    case BlockKind.Quote:
                    case BlockKind.Callout:
                    case BlockKind.Toggle:
                        if (block.Children.Count > 0) kept.Add(block);
                        break;
                    case BlockKind.Column:
                        if (!inRow) kept.AddRange(block.Children);
                        else if (block.Children.Count > 0) kept.Add(block);
                        break;
                    case BlockKind.Columns:
                        var columns = block.Children.Where(child => child.Kind == BlockKind.Column).ToList();
                        if (columns.Count >= 2 && !inColumn)
                        {
                            block.Children = columns;
                            kept.Add(block);
                        }
                        else
                        {
                            // One column is not a row, and a row cannot sit in a column.
                            kept.AddRange(columns.SelectMany(column => column.Children));
                        }
                        break;
                    case BlockKind.Bullet:
                    case BlockKind.Numbered:
                    case BlockKind.Todo:
                        kept.Add(block);
                        break;
                    default:
                        var children = block.Children;
                        block.Children = [];
                        kept.Add(block);
                        kept.AddRange(children);
                        break;
                }
            }
            return kept;
        }

        Blocks = Clean(Blocks, inRow: false, inColumn: false);
    }

    /// <summary><see cref="Tidy"/>, and a note is never left with no blocks at all.</summary>
    public void Normalize(BlockIds ids)
    {
        Tidy();
        if (Blocks.Count == 0) Blocks.Add(new Block(ids.Make(), BlockKind.Paragraph));
    }

    // Return

    /// <summary>
    /// Split a block where the caret is, as Return does. Text that is
    /// selected, from <paramref name="start"/> up to <paramref name="end"/>,
    /// is replaced by the split.
    /// </summary>
    public EditorSelection Split(string id, int start, int end, BlockIds ids)
    {
        if (PathTo(id) is not { } path) return EditorSelection.None;
        var block = At(path);
        if (!block.HasText || block.Kind == BlockKind.Code) return EditorSelection.None;
        block.Text = block.Text.Replace(start, end, new RichText());
        var offset = Math.Clamp(start, 0, block.Text.Length);
        var (siblings, index) = Siblings(path);
        var parent = path.Length > 1 ? At(path.AsSpan(..^1)) : null;

        if (block.Text.IsEmpty && block.Children.Count == 0)
        {
            // Return on an empty item ends the list, one level at a time.
            if (block.IsListItem)
            {
                if (parent?.IsListItem == true)
                {
                    Outdent(id);
                    return EditorSelection.Caret(id, 0);
                }
                return Turn(id, BlockTarget.Paragraph, ids);
            }
            // Return on an empty last line leaves what the line is inside.
            if (block.Kind == BlockKind.Paragraph && index == siblings.Count - 1 && parent is not null
                && (parent.IsListItem || IsWrapper(parent.Kind)))
            {
                Take(id);
                InsertAfter([block], parent.Id);
                Normalize(ids);
                return EditorSelection.Caret(id, 0);
            }
        }

        var added = new Block(ids.Make(), block.Kind, attrs: block.Attrs);
        if (block.IsListItem) added.ParagraphId = ids.Make();
        if (block.Kind == BlockKind.Todo) added.IsChecked = false;

        if (offset == 0 && !block.Text.IsEmpty)
        {
            // At the start, the text keeps its block, so a link to the
            // block still lands on the text. The empty block goes above.
            if (block.Kind == BlockKind.Heading)
            {
                added.Kind = BlockKind.Paragraph;
                added.Attrs = [];
            }
            siblings.Insert(index, added);
            return EditorSelection.Caret(id, 0);
        }

        added.Text = block.Text.Subtext(offset, block.Text.Length);
        block.Text = block.Text.Subtext(0, offset);
        if (block.Kind == BlockKind.Heading && added.Text.IsEmpty)
        {
            added.Kind = BlockKind.Paragraph;
            added.Attrs = [];
        }
        if (block.IsListItem)
        {
            added.Children = block.Children;
            block.Children = [];
        }
        siblings.Insert(index + 1, added);
        return EditorSelection.Caret(added.Id, 0);
    }

    // Delete

    /// <summary>
    /// Backspace with the caret at the start of a block. Null when there
    /// is nothing for it to do.
    /// </summary>
    public EditorSelection? DeleteBackward(string id, BlockIds ids)
    {
        if (PathTo(id) is not { } path) return null;
        var block = At(path);
        if (!block.HasText) return null;
        if (block.Kind == BlockKind.Code) return block.Text.IsEmpty ? Turn(id, BlockTarget.Paragraph, ids) : null;
        // First the block stops being a heading or a list item.
        if (block.Kind != BlockKind.Paragraph) return Turn(id, BlockTarget.Paragraph, ids);
        // Then it leaves the quote, callout or toggle it begins.
        if (path.Length > 1 && path[^1] == 0)
        {
            var parent = At(path.AsSpan(..^1));
            if (IsWrapper(parent.Kind))
            {
                Take(id);
                InsertBefore([block], parent.Id);
                Normalize(ids);
                return EditorSelection.Caret(id, 0);
            }
        }
        // Then it joins whatever is above it.
        if (StopBefore(id) is not { } previous) return null;
        if (previous.Kind == BlockKind.Code || !previous.HasText)
        {
            if (block.Text.IsEmpty)
            {
                Take(id);
                Normalize(ids);
            }
            // Text is never poured into code, and a block without text is
            // selected so that a second Backspace removes it.
            return AtEnd(previous);
        }
        var join = previous.Text.Length;
        Take(id);
        previous.Text += block.Text;
        Normalize(ids);
        return EditorSelection.Caret(previous.Id, join);
    }

    /// <summary>Forward delete with the caret at the end of a block.</summary>
    public EditorSelection? DeleteForward(string id)
    {
        if (Block(id) is not { } block || !block.HasText || block.Kind == BlockKind.Code) return null;
        if (StopAfter(id) is not { } next || next.Kind == BlockKind.Code) return null;
        if (!next.HasText || PathTo(next.Id) is not { } nextPath) return EditorSelection.Blocks([next.Id]);
        var join = block.Text.Length;
        // What was inside the joined block stays where that block was.
        var (siblings, index) = Siblings(nextPath);
        siblings.RemoveAt(index);
        siblings.InsertRange(index, next.Children);
        block.Text += next.Text;
        Tidy();
        return EditorSelection.Caret(id, join);
    }

    // Indenting

    /// <summary>Move a list item under the item before it. False when it cannot be.</summary>
    public bool Indent(string id)
    {
        if (PathTo(id) is not { } path || !At(path).IsListItem) return false;
        var (siblings, index) = Siblings(path);
        if (index == 0 || !siblings[index - 1].IsListItem) return false;
        var moved = siblings[index];
        siblings.RemoveAt(index);
        moved.StartsList = false;
        siblings[index - 1].Children.Add(moved);
        return true;
    }

    /// <summary>
    /// Move a block out of the list item it is in, to follow that item.
    /// The items below it go along as its children, so nothing changes
    /// order on the page.
    /// </summary>
    public bool Outdent(string id)
    {
        if (PathTo(id) is not { Length: >= 2 } path) return false;
        var parentPath = path.AsSpan(..^1);
        var parent = At(parentPath);
        if (!parent.IsListItem) return false;
        var index = path[^1];
        var block = parent.Children[index];
        var following = parent.Children.GetRange(index + 1, parent.Children.Count - index - 1);
        if (block.IsListItem)
        {
            block.Children.AddRange(following);
            parent.Children.RemoveRange(index, parent.Children.Count - index);
        }
        else
        {
            if (following.Count > 0) return false;
            parent.Children.RemoveAt(index);
        }
        block.StartsList = false;
        var (siblings, parentIndex) = Siblings(parentPath);
        siblings.Insert(parentIndex + 1, block);
        return true;
    }

    // Turning a block into another kind

    public EditorSelection Turn(string id, BlockTarget into, BlockIds ids)
    {
        if (PathTo(id) is not { } path) return EditorSelection.None;
        var block = At(path);
        var (siblings, index) = Siblings(path);

        if (block.IsContainer)
        {
            if (block.Kind is BlockKind.Columns or BlockKind.Column) return EditorSelection.None;
            var inside = StopsWithin(block.Children).FirstOrDefault();
            if (IsWrapper(into.Kind))
            {
                block.Kind = into.Kind;
                block.Attrs = [];
            }
            else
            {
                // Anything else lets the blocks inside out.
                siblings.RemoveAt(index);
                siblings.InsertRange(index, block.Children);
            }
            return inside is null ? EditorSelection.None : AtStart(inside);
        }
        if (!block.HasText) return EditorSelection.None;

        if (IsWrapper(into.Kind))
        {
            siblings[index] = new Block(ids.Make(), into.Kind, children: [block]);
            return EditorSelection.Caret(id, 0);
        }

        switch (into.Kind)
        {
            case BlockKind.Divider:
                var made = ids.Make();
                if (block.Kind != BlockKind.Code && block.Text.IsEmpty && block.Children.Count == 0)
                {
                    siblings[index] = new Block(id, BlockKind.Divider);
                    siblings.Insert(index + 1, new Block(made, BlockKind.Paragraph));
                    return EditorSelection.Caret(made, 0);
                }
                // A block with text is kept, and the divider goes below it.
                var below = new Block(ids.Make(), BlockKind.Paragraph);
                siblings.InsertRange(index + 1, [new Block(made, BlockKind.Divider), below]);
                return EditorSelection.Caret(below.Id, 0);
            case BlockKind.Code:
                siblings[index] = new Block(id, BlockKind.Code, new RichText(block.Text.String));
                siblings.InsertRange(index + 1, block.Children);
                break;
            default:
                block.Kind = into.Kind;
                block.Attrs = [];
                block.StartsList = false;
                block.ListAttrs = [];
                if (into.Kind == BlockKind.Heading) block.Level = into.Level;
                if (block.IsListItem)
                {
                    block.ParagraphId ??= ids.Make();
                }
                else
                {
                    // Only a list item can hold blocks. The rest follow it.
                    block.ParagraphId = null;
                    siblings.InsertRange(index + 1, block.Children);
                    block.Children = [];
                }
                break;
        }
        return EditorSelection.Caret(id, 0);
    }

    // Removing and copying

    /// <summary>Remove blocks, with everything inside them.</summary>
    public EditorSelection Remove(IEnumerable<string> removing, BlockIds ids)
    {
        var present = removing.Where(Contains).ToList();
        if (present.Count == 0) return EditorSelection.None;
        var before = StopsWithin(Blocks);
        var going = Within(present.Select(Block).OfType<Block>()).Select(block => block.Id).ToHashSet();
        var first = before.FindIndex(block => going.Contains(block.Id));
        var last = before.FindLastIndex(block => going.Contains(block.Id));
        var above = first > 0 ? before[first - 1] : null;
        var below = last >= 0 && last + 1 < before.Count ? before[last + 1] : null;
        foreach (var id in present) Take(id);
        Normalize(ids);

        if (above is not null && Contains(above.Id)) return AtEnd(above);
        if (below is not null && Contains(below.Id)) return AtStart(below);
        return StopsWithin(Blocks).FirstOrDefault() is { } stop ? AtStart(stop) : EditorSelection.None;
    }

    /// <summary>Put a copy of a block after it. Returns the copy's id.</summary>
    public string? Duplicate(string id, BlockIds ids)
    {
        if (Block(id) is not { } block) return null;
        var copy = Fresh([block], ids)[0];
        copy.StartsList = false;
        InsertAfter([copy], id);
        return copy.Id;
    }

    // Moving

    /// <summary>
    /// Move blocks to a new place. False, with nothing changed, when the
    /// move makes no sense: into itself, or a row of columns into a column.
    /// </summary>
    public bool Move(IEnumerable<string> moving, DropTarget target, BlockIds ids)
    {
        // An id given twice is one block: put in twice it would be in two places.
        var moved = moving.Distinct().ToList();
        var targetId = target.BlockId;
        if (moved.Count == 0 || !moved.All(Contains) || PathTo(targetId) is not { } targetPath
            || moved.Any(id => IsBlock(targetId, inside: id)))
        {
            return false;
        }
        // A block inside another that is moving goes along with it.
        var blocks = moved.Where(id => !moved.Any(other => other != id && IsBlock(id, inside: other)))
            .Select(Block).OfType<Block>().ToList();
        if (blocks.Any(block => block.Kind == BlockKind.Column)) return false;
        var holdsRow = Within(blocks).Any(block => block.Kind == BlockKind.Columns);
        var column = ColumnHolding(targetId);
        if (target.IsBeside)
        {
            if (column is null && targetPath.Length > 1)
            {
                // Only a block at the top, or one in a column, can have a
                // column beside it. Elsewhere the block lands below.
                target = DropTarget.After(targetId);
            }
            else if (holdsRow)
            {
                return false;
            }
        }
        else if (column is not null && holdsRow)
        {
            return false;
        }

        foreach (var block in blocks)
        {
            Take(block.Id);
            block.StartsList = false;
        }
        switch (target.Side)
        {
            case DropSide.Before:
                InsertBefore(blocks, targetId);
                break;
            case DropSide.After:
                InsertAfter(blocks, targetId);
                break;
            default:
                var isLeft = target.Side == DropSide.LeftOf;
                if (column is not null)
                {
                    var added = new Block(ids.Make(), BlockKind.Column, children: blocks);
                    if (isLeft) InsertBefore([added], column.Id);
                    else InsertAfter([added], column.Id);
                }
                else if (PathTo(targetId) is { } path)
                {
                    var beside = At(path);
                    if (beside.Kind == BlockKind.Columns)
                    {
                        var added = new Block(ids.Make(), BlockKind.Column, children: blocks);
                        beside.Children.Insert(isLeft ? 0 : beside.Children.Count, added);
                    }
                    else
                    {
                        var row = new Block(ids.Make(), BlockKind.Columns);
                        var first = new Block(ids.Make(), BlockKind.Column, children: isLeft ? blocks : [beside]);
                        var second = new Block(ids.Make(), BlockKind.Column, children: isLeft ? [beside] : blocks);
                        row.Children = [first, second];
                        var (siblings, index) = Siblings(path);
                        siblings[index] = row;
                    }
                }
                break;
        }
        Normalize(ids);
        return true;
    }

    // Typing shortcuts

    /// <summary>
    /// Turn a paragraph into another kind of block when what has just been
    /// typed at its start is one of the Markdown beginnings: <c># </c>,
    /// <c>- </c>, <c>1. </c>, <c>[] </c>, <c>&gt; </c>, three backticks, or
    /// <c>---</c>. <paramref name="caret"/> is where the caret is, which
    /// must be right after the beginning.
    /// </summary>
    public EditorSelection? ApplyBlockShortcut(string id, int caret, BlockIds ids)
    {
        if (Block(id) is not { Kind: BlockKind.Paragraph } block) return null;
        var text = block.Text.String;
        if (caret <= 0 || caret > text.Length) return null;
        var typed = text[..caret];

        // On a Mac two dashes become a long one as they are typed, when the
        // system is set to make smart dashes. A note begun there is read
        // the same way here.
        if ((typed is "---" or "—-") && text.Length == caret)
        {
            block.Text = new RichText();
            return Turn(id, BlockTarget.Divider, ids);
        }
        if (!typed.EndsWith(' ')) return null;
        var start = typed[..^1];

        BlockTarget target;
        var isChecked = false;
        int? number = null;
        string? language = null;
        switch (start)
        {
            case "#" or "##" or "###" or "####" or "#####" or "######":
                target = BlockTarget.Heading(start.Length);
                break;
            case "-" or "*" or "+":
                target = BlockTarget.Bullet;
                break;
            case "[]" or "[ ]":
                target = BlockTarget.Todo;
                break;
            case "[x]" or "[X]":
                target = BlockTarget.Todo;
                isChecked = true;
                break;
            case ">":
                target = BlockTarget.Quote;
                break;
            default:
                // Digits only: the Mac's reading also takes a sign, which
                // makes a list of "+1. ". That is not carried over.
                if (start.Length is >= 2 and <= 10 && start[^1] is '.' or ')'
                    && int.TryParse(start.AsSpan(..^1), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                {
                    target = BlockTarget.Numbered;
                    number = value;
                }
                else if (start.StartsWith("```", StringComparison.Ordinal) && start[3..].EnumerateRunes().All(IsLanguageLetter))
                {
                    target = BlockTarget.Code;
                    language = start[3..];
                }
                else
                {
                    return null;
                }
                break;
        }

        block.Text = block.Text.Replace(0, caret, new RichText());
        Turn(id, target, ids);
        // Turning a block can put another in its place, so it is found again.
        Update(id, turned =>
        {
            if (isChecked) turned.IsChecked = true;
            if (number is { } first && first != 1) turned.ListAttrs["start"] = JsonValue.Of(first);
            if (language is { Length: > 0 }) turned.Language = language.ToLowerInvariant();
        });
        return EditorSelection.Caret(id, 0);
    }

    private static bool IsLanguageLetter(Rune rune) =>
        Rune.IsLetter(rune) || Rune.IsNumber(rune) || (rune.IsBmp && "+#_-".Contains((char)rune.Value));

    // Pasting

    /// <summary>
    /// Put blocks in at the selection. One paragraph goes into the line
    /// being typed. Anything more goes below the block, or takes the place
    /// of an empty one or of selected blocks. Pasted blocks get new ids,
    /// and the blocks handed in are left as they were.
    /// </summary>
    public EditorSelection Paste(IReadOnlyList<Block> pasted, EditorSelection selection, BlockIds ids)
    {
        if (pasted.Count == 0) return selection;

        // Where the caret goes afterwards is read from the new blocks before
        // they go in: tidying the note may rearrange them.
        static EditorSelection EndOf(List<Block> blocks) =>
            StopsWithin(blocks).LastOrDefault() is { } last ? AtEnd(last) : EditorSelection.None;

        switch (selection)
        {
            case EditorSelection.OfBlocks(var selected):
            {
                if (selected.FirstOrDefault(Contains) is not { } first) return selection;
                var fresh = Fresh(pasted, ids);
                var after = EndOf(fresh);
                InsertBefore(fresh, first);
                foreach (var id in selected) Take(id);
                Normalize(ids);
                return after;
            }
            case EditorSelection.InText(var id, var start, var end):
            {
                if (Block(id) is not { } block) return selection;
                // A range that has gone stale is brought back into the text.
                var length = block.Text.Length;
                var lower = Math.Clamp(start, 0, length);
                var upper = Math.Clamp(end, lower, length);
                if (block.Kind == BlockKind.Code)
                {
                    // Code takes only the words.
                    var words = string.Join("\n", Within(pasted).Where(inner => inner.HasText).Select(inner => inner.Text.String));
                    block.Text = block.Text.Replace(lower, upper, new RichText(words));
                    return EditorSelection.Caret(id, lower + words.Length);
                }
                if (pasted.Count == 1 && pasted[0].Kind == BlockKind.Paragraph && block.HasText)
                {
                    block.Text = block.Text.Replace(lower, upper, pasted[0].Text);
                    return EditorSelection.Caret(id, lower + pasted[0].Text.Length);
                }
                var fresh = Fresh(pasted, ids);
                var after = EndOf(fresh);
                block.Text = block.Text.Replace(lower, upper, new RichText());
                if (block.Kind == BlockKind.Paragraph && length == upper - lower && block.Children.Count == 0)
                {
                    InsertBefore(fresh, id);
                    Take(id);
                }
                else
                {
                    InsertAfter(fresh, id);
                }
                Normalize(ids);
                return after;
            }
            default:
            {
                var fresh = Fresh(pasted, ids);
                var after = EndOf(fresh);
                Blocks.AddRange(fresh);
                Normalize(ids);
                return after;
            }
        }
    }

    // Selecting whole blocks

    /// <summary>
    /// The blocks to select when a selection runs from one block to
    /// another, in either direction. A block stands for everything inside
    /// it, and a container all of whose blocks are selected is selected in
    /// their place. (The Mac's <c>blocks(from:to:)</c>.)
    /// </summary>
    public IReadOnlyList<string> BlocksBetween(string anchor, string head)
    {
        var order = StopsWithin(Blocks).ConvertAll(block => block.Id);
        var first = order.IndexOf(anchor);
        var second = order.IndexOf(head);
        if (first < 0 || second < 0) return [];
        var from = Math.Min(first, second);
        var chosen = order.GetRange(from, Math.Max(first, second) - from + 1).ToHashSet();

        List<string> Reduce(IReadOnlyList<Block> blocks)
        {
            var selected = new List<string>();
            foreach (var block in blocks)
            {
                if (chosen.Contains(block.Id))
                {
                    selected.Add(block.Id);
                    continue;
                }
                var shown = Shown(block);
                var inside = Reduce(shown);
                if (block.IsContainer && shown.Count > 0 && inside.SequenceEqual(shown.Select(inner => inner.Id)))
                {
                    selected.Add(block.Id);
                }
                else
                {
                    selected.AddRange(inside);
                }
            }
            return selected;
        }

        return Reduce(Blocks);
    }

    /// <summary>
    /// Blocks as plain text, in Markdown's way of writing them, for the
    /// clipboard.
    /// </summary>
    public string PlainText(IEnumerable<string> ids) =>
        string.Join("\n", Lines(ids.Select(Block).OfType<Block>().ToList(), ""));

    internal static List<string> Lines(IReadOnlyList<Block> blocks, string indent)
    {
        var lines = new List<string>();
        var number = 0;

        void Add(string prefix, string text)
        {
            // A line break inside a block starts a new line at the same depth.
            var parts = text.Split('\n');
            for (var part = 0; part < parts.Length; part++)
            {
                lines.Add(indent + (part == 0 ? prefix : new string(' ', prefix.Length)) + parts[part]);
            }
        }

        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            var text = block.Text.String;
            switch (block.Kind)
            {
                case BlockKind.Paragraph:
                    Add("", text);
                    break;
                case BlockKind.Heading:
                    Add(new string('#', block.Level) + " ", text);
                    break;
                case BlockKind.Bullet:
                    Add("- ", text);
                    break;
                case BlockKind.Todo:
                    Add(block.IsChecked ? "- [x] " : "- [ ] ", text);
                    break;
                case BlockKind.Numbered:
                    var continues = index > 0 && blocks[index - 1].Kind == BlockKind.Numbered && !block.StartsList;
                    number = continues ? number + 1 : block.ListAttrs.GetValueOrDefault("start")?.IntValue ?? 1;
                    Add($"{number}. ", text);
                    break;
                case BlockKind.Code:
                    lines.Add(indent + "```" + (block.Language == "plaintext" ? "" : block.Language));
                    lines.AddRange(text.Split('\n').Select(line => indent + line));
                    lines.Add(indent + "```");
                    break;
                case BlockKind.Divider:
                    lines.Add(indent + "---");
                    break;
                case BlockKind.Image:
                    lines.Add($"{indent}![{block.Attrs.GetValueOrDefault("alt")?.StringValue ?? ""}]({block.ImageSource ?? ""})");
                    break;
                case BlockKind.Quote:
                case BlockKind.Callout:
                    lines.AddRange(Lines(block.Children, indent + "> "));
                    break;
                case BlockKind.Toggle:
                case BlockKind.Columns:
                case BlockKind.Column:
                    lines.AddRange(Lines(block.Children, indent));
                    break;
            }
            if (block.IsListItem) lines.AddRange(Lines(block.Children, indent + "  "));
        }
        return lines;
    }
}

/// <summary>What typing can do to a block's text by itself: Markdown marks, and web addresses.</summary>
internal sealed partial class RichText
{
    private static readonly (string Mark, TextMark Kind)[] InlineRules =
    [
        ("**", TextMark.Bold), ("__", TextMark.Bold), ("~~", TextMark.Strike),
        ("*", TextMark.Italic), ("_", TextMark.Italic), ("`", TextMark.Code),
    ];

    /// <summary>
    /// Turn a pair of Markdown marks into the mark they stand for, when the
    /// closing one has just been typed: <c>**bold**</c>, <c>*italic*</c>,
    /// <c>`code`</c>, <c>~~strike~~</c>, and the underscore forms.
    /// <paramref name="caret"/> is where the caret is, right after the
    /// closing mark. Null when there is no such pair.
    /// </summary>
    public (RichText Text, int Caret, TextMark Mark)? ApplyingInlineShortcut(int caret)
    {
        var before = String[..Math.Clamp(caret, 0, Length)];
        foreach (var (mark, kind) in InlineRules)
        {
            var width = mark.Length;
            if (before.Length <= width * 2 || !before.EndsWith(mark, StringComparison.Ordinal)) continue;
            var close = before.Length - width;
            var open = before.LastIndexOf(mark, close - 1, close, StringComparison.Ordinal);
            if (open < 0 || open + width >= close) continue;
            var content = before[(open + width)..close];
            if (char.IsWhiteSpace(content[0]) || char.IsWhiteSpace(content[^1]) || content.Contains(mark[0])
                || content.Contains('\n'))
            {
                continue;
            }
            // The pair must begin a word: "2 * 3 * 4" and "snake_case_name"
            // are not marks. Code only must not sit against another backtick.
            if (open > 0)
            {
                var previous = before[open - 1];
                if (mark == "`" ? previous == '`' : !char.IsWhiteSpace(previous)) continue;
            }
            if (Subtext(open, before.Length).spans.Any(span => span.Style.Code || span.Atom is not null)) continue;
            var marked = Subtext(open + width, close);
            marked = marked.Set(kind, true, 0, marked.Length);
            return (Replace(open, before.Length, marked), open + marked.Length, kind);
        }
        return null;
    }

    /// <summary>True for text that is one web address and nothing else.</summary>
    public static bool IsWebAddress(string text)
    {
        if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return !text.Any(char.IsWhiteSpace) && Uri.TryCreate(text, UriKind.Absolute, out var address) && address.Host.Length > 0;
    }

    /// <summary>
    /// Link a web address that has just been typed, when the space after
    /// it is. <paramref name="caret"/> is where the caret is, right after
    /// that space. Null when the word before it is not an address, or is a
    /// link already.
    /// </summary>
    public RichText? Autolinked(int caret)
    {
        var before = String[..Math.Clamp(caret, 0, Length)];
        if (before.Length < 2 || !before.EndsWith(' ')) return null;
        var end = before.Length - 1;
        var start = end;
        while (start > 0 && !char.IsWhiteSpace(before[start - 1])) start--;
        if (start >= end) return null;
        // A full stop or a comma after an address is the sentence's, not its own.
        var word = before[start..end].TrimEnd('.', ',', ';', ':', '!', '?', ')');
        var wordEnd = start + word.Length;
        if (!IsWebAddress(word) || Subtext(start, wordEnd).spans.Any(span => span.Style.Link is not null || span.Style.Code))
        {
            return null;
        }
        return SetLink(new TextLink(word), start, wordEnd);
    }
}
